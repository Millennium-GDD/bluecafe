using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueCafe.Core
{
    public sealed class ValidationException : Exception
    {
        public readonly string code;
        public readonly string[] affectedIds;
        public ValidationException(string code, string message, params string[] affectedIds) : base(message)
        { this.code = code; this.affectedIds = affectedIds ?? Array.Empty<string>(); }
    }

    public static class Placement
    {
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static void Require(bool condition, string code, string message)
        { if (!condition) throw new ValidationException(code, message); }
        public static float Snap(float value, float grid) => (float)Math.Round(value / grid, MidpointRounding.ToEven) * grid;
        // Floor centers may occupy half cells so odd-cell footprints can meet a
        // room edge. Existing full-cell coordinates remain valid and unchanged.
        public static float SnapStep(Item item, Room room) => item.placement == "floor" ? room.grid / 2 : room.grid;
        public static bool IsRoomStyle(Item item) => item.placement == "flooring" || item.placement == "wallpaper" || item.placement == "background";
        public static Item Find(Catalog catalog, string id) => catalog.items.FirstOrDefault(i => i != null && i.id == id)
            ?? throw new ValidationException("UNKNOWN_ITEM", id ?? "null");
        public static IEnumerable<Item> Search(Catalog catalog, string query = "", string category = "", string theme = "") =>
            catalog.items.Where(i => i != null &&
                (string.IsNullOrEmpty(query) || (i.id + " " + i.name + " " + i.category + " " + i.theme + " " + i.sourceAlias).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (string.IsNullOrEmpty(category) || i.category == category) && (string.IsNullOrEmpty(theme) || i.theme == theme));
        public static void ValidateCatalog(Catalog catalog)
        {
            Require(catalog != null && catalog.version == 1 && catalog.items != null && Finite(catalog.gridUnit) && catalog.gridUnit > 0,
                "INVALID_CATALOG", "Expected a version 1 catalog, items array and positive finite gridUnit.");
            var ids = new HashSet<string>();
            foreach (var item in catalog.items)
            {
                Require(item != null && !string.IsNullOrWhiteSpace(item.id) && ids.Add(item.id), "INVALID_CATALOG", "Catalog ids must be unique and nonempty.");
                Require(Finite(item.width) && Finite(item.depth) && Finite(item.height) && item.width > 0 && item.depth > 0 && item.height > 0,
                    "INVALID_CATALOG", "Catalog footprint dimensions must be positive and finite: " + item.id);
                Require(item.placement == "floor" || item.placement == "wall" || IsRoomStyle(item), "INVALID_CATALOG", "Unknown placement type: " + item.id);
            }
        }
        public static void ValidateRoom(Room room)
        {
            Require(room != null && Finite(room.width) && Finite(room.depth) && Finite(room.wallHeight) && Finite(room.grid), "INVALID_ROOM", "Room dimensions must be finite.");
            Require(room.width > 0 && room.depth > 0 && room.wallHeight > 0 && room.grid > 0 && room.width <= 1000 && room.depth <= 1000 && room.grid <= Math.Min(room.width, room.depth),
                "INVALID_ROOM", "Use positive dimensions up to 1000 and a grid smaller than the room.");
        }
        // Legacy placement preview: Place may choose the nearest of the two v1 walls.
        // Group edits never call this to repair a candidate or choose a new attachment.
        public static Position Constrain(Catalog catalog, Item item, Position position, int turns, Room room)
        {
            ValidateRoom(room);
            Require(Finite(position.x) && Finite(position.y) && Finite(position.z), "INVALID_TRANSFORM", "Position must be finite.");
            Require(turns >= 0 && turns <= 3, "INVALID_ROTATION", "quarterTurns must be 0, 1, 2 or 3.");
            if (IsRoomStyle(item)) return new Position();
            var step = SnapStep(item, room);
            position = new Position(Snap(position.x, step), 0, Snap(position.z, step));
            if (item.placement == "wall")
            {
                if (Math.Abs(position.x + room.width / 2) < Math.Abs(position.z - room.depth / 2)) position.x = -room.width / 2;
                else position.z = room.depth / 2;
            }
            var w = Math.Max(.01f, item.width * catalog.gridUnit); var d = Math.Max(.01f, item.depth * catalog.gridUnit);
            if (turns % 2 == 1) { var t = w; w = d; d = t; }
            if (item.placement == "wall")
            {
                var left = Math.Abs(position.x + room.width / 2) < .001f;
                var along = left ? position.z : position.x; var length = left ? room.depth : room.width;
                Require(Math.Abs(along) + item.width * catalog.gridUnit / 2 <= length / 2 + .001f, "OUTSIDE_ROOM", "Wall item must fit along the wall.");
            }
            else Require(Math.Abs(position.x) + w / 2 <= room.width / 2 + .001f && Math.Abs(position.z) + d / 2 <= room.depth / 2 + .001f,
                "OUTSIDE_ROOM", "The item footprint must fit inside the room.");
            return position;
        }
        public static float WallElevation(Catalog catalog, Item item, float elevation, Room room)
        {
            Require(Finite(elevation), "INVALID_ELEVATION", "Elevation must be finite.");
            elevation = Snap(elevation, room.grid);
            var half = item.height * catalog.gridUnit / 2;
            Require(elevation >= half - .001f && elevation + half <= room.wallHeight + .001f, "INVALID_ELEVATION", "Wall item must fit vertically within the wall.");
            return elevation;
        }
        public static void ValidateLayout(Catalog catalog, Layout layout, bool allowUnavailable = false)
        {
            Require(layout != null && layout.version == 1 && layout.instances != null, "INVALID_LAYOUT", "Expected version 1 layout and instances array.");
            ValidateRoom(layout.room);
            var ids = new HashSet<string>(); var styles = new HashSet<string>();
            foreach (var instance in layout.instances)
            {
                Require(instance != null && !string.IsNullOrWhiteSpace(instance.instanceId) && ids.Add(instance.instanceId), "INVALID_INSTANCE", "Instances require unique nonempty ids.");
                try
                {
                    var item = Find(catalog, instance.furnitureId);
                    Require(allowUnavailable || item.available, "ASSET_UNAVAILABLE", instance.furnitureId);
                    if (IsRoomStyle(item)) Require(styles.Add(item.placement) && instance.quarterTurns == 0, "INVALID_ROOM_STYLE", "Each room style must occur once with zero rotation.");
                    var p = Constrain(catalog, item, instance.position, instance.quarterTurns, layout.room);
                    Require(instance.position.y >= 0 && instance.position.y <= layout.room.wallHeight, "INVALID_ELEVATION", instance.instanceId);
                    if (item.placement == "wall")
                    {
                        p.y = WallElevation(catalog, item, instance.position.y, layout.room);
                        var side = Math.Abs(p.x + layout.room.width / 2) < .001f ? 0 : 1;
                        Require(instance.quarterTurns == side, "INVALID_ROTATION", "Wall objects must face inward from their attached wall.");
                    }
                    var dx = p.x - instance.position.x; var dy = p.y - instance.position.y; var dz = p.z - instance.position.z;
                    Require(dx * dx + dy * dy + dz * dz < .000001f, "OFF_GRID", "Positions must obey grid and placement rules: " + instance.instanceId);
                }
                catch (ValidationException e) { throw new ValidationException(e.code, e.Message, instance.instanceId); }
            }
        }
    }
}
