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
            Require(room != null && Finite(room.width) && Finite(room.depth) && Finite(room.wallHeight) && Finite(room.grid) && Finite(room.centerX) && Finite(room.centerZ), "INVALID_ROOM", "Room dimensions must be finite.");
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
                if (Math.Abs(position.x - room.centerX + room.width / 2) < Math.Abs(position.z - room.centerZ - room.depth / 2)) position.x = room.centerX - room.width / 2;
                else position.z = room.centerZ + room.depth / 2;
            }
            var w = Math.Max(.01f, item.width * catalog.gridUnit); var d = Math.Max(.01f, item.depth * catalog.gridUnit);
            if (turns % 2 == 1) { var t = w; w = d; d = t; }
            if (item.placement == "wall")
            {
                var left = Math.Abs(position.x - room.centerX + room.width / 2) < .001f;
                var along = left ? position.z - room.centerZ : position.x - room.centerX; var length = left ? room.depth : room.width;
                Require(Math.Abs(along) + item.width * catalog.gridUnit / 2 <= length / 2 + .001f, "OUTSIDE_ROOM", "Wall item must fit along the wall.");
            }
            else Require(Math.Abs(position.x - room.centerX) + w / 2 <= room.width / 2 + .001f && Math.Abs(position.z - room.centerZ) + d / 2 <= room.depth / 2 + .001f,
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
        public static float AttachmentWidth(Catalog c,Item i) => i.bounds?.min?.Length>=3 && i.bounds.max?.Length>=3 ? Math.Max(i.width*c.gridUnit,i.bounds.max[2]-i.bounds.min[2]) : i.width*c.gridUnit;
        public static float AttachmentHeight(Catalog c,Item i) => i.bounds?.min?.Length>=3 && i.bounds.max?.Length>=3 ? Math.Max(i.height*c.gridUnit,i.bounds.max[1]-i.bounds.min[1]) : i.height*c.gridUnit;
        public static void ValidateAttachment(Catalog c,Item item,Instance instance,Layout l)
        {
            var along=instance.wallAlong;var y=instance.position.y;var half=(instance.legacyWallFootprint?item.width*c.gridUnit:AttachmentWidth(c,item))/2;var h=(instance.legacyWallFootprint?item.height*c.gridUnit:AttachmentHeight(c,item))/2;
            Require(Finite(along)&&Finite(y)&&Finite(instance.position.x)&&Finite(instance.position.z),"INVALID_ATTACHMENT","Attachment coordinates must be finite.");
            Require(along-half>=-.001f&&along+half<=Geometry.WallLength(l.room,instance.wallId)+.001f,"OUTSIDE_WALL","Decoration footprint crosses a wall edge.");
            Require(y-h>=-.001f&&y+h<=l.room.wallHeight+.001f,"INVALID_ELEVATION","Decoration footprint crosses the floor or wall top.");
            var p=Geometry.WallPosition(l.room,instance.wallId,along,y);
            Require(Math.Abs(p.x-instance.position.x)<.001f&&Math.Abs(p.z-instance.position.z)<.001f&&instance.quarterTurns==Geometry.WallTurns(instance.wallId),"ATTACHMENT_CONFLICT","Decoration must retain its attached wall position and orientation.");
            Require((instance.legacyWallFootprint || Math.Abs(Snap(along,l.room.grid)-along)<.001f)&&Math.Abs(Snap(y,l.room.grid)-y)<.001f,"OFF_GRID","Wall position and elevation must obey the wall grid.");
            foreach(var o in l.openings) if(o.wallId==instance.wallId && along+half>o.along-o.width/2+.001f && along-half<o.along+o.width/2-.001f && y+h>o.bottom+.001f && y-h<o.bottom+o.height-.001f)
                throw new ValidationException("OPENING_CONFLICT","Decoration overlaps wall opening "+o.id,instance.instanceId);
        }
        public static void ValidateLayout(Catalog catalog, Layout layout, bool allowUnavailable = false)
        {
            Require(layout != null && (layout.version == 1 || layout.version == 2) && layout.instances != null, "INVALID_LAYOUT", "Expected version 1 or 2 layout and instances array.");
            Geometry.Validate(layout);
            foreach(var area in layout.areas ?? new List<Area>()) {
                if(!string.IsNullOrEmpty(area.flooringId)) Require(Find(catalog,area.flooringId).placement=="flooring","INVALID_ROOM_STYLE","Area flooring id must reference flooring.");
                if(!string.IsNullOrEmpty(area.wallpaperId)) Require(Find(catalog,area.wallpaperId).placement=="wallpaper","INVALID_ROOM_STYLE","Area wallpaper id must reference wallpaper.");
            }
            var ids = new HashSet<string>(); var styles = new HashSet<string>();
            foreach (var instance in layout.instances)
            {
                Require(instance != null && !string.IsNullOrWhiteSpace(instance.instanceId) && ids.Add(instance.instanceId), "INVALID_INSTANCE", "Instances require unique nonempty ids.");
                try
                {
                    var item = Find(catalog, instance.furnitureId);
                    Require(allowUnavailable || item.available, "ASSET_UNAVAILABLE", instance.furnitureId);
                    if (IsRoomStyle(item)) Require(styles.Add(item.placement) && instance.quarterTurns == 0, "INVALID_ROOM_STYLE", "Each room style must occur once with zero rotation.");
                    Require(layout.version != 2 || item.placement != "wall" || !string.IsNullOrEmpty(instance.wallId),"INVALID_ATTACHMENT","Version 2 wall decorations require wall identity and along-wall coordinates.");
                    if(item.placement == "wall" && !string.IsNullOrEmpty(instance.wallId)) {
                        Require(layout.version==2,"INVALID_LAYOUT","Explicit attachments require version 2.");
                        ValidateAttachment(catalog,item,instance,layout); continue;
                    }
                    Require(string.IsNullOrEmpty(instance.wallId) && instance.wallAlong==0 && !instance.legacyWallFootprint,"INVALID_ATTACHMENT","Only wall furniture has attachment coordinates.");
                    var p = Constrain(catalog, item, instance.position, instance.quarterTurns, layout.room);
                    Require(instance.position.y >= 0 && instance.position.y <= layout.room.wallHeight, "INVALID_ELEVATION", instance.instanceId);
                    if (item.placement == "wall")
                    {
                        p.y = WallElevation(catalog, item, instance.position.y, layout.room);
                        var side = Math.Abs(p.x - layout.room.centerX + layout.room.width / 2) < .001f ? 0 : 1;
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
