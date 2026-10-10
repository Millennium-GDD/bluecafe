using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlueCafe.Core;
using UnityEngine;

namespace BlueCafe.Infrastructure
{
    public sealed class PackException : Exception
    {
        public string Code { get; }
        public PackException(string code, string message) : base(message) { Code = code; }
    }
    public sealed class PackProgress
    {
        public int Completed { get; internal set; }
        public int Total { get; internal set; }
        public string Path { get; internal set; }
    }
    public sealed class ItemAvailability
    {
        public bool Available { get; internal set; }
        public string Reason { get; internal set; }
        public bool ThumbnailAvailable { get; internal set; }
        public string ThumbnailReason { get; internal set; }
    }
    [Serializable] public sealed class DeclaredSource
    {
        public string region, gameVersion, sourceCatalog, metadataSource;
        public int patchVersion;
        public string Verification => "Declared source metadata; authenticity and rendered fidelity are unverified.";
    }
    [Serializable] public sealed class PackMesh
    {
        public string name;
        public float[] vertices, normals, uv;
        public PackSubmesh[] submeshes;
    }
    [Serializable] public sealed class PackSubmesh { public int[] triangles; }

    /// <summary>A detached, read-only source-pack verification result. Load on a worker thread, then adopt atomically.</summary>
    public sealed class Pack
    {
        public const string LegacyPrefix = "Assets/BlueCafe/Content/";
        public Catalog Catalog { get; private set; }
        public string Root { get; private set; }
        public string Fingerprint { get; private set; }
        public DeclaredSource Source { get; private set; }
        public int AvailableCount => availability.Values.Count(x => x.Available);
        public int MeshCount { get; private set; }
        public int ImageCount { get; private set; }
        readonly Dictionary<string, ItemAvailability> availability = new Dictionary<string, ItemAvailability>(StringComparer.Ordinal);

        public ItemAvailability Availability(string id)
        {
            if (!availability.TryGetValue(id ?? "", out var value)) throw new PackException("UNKNOWN_ITEM", "Unknown furniture id: " + id);
            return value;
        }
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? path.Substring(LegacyPrefix.Length) : path;
        }
        public string Resolve(string path)
        {
            path = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0)
                throw new PackException("UNSAFE_PACK_PATH", "Pack paths must be relative to the selected catalog folder: " + path);
            var parts = path.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new PackException("UNSAFE_PACK_PATH", "Pack paths cannot contain empty, '.' or '..' segments: " + path);
            string absolute = Path.GetFullPath(Path.Combine(Root, path));
            string prefix = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, PathComparison)) throw new PackException("UNSAFE_PACK_PATH", "Path escapes selected pack: " + path);
            CheckLinks(Root);
            string candidate = Root;
            foreach (string part in parts) { candidate = Path.Combine(candidate, part); CheckLinks(candidate); }
            return absolute;
        }
        static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static void CheckLinks(string path)
        {
            // Reject all symlinked components, including links whose current target is contained.
            // This avoids adopting a pack whose paths can later resolve outside the selected folder.
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new PackException("UNSAFE_PACK_PATH", "Pack paths cannot contain symbolic links: " + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        public static Task<Pack> LoadAsync(string root, CancellationToken cancellationToken = default, IProgress<PackProgress> progress = null)
            => Task.Run(() => Load(root, cancellationToken, progress), cancellationToken);
        public static Pack Load(string root) => Load(root, CancellationToken.None, null);
        static Pack Load(string root, CancellationToken cancellationToken, IProgress<PackProgress> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new PackException("PACK_NOT_FOUND", "Choose an existing folder containing catalog.json.");
            var pack = new Pack { Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) };
            if (pack.Root.Length == 0) pack.Root = Path.GetPathRoot(Path.GetFullPath(root));
            // Parent links matter too: lexical containment alone is insufficient.
            for (var dir = new DirectoryInfo(pack.Root); dir != null; dir = dir.Parent) CheckLinks(dir.FullName);
            string catalogPath = pack.Resolve("catalog.json");
            if (!File.Exists(catalogPath)) throw new PackException("CATALOG_NOT_FOUND", "The selected folder does not contain catalog.json.");
            byte[] bytes = File.ReadAllBytes(catalogPath);
            string json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            try
            {
                InspectCatalogJson(json);
                pack.Catalog = JsonUtility.FromJson<Catalog>(json);
                pack.Source = JsonUtility.FromJson<DeclaredSource>(json);
                pack.ValidateCatalog();
            }
            catch (PackException) { throw; }
            catch (Exception ex) { throw new PackException("INVALID_CATALOG", "Invalid prepared catalog: " + ex.Message); }
            // Validate every declared path before reading any resource or publishing an item.
            var meshes = new HashSet<string>(StringComparer.Ordinal);
            var images = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in pack.Catalog.items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.modelPath = pack.NormalizeAndCheck(item.modelPath);
                item.thumbnailPath = pack.NormalizeAndCheck(item.thumbnailPath);
                if (!string.IsNullOrEmpty(item.thumbnailPath)) images.Add(item.thumbnailPath);
                foreach (var part in item.parts ?? Array.Empty<Part>())
                {
                    part.modelPath = pack.NormalizeAndCheck(part.modelPath);
                    part.meshJsonPath = pack.NormalizeAndCheck(part.meshJsonPath);
                    if (!string.IsNullOrEmpty(part.meshJsonPath)) meshes.Add(part.meshJsonPath);
                    foreach (var material in part.materials ?? Array.Empty<MaterialSpec>())
                    {
                        material.texturePath = pack.NormalizeAndCheck(material.texturePath);
                        if (!string.IsNullOrEmpty(material.texturePath)) images.Add(material.texturePath);
                    }
                }
            }
            pack.MeshCount = meshes.Count; pack.ImageCount = images.Count;
            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            var allPaths = meshes.Concat(images).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            using (var fingerprint = SHA256.Create())
            {
                HashAppend(fingerprint, "BlueCafe.PreparedPack.v1\n"); HashAppend(fingerprint, bytes);
                int count = 0;
                foreach (var path in allPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string resolved = pack.Resolve(path);
                    string reason = null;
                    byte[] content = null;
                    try
                    {
                        if (!File.Exists(resolved)) reason = "Missing source file: " + path;
                        else
                        {
                            content = File.ReadAllBytes(resolved);
                            if (meshes.Contains(path)) ValidateMesh(content);
                            else PngValidation.Validate(content);
                        }
                    }
                    catch (PackException) { throw; }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException || ex is InvalidDataException)
                    { reason = "Invalid source file " + path + ": " + ex.Message; }
                    results[path] = reason;
                    HashAppend(fingerprint, "\n" + path + "\n");
                    if (content != null) { using (var fileHash = SHA256.Create()) HashAppend(fingerprint, fileHash.ComputeHash(content)); }
                    else HashAppend(fingerprint, "missing-or-unreadable");
                    progress?.Report(new PackProgress { Completed = ++count, Total = allPaths.Length, Path = path });
                }
                fingerprint.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                pack.Fingerprint = BitConverter.ToString(fingerprint.Hash).Replace("-", "").ToLowerInvariant();
            }
            foreach (var item in pack.Catalog.items)
            {
                var state = new ItemAvailability();
                if (!item.available) state.Reason = "The prepared catalog declares this item unavailable.";
                else if (item.parts == null || item.parts.Length == 0) state.Reason = "No prepared mesh parts were supplied.";
                else foreach (var part in item.parts)
                {
                    if (string.IsNullOrEmpty(part.meshJsonPath)) { state.Reason = "A part has no prepared mesh JSON; raw model import is unsupported."; break; }
                    if (results[part.meshJsonPath] != null) { state.Reason = results[part.meshJsonPath]; break; }
                    foreach (var material in part.materials ?? Array.Empty<MaterialSpec>())
                        if (!string.IsNullOrEmpty(material.texturePath) && results[material.texturePath] != null) { state.Reason = results[material.texturePath]; break; }
                    if (state.Reason != null) break;
                }
                state.Available = state.Reason == null;
                state.ThumbnailReason = string.IsNullOrEmpty(item.thumbnailPath) ? "No thumbnail was supplied." : results[item.thumbnailPath];
                state.ThumbnailAvailable = state.ThumbnailReason == null;
                item.available = state.Available;
                pack.availability.Add(item.id, state);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return pack;
        }
        string NormalizeAndCheck(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string normalized = NormalizePath(path); Resolve(normalized); return normalized;
        }
        static void InspectCatalogJson(string json)
        {
            StrictJson.Inspect(json, (path, keys) =>
            {
                if (path == "") StrictJson.Require(keys, "version", "gridUnit", "items");
                else if (Regex.IsMatch(path, @"^\.items\[\d+\]$")) StrictJson.Require(keys, "id", "name", "placement", "width", "depth", "height", "available");
                else if (Regex.IsMatch(path, @"^\.items\[\d+\]\.parts\[\d+\]$")) StrictJson.Require(keys, "position", "rotation", "scale", "materials");
                else if (Regex.IsMatch(path, @"^\.items\[\d+\]\.parts\[\d+\]\.materials\[\d+\]$")) StrictJson.Require(keys, "color");
            }, (path, kind) =>
            {
                string member = path.Substring(path.LastIndexOf('.') + 1);
                bool itemMember = Regex.IsMatch(path, @"^\.items\[\d+\]\.[^.\[]+$");
                if ((path == ".version" || path == ".gridUnit" || (itemMember && (member == "width" || member == "depth" || member == "height"))) && kind != '-' && (kind < '0' || kind > '9')) throw new FormatException("Expected numeric " + path + ".");
                if (path == ".items" && kind != '[') throw new FormatException("Catalog items must be an array.");
                if (itemMember && (member == "id" || member == "name" || member == "placement") && kind != '"') throw new FormatException("Expected string " + path + ".");
                if (itemMember && member == "available" && kind != 't' && kind != 'f') throw new FormatException("Expected availability boolean.");
            }, (path, number) => { if (path == ".version" && number != "1") throw new PackException("UNSUPPORTED_CATALOG_VERSION", "Only prepared catalog version 1 is supported."); });
        }
        void ValidateCatalog()
        {
            if (Catalog == null || Catalog.version != 1) throw new PackException("UNSUPPORTED_CATALOG_VERSION", "Only prepared catalog version 1 is supported.");
            if (!Finite(Catalog.gridUnit) || Catalog.gridUnit <= 0 || Catalog.items == null) throw new PackException("INVALID_CATALOG", "Catalog needs a positive finite gridUnit and an items array.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Catalog.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id)) throw new PackException("INVALID_CATALOG", "Furniture IDs must be nonempty and unique.");
                if (string.IsNullOrWhiteSpace(item.name) || !new[] { "floor", "wall", "flooring", "wallpaper", "background" }.Contains(item.placement))
                    throw new PackException("INVALID_CATALOG", "Furniture " + item.id + " has an invalid name or placement type.");
                if (!Finite(item.width) || !Finite(item.depth) || !Finite(item.height) || item.width <= 0 || item.depth <= 0 || item.height <= 0)
                    throw new PackException("INVALID_CATALOG", "Furniture " + item.id + " has invalid footprint dimensions.");
                if (item.bounds != null)
                {
                    RequireVector(item.bounds.min, 3, "bounds minimum"); RequireVector(item.bounds.max, 3, "bounds maximum");
                    for (int axis = 0; axis < 3; axis++) if (item.bounds.min[axis] > item.bounds.max[axis]) throw new FormatException("Inverted bounds.");
                }
                foreach (var part in item.parts ?? Array.Empty<Part>())
                {
                    if (part == null) throw new FormatException("Null furniture part.");
                    RequireVector(part.position, 3, "part position"); RequireVector(part.rotation, 4, "part rotation"); RequireVector(part.scale, 3, "part scale");
                    double norm = part.rotation.Sum(v => (double)v * v);
                    if (Math.Abs(norm - 1) > .0001 || part.scale.Any(v => v == 0)) throw new FormatException("Invalid part rotation or scale.");
                    foreach (var material in part.materials ?? Array.Empty<MaterialSpec>())
                    {
                        if (material == null) throw new FormatException("Null material.");
                        RequireVector(material.color, 4, "material color");
                        if (material.textureScale != null) RequireVector(material.textureScale, 2, "texture scale");
                        if (material.textureOffset != null) RequireVector(material.textureOffset, 2, "texture offset");
                        if (!Finite(material.cutoff) || !Finite(material.cull)) throw new FormatException("Nonfinite material state.");
                    }
                }
            }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void RequireVector(float[] values, int count, string name)
        {
            if (values == null || values.Length != count || values.Any(v => !Finite(v))) throw new FormatException("Invalid " + name + ".");
        }
        public static PackMesh ReadMesh(string path)
        {
            byte[] bytes = File.ReadAllBytes(path); return ValidateMesh(bytes);
        }
        static PackMesh ValidateMesh(byte[] bytes)
        {
            string json = new UTF8Encoding(false, true).GetString(bytes);
            StrictJson.Require(StrictJson.Validate(json), "vertices", "submeshes");
            var mesh = JsonUtility.FromJson<PackMesh>(json);
            if (mesh == null || mesh.vertices == null || mesh.vertices.Length == 0 || mesh.vertices.Length % 3 != 0 || mesh.vertices.Any(v => !Finite(v))) throw new FormatException("Mesh vertices must be finite XYZ triples.");
            int count = mesh.vertices.Length / 3;
            if (mesh.normals != null && mesh.normals.Length != 0 && (mesh.normals.Length != mesh.vertices.Length || mesh.normals.Any(v => !Finite(v)))) throw new FormatException("Invalid mesh normals.");
            if (mesh.uv != null && mesh.uv.Length != 0 && (mesh.uv.Length != count * 2 || mesh.uv.Any(v => !Finite(v)))) throw new FormatException("Invalid mesh UVs.");
            if (mesh.submeshes == null || mesh.submeshes.Length == 0) throw new FormatException("Mesh needs submeshes.");
            foreach (var submesh in mesh.submeshes)
                if (submesh == null || submesh.triangles == null || submesh.triangles.Length % 3 != 0 || submesh.triangles.Any(i => i < 0 || i >= count)) throw new FormatException("Mesh triangle indices are invalid.");
            return mesh;
        }
        static void HashAppend(HashAlgorithm hash, string text) => HashAppend(hash, Encoding.UTF8.GetBytes(text));
        static void HashAppend(HashAlgorithm hash, byte[] bytes) => hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }
}
