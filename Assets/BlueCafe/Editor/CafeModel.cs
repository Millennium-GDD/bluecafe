using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlueCafe
{
    [Serializable] public class CafeCatalog { public int version = 1; public float gridUnit = .6f; public CafeItem[] items = Array.Empty<CafeItem>(); }
    [Serializable] public class CafeCatalogSummary { public int version = 1; public float gridUnit; public CafeItemSummary[] items; }
    [Serializable] public class CafeItemSummary
    {
        public string id, name, category, theme, placement;
        public float width, depth, height;
        public bool available;
    }
    [Serializable] public class CafeItem
    {
        public CafeBounds bounds;
        public string id, name, category, theme, modelPath, thumbnailPath, placement, sourcePrefab;
        public float width = 1, depth = 1, height = 1;
        public bool available;
        public CafePart[] parts;
    }
    [Serializable] public class CafeBounds { public float[] min, max; }
    [Serializable] public class CafePart
    {
        public string modelPath, meshJsonPath;
        public float[] position, rotation, scale;
        public CafeMaterial[] materials;
    }
    [Serializable] public class CafeMaterial { public string name, texturePath; public float[] color, textureScale, textureOffset; public bool transparent; public float cutoff; public float cull; }
    [Serializable] public class CafeRoom { public float width = 18, depth = 18, wallHeight = 4.8f, grid = .6f; }
    [Serializable] public class CafeInstance
    {
        public string instanceId, furnitureId;
        public Vector3 position;
        public int quarterTurns;
    }
    [Serializable] public class CafeLayout
    {
        public int version = 1;
        public string name = "Untitled cafe";
        public CafeRoom room = new CafeRoom();
        public List<CafeInstance> instances = new List<CafeInstance>();
    }
    [FilePath("Library/BlueCafeSession.asset", FilePathAttribute.Location.ProjectFolder)]
    public class CafeSession : ScriptableSingleton<CafeSession>
    {
        [SerializeField] string layoutJson = "";
        public CafeLayout Read() => string.IsNullOrEmpty(layoutJson) ? new CafeLayout() : JsonUtility.FromJson<CafeLayout>(layoutJson);
        public void Write(CafeLayout value, string action)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RecordObject(this, action);
            layoutJson = JsonUtility.ToJson(value);
            Undo.FlushUndoRecordObjects();
            Save(true);
        }
        public void Persist() => Save(true);
    }
    public class CafeValidationException : Exception
    {
        public readonly string code;
        public CafeValidationException(string code, string message) : base(code + ": " + message) { this.code = code; }
    }
    [InitializeOnLoad]
    public static class CafeApi
    {
        public const string CatalogPath = "Assets/BlueCafe/Content/catalog.json";
        static CafeCatalog catalog;
        public static event Action Changed;
        static CafeApi() { Undo.undoRedoPerformed += () => { CafeSession.instance.Persist(); Notify(); }; }
        public static CafeLayout Layout => CafeSession.instance.Read();
        public static CafeCatalog Catalog { get { if (catalog == null) ReloadCatalog(); return catalog; } }
        public static void ReloadCatalog()
        {
            catalog = File.Exists(CatalogPath) ? JsonUtility.FromJson<CafeCatalog>(File.ReadAllText(CatalogPath)) : new CafeCatalog();
            if (catalog == null || catalog.items == null) catalog = new CafeCatalog();
            Notify();
        }
        static void Notify() { Changed?.Invoke(); }
        static void Commit(CafeLayout layout, string action) { CafeSession.instance.Write(layout, action); Notify(); }
        public static string InspectCatalog(string query = "", string category = "", string theme = "", bool detailed = false)
        {
            var matches = Search(query, category, theme).ToArray();
            if (detailed) return JsonUtility.ToJson(new CafeCatalog { gridUnit = Catalog.gridUnit, items = matches }, true);
            return JsonUtility.ToJson(new CafeCatalogSummary { gridUnit = Catalog.gridUnit, items = matches.Select(i => new CafeItemSummary {
                id = i.id, name = i.name, category = i.category, theme = i.theme, placement = i.placement,
                width = i.width, depth = i.depth, height = i.height, available = i.available
            }).ToArray() }, true);
        }
        public static IEnumerable<CafeItem> Search(string query = "", string category = "", string theme = "") => Catalog.items.Where(i =>
            (string.IsNullOrEmpty(query) || ((i.name ?? "") + " " + i.id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (string.IsNullOrEmpty(category) || i.category == category) && (string.IsNullOrEmpty(theme) || i.theme == theme));
        public static CafeItem Find(string id) => Catalog.items.FirstOrDefault(i => i.id == id) ?? throw new CafeValidationException("UNKNOWN_ITEM", id ?? "null");
        public static bool IsRoomStyle(CafeItem item) => item.placement == "flooring" || item.placement == "wallpaper" || item.placement == "background";
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static void Require(bool condition, string code, string message) { if (!condition) throw new CafeValidationException(code, message); }
        public static void ValidateRoom(CafeRoom r)
        {
            Require(r != null && Finite(r.width) && Finite(r.depth) && Finite(r.wallHeight) && Finite(r.grid), "INVALID_ROOM", "Room dimensions must be finite.");
            Require(r.width > 0 && r.depth > 0 && r.wallHeight > 0 && r.grid > 0 && r.width <= 1000 && r.depth <= 1000 && r.grid <= Mathf.Min(r.width, r.depth), "INVALID_ROOM", "Use positive dimensions up to 1000 and a grid smaller than the room.");
        }
        public static Vector3 Constrain(CafeItem item, Vector3 position, int turns, CafeRoom room)
        {
            ValidateRoom(room);
            Require(Finite(position.x) && Finite(position.y) && Finite(position.z), "INVALID_TRANSFORM", "Position must be finite.");
            Require(turns >= 0 && turns <= 3, "INVALID_ROTATION", "quarterTurns must be 0, 1, 2 or 3.");
            if (IsRoomStyle(item)) return Vector3.zero;
            float Snap(float value) => Mathf.Round(value / room.grid) * room.grid;
            position = new Vector3(Snap(position.x), 0, Snap(position.z));
            if (item.placement == "wall")
            {
                if (Mathf.Abs(position.x + room.width / 2) < Mathf.Abs(position.z - room.depth / 2)) position.x = -room.width / 2;
                else position.z = room.depth / 2;
            }
            float w = Mathf.Max(.01f, item.width * Catalog.gridUnit), d = Mathf.Max(.01f, item.depth * Catalog.gridUnit);
            if (turns % 2 == 1) { float temp = w; w = d; d = temp; }
            if (item.placement == "wall")
            {
                bool left = Mathf.Abs(position.x + room.width / 2) < .001f;
                float along = left ? position.z : position.x;
                float length = left ? room.depth : room.width;
                Require(Mathf.Abs(along) + item.width * Catalog.gridUnit / 2 <= length / 2 + .001f, "OUTSIDE_ROOM", "Wall item must fit along the wall.");
            }
            else Require(Mathf.Abs(position.x) + w / 2 <= room.width / 2 + .001f && Mathf.Abs(position.z) + d / 2 <= room.depth / 2 + .001f, "OUTSIDE_ROOM", "The item footprint must fit inside the room.");
            return position;
        }
        public static string Place(string furnitureId, float x, float z, int quarterTurns = 0, float elevation = -1)
        {
            var item = Find(furnitureId);
            Require(item.available, "ASSET_UNAVAILABLE", item.name + " has no acquired model.");
            var layout = Layout;
            var position = Constrain(item, new Vector3(x, elevation, z), quarterTurns, layout.room);
            Require(Finite(elevation) && elevation >= -1 && elevation <= layout.room.wallHeight, "INVALID_ELEVATION", "Elevation must be within the wall height.");
            if (item.placement == "wall")
            {
                if (elevation < 0) elevation = Mathf.Round(layout.room.wallHeight / 2 / layout.room.grid) * layout.room.grid;
                position.y = WallElevation(item, elevation, layout.room);
                quarterTurns = Mathf.Abs(position.x + layout.room.width / 2) < .001f ? 0 : 1;
            }
            var placed = new CafeInstance { instanceId = Guid.NewGuid().ToString("N"), furnitureId = furnitureId, position = position, quarterTurns = quarterTurns };
            if (IsRoomStyle(item)) { placed.quarterTurns = 0; layout.instances.RemoveAll(i => Find(i.furnitureId).placement == item.placement); }
            layout.instances.Add(placed); Commit(layout, "Place " + item.name); return placed.instanceId;
        }
        public static void Move(string instanceId, float x, float z, float elevation = -1)
        {
            var layout = Layout; var instance = Get(layout, instanceId); var item = Find(instance.furnitureId);
            var position = Constrain(item, new Vector3(x, elevation, z), instance.quarterTurns, layout.room);
            Require(Finite(elevation) && elevation >= -1 && elevation <= layout.room.wallHeight, "INVALID_ELEVATION", "Elevation must be within the wall height.");
            if (item.placement == "wall")
            {
                position.y = WallElevation(item, elevation < 0 ? instance.position.y : elevation, layout.room);
                instance.quarterTurns = Mathf.Abs(position.x + layout.room.width / 2) < .001f ? 0 : 1;
            }
            instance.position = position; Commit(layout, "Move furniture");
        }
        public static void Rotate(string instanceId, int steps = 1)
        {
            var layout = Layout; var instance = Get(layout, instanceId); var item = Find(instance.furnitureId);
            if (IsRoomStyle(item)) return;
            if (item.placement == "wall")
            {
                if (steps % 2 != 0)
                {
                    bool left = Mathf.Abs(instance.position.x + layout.room.width / 2) < .001f;
                    var p = left ? new Vector3(instance.position.z, instance.position.y, layout.room.depth / 2) : new Vector3(-layout.room.width / 2, instance.position.y, instance.position.x);
                    var snapped = Constrain(item, p, left ? 1 : 0, layout.room); snapped.y = instance.position.y;
                    instance.position = snapped; instance.quarterTurns = left ? 1 : 0;
                }
                Commit(layout, "Move item to adjacent wall"); return;
            }
            int turns = ((instance.quarterTurns + steps) % 4 + 4) % 4;
            Constrain(item, instance.position, turns, layout.room); instance.quarterTurns = turns; Commit(layout, "Rotate furniture");
        }
        public static string Duplicate(string instanceId)
        {
            var layout = Layout; var instance = Get(layout, instanceId);
            return Place(instance.furnitureId, instance.position.x, instance.position.z, instance.quarterTurns, instance.position.y);
        }
        public static void Delete(string instanceId) { var l = Layout; Get(l, instanceId); l.instances.RemoveAll(i => i.instanceId == instanceId); Commit(l, "Remove furniture"); }
        static CafeInstance Get(CafeLayout layout, string id) => layout.instances.FirstOrDefault(i => i.instanceId == id) ?? throw new CafeValidationException("UNKNOWN_INSTANCE", id ?? "null");
        public static void NewLayout(string name = "Untitled cafe", float width = 18, float depth = 18, float grid = .6f)
        {
            var layout = new CafeLayout { name = name, room = new CafeRoom { width = width, depth = depth, grid = grid } }; ValidateRoom(layout.room); Commit(layout, "New cafe");
        }
        public static void ResizeRoom(float width, float depth, float height, float grid)
        {
            var layout = Layout; layout.room = new CafeRoom { width = width, depth = depth, wallHeight = height, grid = grid }; ValidateLayout(layout); Commit(layout, "Resize cafe");
        }
        static float WallElevation(CafeItem item, float elevation, CafeRoom room)
        {
            elevation = Mathf.Round(elevation / room.grid) * room.grid;
            float half = item.height * Catalog.gridUnit / 2;
            Require(elevation >= half - .001f && elevation + half <= room.wallHeight + .001f, "INVALID_ELEVATION", "Wall item must fit vertically within the wall."); return elevation;
        }
        public static void ValidateLayout(CafeLayout layout)
        {
            Require(layout != null && layout.version == 1 && layout.instances != null, "INVALID_LAYOUT", "Expected version 1 layout and instances array."); ValidateRoom(layout.room);
            var ids = new HashSet<string>();
            var styles = new HashSet<string>();
            foreach (var instance in layout.instances)
            {
                Require(instance != null && !string.IsNullOrWhiteSpace(instance.instanceId) && ids.Add(instance.instanceId), "INVALID_INSTANCE", "Instances require unique nonempty ids.");
                var item = Find(instance.furnitureId); Require(item.available, "ASSET_UNAVAILABLE", instance.furnitureId);
                if (IsRoomStyle(item)) Require(styles.Add(item.placement) && instance.quarterTurns == 0, "INVALID_ROOM_STYLE", "Each room style must occur once with zero rotation.");
                var p = Constrain(item, instance.position, instance.quarterTurns, layout.room);
                Require(instance.position.y >= 0 && instance.position.y <= layout.room.wallHeight, "INVALID_ELEVATION", instance.instanceId);
                if (item.placement == "wall")
                {
                    p.y = WallElevation(item, instance.position.y, layout.room);
                    int side = Mathf.Abs(p.x + layout.room.width / 2) < .001f ? 0 : 1;
                    Require(instance.quarterTurns == side, "INVALID_ROTATION", "Wall objects must face inward from their attached wall.");
                }
                Require(Vector3.Distance(p, instance.position) < .001f, "OFF_GRID", "Positions must obey grid and placement rules: " + instance.instanceId);
            }
        }
        public static string ExportJson() => JsonUtility.ToJson(Layout, true);
        public static void ImportJson(string json)
        {
            CafeLayout candidate;
            try { candidate = JsonUtility.FromJson<CafeLayout>(json); } catch (Exception e) { throw new CafeValidationException("MALFORMED_JSON", e.Message); }
            ValidateLayout(candidate); Commit(candidate, "Load cafe");
        }
        public static string Save(string path) { path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, ExportJson()); return path; }
        public static void Load(string path) => ImportJson(File.ReadAllText(path));
        public static void UndoLast() => Undo.PerformUndo();
        public static void RedoLast() => Undo.PerformRedo();
        public static string Capture(string path, int width = 1600, int height = 1200) { using (var view = new CafePreview()) { view.Rebuild(Layout); return view.Capture(path, width, height); } }
        public static string ExportScene(string path) => CafePreview.ExportScene(Layout, path);
        public static string Status() => "{\"instances\":" + Layout.instances.Count + ",\"catalog\":" + Catalog.items.Length + ",\"available\":" + Catalog.items.Count(i => i.available) + "}";
    }
}
