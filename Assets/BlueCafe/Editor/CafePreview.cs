using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BlueCafe
{
    [Serializable] public class CafeMeshData { public string name; public float[] vertices, normals, uv; public CafeSubmesh[] submeshes; }
    [Serializable] public class CafeSubmesh { public int[] triangles; }
    public sealed class CafePreview : IDisposable
    {
        readonly Scene scene;
        readonly GameObject root;
        public readonly Camera camera;
        readonly List<UnityEngine.Object> transient = new List<UnityEngine.Object>();
        readonly Dictionary<string, GameObject> objects = new Dictionary<string, GameObject>();
        readonly HashSet<GameObject> backgrounds = new HashSet<GameObject>();
        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        RenderTexture texture;
        CafeRoom room;
        bool framed;
        public IReadOnlyDictionary<string, GameObject> Objects => objects;
        public CafePreview()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("BlueCafe Preview"); SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Cafe Camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cameraType = CameraType.Preview; camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.80f, .86f, .89f); camera.orthographic = true;
            camera.nearClipPlane = .01f; camera.farClipPlane = 1000; camera.allowHDR = false; camera.allowMSAA = true;
        }
        public void Rebuild(CafeLayout layout)
        {
            foreach (var go in objects.Values) if (go) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear(); backgrounds.Clear(); room = layout.room;
            foreach (var instance in layout.instances)
            {
                var item = CafeApi.Find(instance.furnitureId);
                var go = (item.placement == "flooring" || item.placement == "wallpaper") ? AssembleRoomStyle(item, root.transform) : Assemble(item, root.transform);
                if (item.placement == "wall" && item.bounds != null && item.bounds.min != null && item.bounds.max != null)
                {
                    // Placement elevation describes a wall footprint center, while native
                    // prefabs mix center pivots and floor pivots (notably tall windows).
                    var anchor = new GameObject("Native pivot offset"); anchor.transform.SetParent(go.transform, false);
                    var children = go.transform.Cast<Transform>().Where(t => t != anchor.transform).ToArray();
                    foreach (var child in children) child.SetParent(anchor.transform, false);
                    anchor.transform.localPosition = new Vector3(0, -(item.bounds.min[1] + item.bounds.max[1]) / 2, 0);
                }
                go.name = item.name + " [" + instance.instanceId + "]";
                go.transform.localPosition = instance.position; go.transform.localRotation = Quaternion.Euler(0, instance.quarterTurns * 90, 0);
                objects.Add(instance.instanceId, go);
                if (item.placement == "background") backgrounds.Add(go);
            }
            if (!framed) FrameRoom();
        }
        public void FrameRoom()
        {
            if (room == null) return;
            framed = true;
            // Proposed camera: source previews establish the cutaway composition, not lens numbers.
            var rotation = Quaternion.LookRotation(-new Vector3(1, .95f, -1.4f).normalized, Vector3.up);
            var inverse = Quaternion.Inverse(rotation);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            void Include(Vector3 point) { var p = inverse * point; min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !backgrounds.Any(b => r.transform.IsChildOf(b.transform))).ToArray();
            if (renderers.Length == 0)
            {
                for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) Include(new Vector3((x - .5f) * room.width, 0, (z - .5f) * room.depth));
            }
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) Include(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
            }
            var center = (min + max) / 2;
            camera.transform.SetPositionAndRotation(rotation * new Vector3(center.x, center.y, min.z - 50), rotation);
            camera.orthographicSize = Mathf.Max((max.y - min.y) / 2, (max.x - min.x) / 2 / Mathf.Max(.1f, camera.aspect)) * 1.09f;
            UpdateBackgrounds();
        }
        void UpdateBackgrounds()
        {
            // The original game backdrop prefab is a camera-facing built-in Quad.
            foreach (var background in backgrounds)
            {
                background.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 200, camera.transform.rotation);
                background.transform.localScale = new Vector3(camera.orthographicSize * camera.aspect * 2, camera.orthographicSize * 2, 1);
            }
        }
        public void Pan(Vector2 delta)
        {
            camera.transform.position += (-camera.transform.right * delta.x + camera.transform.up * delta.y) * camera.orthographicSize / 350;
        }
        public void Zoom(float delta) => camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * Mathf.Exp(delta * .04f), 1, 300);
        public RenderTexture Render(int width, int height)
        {
            width = Mathf.Clamp(width, 32, 4096); height = Mathf.Clamp(height, 32, 4096);
            if (!texture || texture.width != width || texture.height != height)
            {
                if (texture) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
                texture.Create();
            }
            bool firstRender = camera.targetTexture == null;
            camera.targetTexture = texture; camera.aspect = (float)width / height; if (firstRender) FrameRoom(); UpdateBackgrounds(); camera.Render(); return texture;
        }
        public Ray ScreenRay(Vector2 local, Rect rect) => camera.ViewportPointToRay(new Vector3(local.x / rect.width, 1 - local.y / rect.height, 0));
        public Vector3 FloorPoint(Vector2 local, Rect rect)
        {
            var ray = ScreenRay(local, rect); return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance) ? ray.GetPoint(distance) : Vector3.zero;
        }
        public string Pick(Vector2 point, Rect rect)
        {
            var ray = ScreenRay(point, rect); string selected = null; float nearest = float.MaxValue;
            foreach (var pair in objects.Where(p => !backgrounds.Contains(p.Value)))
            foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>())
                if (renderer.bounds.IntersectRay(ray, out float distance) && distance < nearest) { nearest = distance; selected = pair.Key; }
            return selected;
        }
        public Bounds? BoundsFor(string id)
        {
            if (id == null || !objects.TryGetValue(id, out var go)) return null;
            var renderers = go.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) return null;
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b;
        }
        GameObject AssembleRoomStyle(CafeItem item, Transform parent)
        {
            var group = new GameObject(item.name); group.transform.SetParent(parent, false);
            var unit = CafeApi.Catalog.gridUnit;
            if (item.placement == "flooring")
            {
                // Modules can cover multiple grid cells (Abydos uses 2x2). Rotate the
                // native two-sided skirt toward the open edges and tile by its bounds.
                bool hasBounds = item.bounds?.min != null && item.bounds.max != null;
                float spanX = hasBounds ? item.bounds.max[2] - item.bounds.min[2] : unit;
                float spanZ = hasBounds ? item.bounds.max[0] - item.bounds.min[0] : unit;
                spanX = Mathf.Max(unit, Mathf.Round(spanX / unit) * unit);
                spanZ = Mathf.Max(unit, Mathf.Round(spanZ / unit) * unit);
                float offsetX = hasBounds ? -item.bounds.min[2] : unit / 2;
                float offsetZ = hasBounds ? item.bounds.max[0] : unit / 2;
                int nx = Mathf.CeilToInt(room.width / spanX), nz = Mathf.CeilToInt(room.depth / spanZ);
                for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                {
                    var tile = Assemble(item, group.transform); tile.transform.localRotation = Quaternion.Euler(0, 90, 0); tile.transform.localPosition = new Vector3(-room.width / 2 + offsetX + spanX * x, 0, -room.depth / 2 + offsetZ + spanZ * z);
                }
            }
            else
            {
                float span = item.bounds != null && item.bounds.max != null ? item.bounds.max[2] - item.bounds.min[2] : unit * 2;
                // Source float bounds can be microscopically shorter than a whole
                // grid module; ceil(length / span) must not duplicate a 12m wall.
                span = Mathf.Max(unit, Mathf.Round(span / unit) * unit);
                float start = item.bounds != null && item.bounds.min != null ? -item.bounds.min[2] : unit / 2;
                for (int side = 0; side < 2; side++)
                {
                    float length = side == 0 ? room.depth : room.width;
                    for (int i = 0; i < Mathf.CeilToInt(length / span); i++)
                    {
                        var wall = Assemble(item, group.transform);
                        wall.transform.localPosition = side == 0 ? new Vector3(-room.width / 2, 0, -room.depth / 2 + start + i * span) : new Vector3(-room.width / 2 + start + i * span, 0, room.depth / 2);
                        wall.transform.localRotation = Quaternion.Euler(0, side * 90, 0);
                    }
                }
            }
            return group;
        }
        GameObject Assemble(CafeItem item, Transform parent)
        {
            var result = new GameObject(item.name ?? item.id); result.transform.SetParent(parent, false);
            if (item.parts != null && item.parts.Length > 0)
            {
                foreach (var part in item.parts)
                {
                    GameObject child;
                    if (!string.IsNullOrEmpty(part.meshJsonPath) && File.Exists(part.meshJsonPath))
                    {
                        child = new GameObject(Path.GetFileNameWithoutExtension(part.meshJsonPath)); child.AddComponent<MeshFilter>().sharedMesh = LoadMesh(part.meshJsonPath);
                        child.AddComponent<MeshRenderer>().sharedMaterials = (part.materials ?? Array.Empty<CafeMaterial>()).Select(LoadMaterial).ToArray();
                    }
                    else
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(part.modelPath ?? "");
                        if (!prefab) continue; child = UnityEngine.Object.Instantiate(prefab);
                        ApplyMaterials(child, part.materials);
                    }
                    child.transform.SetParent(result.transform, false);
                    child.transform.localPosition = V3(part.position, Vector3.zero);
                    child.transform.localRotation = part.rotation != null && part.rotation.Length == 4 ? new Quaternion(part.rotation[0], part.rotation[1], part.rotation[2], part.rotation[3]) : Quaternion.identity;
                    child.transform.localScale = V3(part.scale, Vector3.one);
                }
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.modelPath ?? "");
                if (prefab) UnityEngine.Object.Instantiate(prefab, result.transform, false);
            }
            return result;
        }
        static Vector3 V3(float[] p, Vector3 fallback) => p != null && p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : fallback;
        void ApplyMaterials(GameObject go, CafeMaterial[] specs)
        {
            if (specs == null || specs.Length == 0) return;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = specs.Select(LoadMaterial).ToArray();
        }
        Material LoadMaterial(CafeMaterial spec)
        {
            var key = JsonUtility.ToJson(spec); if (materials.TryGetValue(key, out var existing)) return existing;
            var shader = Shader.Find("BlueCafe/BakedTexture") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = spec.name ?? "Cafe material" };
            if (!string.IsNullOrEmpty(spec.texturePath)) material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.texturePath);
            if (spec.textureScale != null && spec.textureScale.Length == 2) material.mainTextureScale = new Vector2(spec.textureScale[0], spec.textureScale[1]);
            if (spec.textureOffset != null && spec.textureOffset.Length == 2) material.mainTextureOffset = new Vector2(spec.textureOffset[0], spec.textureOffset[1]);
            material.SetFloat("_Cutoff", spec.cutoff > 0 ? spec.cutoff : .08f);
            // Native opaque shader textures can store masks in alpha (Abydos frames,
            // partitions and cabinets). Only explicit transparent materials use opacity.
            bool usesOpacity = spec.transparent || (spec.color != null && spec.color.Length == 4 && spec.color[3] < .999f);
            material.SetFloat("_UseAlpha", usesOpacity ? 1 : 0);
            if (usesOpacity)
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite", 0); material.SetFloat("_Cutoff", .005f); material.renderQueue = 3000;
            }
            if (spec.color != null && spec.color.Length == 4)
            {
                // The native lit shader uses HDR tint boosts. Baked unlit display normalizes
                // their peak brightness while preserving hue; source bytes remain unchanged.
                float peak = Mathf.Max(1, Mathf.Max(spec.color[0], Mathf.Max(spec.color[1], spec.color[2])));
                material.color = new Color(spec.color[0] / peak, spec.color[1] / peak, spec.color[2] / peak, spec.color[3]);
            }
            transient.Add(material); materials[key] = material; return material;
        }
        Mesh LoadMesh(string path)
        {
            if (meshes.TryGetValue(path, out var cached)) return cached;
            var data = JsonUtility.FromJson<CafeMeshData>(File.ReadAllText(path));
            var mesh = new Mesh { name = data.name ?? Path.GetFileNameWithoutExtension(path), indexFormat = IndexFormat.UInt32 };
            var vertices = new Vector3[data.vertices.Length / 3]; for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.vertices[i * 3], data.vertices[i * 3 + 1], data.vertices[i * 3 + 2]); mesh.vertices = vertices;
            if (data.normals != null && data.normals.Length == data.vertices.Length) { var normals = new Vector3[vertices.Length]; for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]); mesh.normals = normals; }
            if (data.uv != null && data.uv.Length == vertices.Length * 2) { var uv = new Vector2[vertices.Length]; for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(data.uv[i * 2], data.uv[i * 2 + 1]); mesh.uv = uv; }
            mesh.subMeshCount = data.submeshes.Length; for (int i = 0; i < data.submeshes.Length; i++) mesh.SetTriangles(data.submeshes[i].triangles, i);
            mesh.RecalculateBounds(); transient.Add(mesh); meshes[path] = mesh; return mesh;
        }
        public string Capture(string path, int width, int height)
        {
            camera.aspect = (float)width / height; FrameRoom(); Render(width, height); var previous = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try { image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply(); path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG()); return path; }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
        public static string ExportScene(CafeLayout layout, string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) throw new CafeValidationException("INVALID_PATH", "Scene path must be Assets/.../Name.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var active = SceneManager.GetActiveScene();
            // Unity refuses NewScene(Additive) while any untitled scene is loaded.
            // Copy/open our empty saved template instead; never save the user's scene.
            const string templatePath = "Assets/BlueCafe/Editor/ExportTemplate.unity";
            string stagingPath = "Assets/BlueCafe/Editor/ExportWorking-" + Guid.NewGuid().ToString("N") + ".unity";
            if (!AssetDatabase.CopyAsset(templatePath, stagingPath)) throw new CafeValidationException("EXPORT_TEMPLATE_MISSING", templatePath);
            Scene output = default;
            try
            {
                output = EditorSceneManager.OpenScene(stagingPath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(output);
            using (var preview = new CafePreview())
            {
                try
                {
                    preview.Rebuild(layout); preview.FrameRoom();
                    var assetFolder = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path) + "_Resources"; Directory.CreateDirectory(assetFolder); AssetDatabase.Refresh();
                    int index = 0;
                    foreach (var obj in preview.transient) { var clone = UnityEngine.Object.Instantiate(obj); var assetPath = AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/" + (++index) + ".asset"); AssetDatabase.CreateAsset(clone, assetPath);
                        foreach (var mf in preview.root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh == obj) mf.sharedMesh = (Mesh)clone;
                        foreach (var mr in preview.root.GetComponentsInChildren<Renderer>()) { var mats = mr.sharedMaterials; bool changed = false; for (int i = 0; i < mats.Length; i++) if (mats[i] == obj) { mats[i] = (Material)clone; changed = true; } if (changed) mr.sharedMaterials = mats; }
                    }
                    var cafe = UnityEngine.Object.Instantiate(preview.root); cafe.name = layout.name; SceneManager.MoveGameObjectToScene(cafe, output);
                    var cameraObject = new GameObject("Cafe Camera"); SceneManager.MoveGameObjectToScene(cameraObject, output); var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(preview.camera); camera.scene = output; camera.targetTexture = null; camera.cameraType = CameraType.Game; camera.enabled = true; camera.transform.SetPositionAndRotation(preview.camera.transform.position, preview.camera.transform.rotation); camera.tag = "MainCamera";
                    if (!EditorSceneManager.SaveScene(output, path)) throw new CafeValidationException("EXPORT_SAVE_FAILED", path);
                    AssetDatabase.SaveAssets(); return path;
                }
                finally { if (active.IsValid()) SceneManager.SetActiveScene(active); }
            }
            }
            finally
            {
                if (output.IsValid() && output.isLoaded) EditorSceneManager.CloseScene(output, true);
                if (active.IsValid()) SceneManager.SetActiveScene(active);
                AssetDatabase.DeleteAsset(stagingPath);
            }
        }
        public void Dispose()
        {
            if (texture) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
            foreach (var obj in transient) if (obj) UnityEngine.Object.DestroyImmediate(obj);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
