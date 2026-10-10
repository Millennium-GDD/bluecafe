using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlueCafe.Unity
{
    // On-demand immutable resources, scoped to one validated pack. Live geometry is
    // pinned; unused resources are released after each projection. No ever-growing cache.
    internal sealed class RuntimeResources : IDisposable
    {
        sealed class Entry<T> where T : UnityEngine.Object
        {
            public T Value;
            public long Bytes;
        }
        readonly Pack pack;
        readonly long budget;
        readonly Dictionary<string, Entry<Mesh>> meshes = new Dictionary<string, Entry<Mesh>>();
        readonly Dictionary<string, Entry<Texture2D>> textures = new Dictionary<string, Entry<Texture2D>>();
        readonly Dictionary<string, Entry<Material>> materials = new Dictionary<string, Entry<Material>>();
        public long Bytes { get; private set; }
        public int MeshCount => meshes.Count;
        public int TextureCount => textures.Count;
        public int MaterialCount => materials.Count;

        public RuntimeResources(Pack pack, long budget) { this.pack = pack; this.budget = budget; }
        void Reserve(long bytes)
        {
            if (bytes < 0 || bytes > budget - Bytes)
                throw new InvalidDataException("RESOURCE_BUDGET: original assets exceed the runtime resource budget; reduce the scene or increase the configured budget.");
        }
        public Mesh Mesh(string path)
        {
            path = pack.Resolve(path);
            if (meshes.TryGetValue(path, out var entry)) return entry.Value;
            if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("MESH_TOO_LARGE: " + path);
            var data = Pack.ReadMesh(path);
            long bytes = (long)data.vertices.Length * sizeof(float) + (data.normals?.LongLength ?? 0) * sizeof(float) + (data.uv?.LongLength ?? 0) * sizeof(float);
            foreach (var sub in data.submeshes) bytes += sub.triangles.LongLength * sizeof(int);
            // CPU-readable source mesh plus GPU buffers; retained for original-geometry picking.
            bytes *= 2;
            Reserve(bytes);
            var mesh = new Mesh { name = data.name ?? Path.GetFileNameWithoutExtension(path), indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            try
            {
                var vertices = new Vector3[data.vertices.Length / 3];
                for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.vertices[i * 3], data.vertices[i * 3 + 1], data.vertices[i * 3 + 2]);
                mesh.vertices = vertices;
                if (data.normals != null && data.normals.Length != 0)
                {
                    var normals = new Vector3[vertices.Length];
                    for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]);
                    mesh.normals = normals;
                }
                if (data.uv != null && data.uv.Length != 0)
                {
                    var uv = new Vector2[vertices.Length];
                    for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(data.uv[i * 2], data.uv[i * 2 + 1]);
                    mesh.uv = uv;
                }
                mesh.subMeshCount = data.submeshes.Length;
                for (int i = 0; i < data.submeshes.Length; i++) mesh.SetTriangles(data.submeshes[i].triangles, i, false);
                mesh.RecalculateBounds();
                meshes.Add(path, new Entry<Mesh> { Value = mesh, Bytes = bytes }); Bytes += bytes;
                return mesh;
            }
            catch { Release(mesh); throw; }
        }
        Texture2D Texture(string path)
        {
            path = pack.Resolve(path);
            if (textures.TryGetValue(path, out var entry)) return entry.Value;
            var info = new FileInfo(path);
            if (info.Length > 64L * 1024 * 1024) throw new InvalidDataException("TEXTURE_TOO_LARGE: " + path);
            var png = File.ReadAllBytes(path);
            if (png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 || png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10)
                throw new InvalidDataException("INVALID_TEXTURE: expected original PNG: " + path);
            long width = ReadBigEndian(png, 16), height = ReadBigEndian(png, 20);
            if (width < 1 || height < 1 || width > 8192 || height > 8192) throw new InvalidDataException("INVALID_TEXTURE: PNG dimensions exceed supported limits.");
            long bytes = width * height * 4;
            Reserve(bytes);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(path), hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            try
            {
                if (!texture.LoadImage(png, true)) throw new InvalidDataException("INVALID_TEXTURE: PNG decode failed: " + path);
                textures.Add(path, new Entry<Texture2D> { Value = texture, Bytes = bytes }); Bytes += bytes;
                return texture;
            }
            catch { Release(texture); throw; }
        }
        static long ReadBigEndian(byte[] bytes, int offset) => ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) | ((long)bytes[offset + 2] << 8) | bytes[offset + 3];
        public Material Material(MaterialSpec spec, Item item)
        {
            if (spec == null) throw new InvalidDataException("MATERIAL_UNAVAILABLE: material specification missing.");
            var key = (RuntimeSurfaceRules.IsAuditedSurface(item) ? "surface:" + item.id + ":" : item.placement == "wallpaper" ? "wallpaper:" : "default:") + JsonUtility.ToJson(spec);
            if (materials.TryGetValue(key, out var existing)) return existing.Value;
            var shader = Shader.Find("BlueCafe/BakedTexture");
            if (!shader || !shader.isSupported) throw new InvalidDataException("SHADER_UNAVAILABLE: BlueCafe/BakedTexture must be included in the Player.");
            // Resolve original texture before creating any material: no invented visual fallback.
            var texture = string.IsNullOrEmpty(spec.texturePath) ? null : Texture(spec.texturePath);
            Reserve(1024);
            var material = new Material(shader) { name = spec.name ?? "Cafe material", hideFlags = HideFlags.DontSave };
            try
            {
                if (texture) material.mainTexture = texture;
                if (spec.textureScale?.Length == 2) material.mainTextureScale = new Vector2(spec.textureScale[0], spec.textureScale[1]);
                if (spec.textureOffset?.Length == 2) material.mainTextureOffset = new Vector2(spec.textureOffset[0], spec.textureOffset[1]);
                material.SetFloat("_Cutoff", spec.cutoff > 0 ? spec.cutoff : .08f);
                bool usesOpacity = spec.transparent || (spec.color?.Length == 4 && spec.color[3] < .999f);
                material.SetFloat("_UseAlpha", usesOpacity ? 1 : 0);
                if (usesOpacity)
                {
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0); material.SetFloat("_Cutoff", .005f); material.renderQueue = 3000;
                }
                if (spec.color?.Length == 4)
                {
                    float peak = Mathf.Max(1, Mathf.Max(spec.color[0], Mathf.Max(spec.color[1], spec.color[2])));
                    material.color = new Color(spec.color[0] / peak, spec.color[1] / peak, spec.color[2] / peak, spec.color[3]);
                }
                RuntimeSurfaceRules.Apply(item, spec, material);
                materials.Add(key, new Entry<Material> { Value = material, Bytes = 1024 }); Bytes += 1024;
                return material;
            }
            catch { Release(material); throw; }
        }
        public void Trim(GameObject root)
        {
            var usedMeshes = new HashSet<Mesh>(root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh));
            var usedMaterials = new HashSet<Material>(root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials));
            var usedTextures = new HashSet<Texture2D>(usedMaterials.Where(m => m).Select(m => m.mainTexture as Texture2D));
            Trim(meshes, usedMeshes); Trim(materials, usedMaterials); Trim(textures, usedTextures);
        }
        void Trim<T>(Dictionary<string, Entry<T>> entries, HashSet<T> used) where T : UnityEngine.Object
        {
            foreach (var pair in entries.ToArray()) if (!used.Contains(pair.Value.Value)) { Bytes -= pair.Value.Bytes; Release(pair.Value.Value); entries.Remove(pair.Key); }
        }
        public void Dispose()
        {
            foreach (var entry in meshes.Values) Release(entry.Value);
            foreach (var entry in materials.Values) Release(entry.Value);
            foreach (var entry in textures.Values) Release(entry.Value);
            meshes.Clear(); materials.Clear(); textures.Clear(); Bytes = 0;
        }
        internal static void Release(UnityEngine.Object value)
        {
            if (!value) return;
            if (value is GameObject go) { go.SetActive(false); go.transform.SetParent(null); }
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
