using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlueCafe.Unity
{
    [Serializable] public sealed class RenderUnavailable
    {
        public string instanceId, furnitureId, reason;
    }

    /// <summary>Main-thread projection of a committed v1 layout, using only prepared original pack assets.</summary>
    public sealed partial class RuntimeView : IDisposable
    {
        sealed class Projection
        {
            public string FurnitureId;
            public GameObject Object;
            public Instance Instance;
        }
        const int ViewLayer = 30;
        const int MaxModulesPerStyle = 4096;
        readonly Pack pack;
        readonly Dictionary<string, Item> items;
        readonly RuntimeResources resources;
        readonly Scene scene;
        readonly bool ownsScene;
        readonly GameObject root;
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        readonly Dictionary<string, Projection> objects = new Dictionary<string, Projection>();
        readonly HashSet<GameObject> backgrounds = new HashSet<GameObject>();
        readonly HashSet<Renderer> framingHelpers = new HashSet<Renderer>();
        readonly List<RenderUnavailable> unavailable = new List<RenderUnavailable>();
        RenderTexture texture;
        Room room;
        bool framed, disposed;
        public Camera Camera { get; }
        public long Revision { get; private set; } = -1;
        public IReadOnlyList<RenderUnavailable> Unavailable => unavailable;
        public long ResourceBytes => resources.Bytes;
        public int MeshCount => resources.MeshCount;
        public int TextureCount => resources.TextureCount;
        public int MaterialCount => resources.MaterialCount;
        public int ObjectCount => objects.Count;
        public string PackFingerprint => pack.Fingerprint;

        public RuntimeView(Pack pack, long resourceBudgetBytes = 512L * 1024 * 1024, Scene? projectionScene = null)
        {
            this.pack = pack ?? throw new ArgumentNullException(nameof(pack));
            if (resourceBudgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(resourceBudgetBytes));
            if (projectionScene.HasValue && (!projectionScene.Value.IsValid() || !projectionScene.Value.isLoaded))
                throw new ArgumentException("The supplied projection scene must be valid and loaded.", nameof(projectionScene));
            items = pack.Catalog.items.ToDictionary(i => i.id, StringComparer.Ordinal);
            resources = new RuntimeResources(pack, resourceBudgetBytes);
            // Editor adapters may supply their own preview scene; the runtime layer
            // only owns and unloads scenes that it creates for the Player.
            ownsScene = !projectionScene.HasValue;
            scene = projectionScene ?? SceneManager.CreateScene("BlueCafe Runtime " + Guid.NewGuid().ToString("N"));
            root = NewObject("BlueCafe Runtime", null);
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = NewObject("Cafe Camera", root.transform);
            Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.80f, .86f, .89f);
            Camera.orthographic = true; Camera.nearClipPlane = .01f; Camera.farClipPlane = 1000;
            Camera.allowHDR = false; Camera.allowMSAA = true; Camera.cullingMask = 1 << ViewLayer;
            Camera.scene = scene;
        }
        void CheckThread()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimeView));
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("RuntimeView must be used on its owning Unity main thread.");
        }
        static GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name) { layer = ViewLayer, hideFlags = HideFlags.DontSave };
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }
        public void Sync(Layout layout, long revision)
        {
            CheckThread();
            if (layout?.room == null || layout.instances == null) throw new ArgumentException("A committed layout with room and instances is required.", nameof(layout));
            if (revision < Revision) throw new InvalidOperationException("STALE_REVISION: projection cannot move backward.");
            ClearPlacementPreview();
            bool roomChanged = room == null || room.width != layout.room.width || room.depth != layout.room.depth || room.wallHeight != layout.room.wallHeight;
            room = new Room { width = layout.room.width, depth = layout.room.depth, wallHeight = layout.room.wallHeight, grid = layout.room.grid };
            var byId = layout.instances.ToDictionary(i => i.instanceId, StringComparer.Ordinal);
            foreach (var pair in objects.ToArray())
            {
                byId.TryGetValue(pair.Key, out var source);
                bool style = items.TryGetValue(pair.Value.FurnitureId, out var oldItem) && (oldItem.placement == "flooring" || oldItem.placement == "wallpaper");
                if (source == null || source.furnitureId != pair.Value.FurnitureId || (style && roomChanged)) Remove(pair.Key);
            }
            resources.Trim(root);
            unavailable.Clear();
            foreach (var instance in layout.instances)
            {
                if (!items.TryGetValue(instance.furnitureId, out var item))
                {
                    Missing(instance, "UNKNOWN_ITEM: furniture id is absent from the validated pack."); continue;
                }
                if (!objects.TryGetValue(instance.instanceId, out var projection))
                {
                    var availability = pack.Availability(item.id);
                    if (!availability.Available) { Missing(instance, availability.Reason); continue; }
                    GameObject go = null;
                    try
                    {
                        // An item is atomic: one missing original part makes the whole instance unavailable.
                        go = NewObject(item.name ?? item.id, root.transform);
                        if (item.placement == "flooring" || item.placement == "wallpaper") AssembleRoomStyle(item, go.transform);
                        else Assemble(item, go.transform);
                        ApplyNativePivot(item, go);
                        projection = new Projection { FurnitureId = item.id, Object = go };
                        go.name = (item.name ?? item.id) + " [" + instance.instanceId + "]";
                        objects.Add(instance.instanceId, projection);
                        if (item.placement == "background") backgrounds.Add(go);
                    }
                    catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is PackException || e is FormatException || e is UnityException)
                    {
                        if (go) RuntimeResources.Release(go);
                        resources.Trim(root); framingHelpers.RemoveWhere(r => !r || !r.transform.IsChildOf(root.transform));
                        Missing(instance, e.Message); continue;
                    }
                }
                projection.Instance = Copies.Copy(instance);
                projection.Object.transform.localPosition = new Vector3(instance.position.x, instance.position.y, instance.position.z);
                projection.Object.transform.localRotation = Quaternion.Euler(0, instance.quarterTurns * 90, 0);
            }
            resources.Trim(root);
            Revision = revision;
            if (!framed || roomChanged) FrameRoom();
        }
        void Missing(Instance instance, string reason) => unavailable.Add(new RenderUnavailable { instanceId = instance.instanceId, furnitureId = instance.furnitureId, reason = reason });
        void Remove(string id)
        {
            var go = objects[id].Object;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>()) framingHelpers.Remove(renderer);
            backgrounds.Remove(go); RuntimeResources.Release(go); objects.Remove(id);
        }
        void Assemble(Item item, Transform parent)
        {
            if (item.parts == null || item.parts.Length == 0) throw new InvalidDataException("MODEL_UNAVAILABLE: no prepared mesh parts for " + item.id);
            foreach (var part in item.parts)
            {
                if (string.IsNullOrEmpty(part.meshJsonPath)) throw new InvalidDataException("MODEL_UNAVAILABLE: part has no prepared mesh JSON for " + item.id);
                var mesh = resources.Mesh(part.meshJsonPath);
                if (part.materials == null || part.materials.Length == 0)
                    throw new InvalidDataException("MATERIAL_UNAVAILABLE: original material array is missing for " + item.id);
                // Preserve the native material array even when shorter than the submesh count.
                // Four acquired items intentionally retain that original arrangement.
                var mats = part.materials.Select(spec => resources.Material(spec, item)).ToArray();
                var child = NewObject(Path.GetFileNameWithoutExtension(part.meshJsonPath), parent);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterials = mats;
                if (RuntimeSurfaceRules.IsFramingHelper(item, part)) framingHelpers.Add(renderer);
                child.transform.localPosition = V3(part.position, Vector3.zero);
                child.transform.localRotation = part.rotation?.Length == 4 ? new Quaternion(part.rotation[0], part.rotation[1], part.rotation[2], part.rotation[3]) : Quaternion.identity;
                child.transform.localScale = V3(part.scale, Vector3.one);
            }
        }
        static Vector3 V3(float[] p, Vector3 fallback) => p != null && p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : fallback;
        static void ApplyNativePivot(Item item, GameObject go)
        {
            if (item.placement != "wall" || !(item.bounds?.min?.Length >= 3) || !(item.bounds.max?.Length >= 3)) return;
            var anchor = NewObject("Native pivot offset", go.transform);
            foreach (var child in go.transform.Cast<Transform>().Where(t => t != anchor.transform).ToArray()) child.SetParent(anchor.transform, false);
            float centerY = (item.bounds.min[1] + item.bounds.max[1]) / 2;
            if (RuntimeSurfaceRules.IsAuditedSurface(item))
            {
                var frame = go.GetComponentsInChildren<Renderer>().FirstOrDefault(r => RuntimeSurfaceRules.IsFrameRenderer(item, r));
                if (frame) centerY = frame.bounds.center.y;
            }
            anchor.transform.localPosition = new Vector3(0, -centerY, 0);
        }
        // Declared placement dimensions are independent of the original mesh bounds.
        // Both transient placement and selected furniture use this same floor plan.
        public Bounds? PlacementFootprintFor(Instance instance)
        {
            CheckThread();
            if (instance?.position == null || !items.TryGetValue(instance.furnitureId, out var item) || Placement.IsRoomStyle(item)) return null;
            float width = Math.Max(.01f, item.width * pack.Catalog.gridUnit), depth = Math.Max(.01f, item.depth * pack.Catalog.gridUnit);
            if (instance.quarterTurns % 2 != 0) (width, depth) = (depth, width);
            return new Bounds(new Vector3(instance.position.x, .03f, instance.position.z), new Vector3(width, 0, depth));
        }
        public Bounds? FootprintBoundsFor(string instanceId)
        {
            CheckThread();
            return objects.TryGetValue(instanceId, out var projection) ? PlacementFootprintFor(projection.Instance) : null;
        }
        public Bounds? BoundsFor(string instanceId)
        {
            CheckThread();
            if (!objects.TryGetValue(instanceId, out var projection)) return null;
            var renderers = projection.Object.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        public Vector3 NativePivotOffsetFor(string instanceId)
        {
            CheckThread();
            if (!objects.TryGetValue(instanceId, out var projection)) return Vector3.zero;
            var anchor = projection.Object.transform.Find("Native pivot offset");
            return anchor ? anchor.localPosition : Vector3.zero;
        }
        public void FrameRoom()
        {
            CheckThread(); if (room == null) return; framed = true;
            var rotation = Quaternion.LookRotation(-new Vector3(1, .95f, -1.4f).normalized, Vector3.up);
            var inverse = Quaternion.Inverse(rotation);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            void Include(Vector3 point) { var p = inverse * point; min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !IsPlacementPreviewRenderer(r) && !framingHelpers.Contains(r) && !backgrounds.Any(b => r.transform.IsChildOf(b.transform))).ToArray();
            if (renderers.Length == 0)
                for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) Include(new Vector3((x - .5f) * room.width, 0, (z - .5f) * room.depth));
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) Include(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
            }
            var center = (min + max) / 2;
            Camera.transform.SetPositionAndRotation(rotation * new Vector3(center.x, center.y, min.z - 50), rotation);
            Camera.orthographicSize = Mathf.Max(.1f, Mathf.Max((max.y - min.y) / 2, (max.x - min.x) / 2 / Mathf.Max(.1f, Camera.aspect)) * 1.09f);
            UpdateBackgrounds();
        }
        void UpdateBackgrounds()
        {
            foreach (var background in backgrounds)
            {
                background.transform.SetPositionAndRotation(Camera.transform.position + Camera.transform.forward * 200, Camera.transform.rotation);
                float span = Camera.orthographicSize * 2;
                background.transform.localScale = new Vector3(span * Camera.aspect, span, 1);
            }
        }
        public void Pan(Vector2 delta) { CheckThread(); Camera.transform.position += (-Camera.transform.right * delta.x + Camera.transform.up * delta.y) * Camera.orthographicSize / 350; }
        public void Zoom(float delta) { CheckThread(); Camera.orthographicSize = Mathf.Clamp(Camera.orthographicSize * Mathf.Exp(delta * .04f), 1, 300); }
        public RenderTexture Render(int width, int height)
        {
            CheckThread(); width = Mathf.Clamp(width, 32, 4096); height = Mathf.Clamp(height, 32, 4096);
            bool firstRender = texture == null;
            if (!texture || texture.width != width || texture.height != height)
            {
                if (texture) { Camera.targetTexture = null; texture.Release(); RuntimeResources.Release(texture); }
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.DontSave };
                if (!texture.Create()) throw new InvalidOperationException("RENDER_TARGET_FAILED: could not allocate viewport.");
            }
            Camera.targetTexture = texture; Camera.aspect = (float)width / height;
            if (firstRender) FrameRoom(); UpdateBackgrounds(); Camera.Render(); return texture;
        }
        /// <summary>Point is local to the top-left of the viewport, matching UI Toolkit.</summary>
        public Ray ScreenRay(Vector2 local, Rect rect)
        {
            CheckThread();
            if (rect.width <= 0 || rect.height <= 0) throw new ArgumentException("Viewport must have positive dimensions.", nameof(rect));
            return Camera.ViewportPointToRay(new Vector3(local.x / rect.width, 1 - local.y / rect.height, 0));
        }
        public Vector3? FloorPoint(Vector2 local, Rect rect)
        {
            var ray = ScreenRay(local, rect);
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance) ? ray.GetPoint(distance) : (Vector3?)null;
        }
        public string Pick(Vector2 local, Rect rect)
        {
            var ray = ScreenRay(local, rect); string selected = null; float nearest = float.MaxValue;
            foreach (var pair in objects.Where(p => !backgrounds.Contains(p.Value.Object) && !Placement.IsRoomStyle(items[p.Value.FurnitureId])))
                foreach (var renderer in pair.Value.Object.GetComponentsInChildren<Renderer>())
                    if (!framingHelpers.Contains(renderer) && renderer.bounds.IntersectRay(ray, out float distance) && distance < nearest) { nearest = distance; selected = pair.Key; }
            return selected;
        }
        public string Capture(string path, int width, int height)
        {
            CheckThread();
            if (Revision < 0) throw new InvalidOperationException("PROJECTION_REQUIRED: synchronize a committed layout before capture.");
            if (width < 32 || height < 32 || width > 4096 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width), "Capture dimensions must be 32–4096.");
            var oldPosition = Camera.transform.position; var oldRotation = Camera.transform.rotation;
            var oldSize = Camera.orthographicSize; var oldAspect = Camera.aspect;
            var previous = RenderTexture.active; Texture2D image = null;
            try
            {
                Camera.aspect = (float)width / height; FrameRoom(); Render(width, height); RenderTexture.active = texture;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG()); return path;
            }
            finally
            {
                RenderTexture.active = previous; RuntimeResources.Release(image);
                Camera.transform.SetPositionAndRotation(oldPosition, oldRotation); Camera.orthographicSize = oldSize; Camera.aspect = oldAspect; UpdateBackgrounds();
            }
        }
        public void Dispose()
        {
            if (disposed) return; CheckThread(); ClearPlacementPreview(); disposed = true;
            if (texture) { Camera.targetTexture = null; texture.Release(); RuntimeResources.Release(texture); }
            RuntimeResources.Release(root); resources.Dispose();
            objects.Clear(); backgrounds.Clear(); framingHelpers.Clear(); unavailable.Clear();
            if (ownsScene && scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }
}
