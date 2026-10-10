using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueCafe.Core
{
    [Serializable] public sealed class CommandResult
    {
        public bool success;
        public string code, message;
        public string[] affectedIds = Array.Empty<string>();
        public string[] instanceIds = Array.Empty<string>();
        public long revision;
    }

    // The host dispatches these synchronous operations on its main-thread command queue.
    // No caller can mutate committed state through the detached public snapshots.
    public sealed class Session
    {
        Catalog catalog;
        Layout layout, saved;
        readonly Stack<Layout> undo = new Stack<Layout>();
        readonly Stack<Layout> redo = new Stack<Layout>();
        public Catalog Catalog => Copies.Copy(catalog);
        public Layout Layout => Copies.Copy(layout);
        public long Revision { get; private set; }
        public long SavedRevision { get; private set; }
        public long CatalogRevision { get; private set; }
        public bool IsDirty => saved == null || !Copies.Equal(layout, saved);
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public Session(Catalog catalog, Layout layout = null)
        {
            Placement.ValidateCatalog(catalog);
            this.catalog = Copies.Copy(catalog);
            this.layout = Copies.Copy(layout ?? new Layout());
            Placement.ValidateLayout(this.catalog, this.layout, true);
            saved = Copies.Copy(this.layout);
        }
        public IEnumerable<Item> Search(string query = "", string category = "", string theme = "") =>
            Placement.Search(catalog, query, category, theme).Select(Copies.Copy).ToArray();
        public Item Find(string id) => Copies.Copy(Placement.Find(catalog, id));

        CommandResult Result(bool success, string code = "OK", string message = "", string[] affected = null, string[] created = null) => new CommandResult {
            success = success, code = code, message = message, revision = Revision,
            affectedIds = affected ?? Array.Empty<string>(), instanceIds = created ?? Array.Empty<string>()
        };
        void CheckRevision(long? expected)
        {
            Placement.Require(!expected.HasValue || expected.Value == Revision, "REVISION_CONFLICT", "The document changed; inspect the current revision and retry.");
        }
        CommandResult Execute(long? expected, string[] requested, Func<Layout, string[]> edit)
        {
            try
            {
                CheckRevision(expected);
                var candidate = Copies.Copy(layout);
                var ids = edit(candidate);
                Placement.ValidateLayout(catalog, candidate, true);
                var affected = requested ?? layout.instances.Select(i => i.instanceId)
                    .Except(candidate.instances.Select(i => i.instanceId)).Concat(ids).ToArray();
                // No-op commands do not create undo entries or invalidate a current revision.
                var changed = !Copies.Equal(candidate, layout);
                if (changed)
                {
                    undo.Push(layout); redo.Clear(); layout = candidate; Revision++;
                }
                return Result(true, changed ? "OK" : "NO_CHANGE", affected: affected, created: ids);
            }
            catch (ValidationException e)
            {
                var affected = e.affectedIds.Length > 0 ? e.affectedIds : requested;
                // A failed copy reports existing source ids, not ids that were never committed.
                if (requested != null && affected != null && affected.Any(id => !requested.Contains(id))) affected = requested;
                return Result(false, e.code, e.Message, affected);
            }
        }
        static string[] Ids(IEnumerable<string> ids) => ids?.ToArray();
        static Instance[] Selection(Layout candidate, string[] ids)
        {
            Placement.Require(ids != null && ids.Length > 0, "EMPTY_SELECTION", "Select at least one instance.");
            Placement.Require(ids.All(i => !string.IsNullOrWhiteSpace(i)) && ids.Distinct().Count() == ids.Length,
                "INVALID_SELECTION", "Selection ids must be nonempty and unique.");
            return ids.Select(id => candidate.instances.FirstOrDefault(i => i.instanceId == id)
                ?? throw new ValidationException("UNKNOWN_INSTANCE", id, id)).ToArray();
        }

        public CommandResult Place(string furnitureId, float x, float z, int quarterTurns = 0, float elevation = -1, long? expectedRevision = null) =>
            Execute(expectedRevision, null, candidate => {
                var item = Placement.Find(catalog, furnitureId);
                Placement.Require(item.available, "ASSET_UNAVAILABLE", item.name + " has no acquired model.");
                var p = Placement.Constrain(catalog, item, new Position(x, elevation, z), quarterTurns, candidate.room);
                Placement.Require(Placement.Finite(elevation) && elevation >= -1 && elevation <= candidate.room.wallHeight,
                    "INVALID_ELEVATION", "Elevation must be within the wall height.");
                if (item.placement == "wall")
                {
                    p.y = Placement.WallElevation(catalog, item, elevation < 0 ? Placement.Snap(candidate.room.wallHeight / 2, candidate.room.grid) : elevation, candidate.room);
                    quarterTurns = Math.Abs(p.x + candidate.room.width / 2) < .001f ? 0 : 1;
                }
                if (Placement.IsRoomStyle(item))
                {
                    quarterTurns = 0;
                    candidate.instances.RemoveAll(i => Placement.Find(catalog, i.furnitureId).placement == item.placement);
                }
                var id = Guid.NewGuid().ToString("N");
                candidate.instances.Add(new Instance { instanceId = id, furnitureId = furnitureId, position = p, quarterTurns = quarterTurns });
                return new[] { id };
            });

        // Translation snaps one group displacement on its existing grid phase.
        // Every selected instance receives the same delta, including wall-normal motion.
        // Validation then rejects detachment instead of guessing another wall.
        void Translate(Layout candidate, Instance[] selected, float dx, float dz)
        {
            Placement.Require(Placement.Finite(dx) && Placement.Finite(dz), "INVALID_TRANSFORM", "Move offsets must be finite.");
            // One shared delta preserves every relative offset. Wall or mixed
            // groups retain full-cell movement and their protected attachment.
            var step = selected.All(i => Placement.Find(catalog, i.furnitureId).placement == "floor")
                ? candidate.room.grid / 2 : candidate.room.grid;
            var sx = Placement.Snap(dx, step);
            var sz = Placement.Snap(dz, step);
            foreach (var instance in selected)
            {
                var p = instance.position;
                instance.position = new Position(p.x + sx, p.y, p.z + sz);
            }
        }
        void ProtectAttachments(Instance[] before, Instance[] after, Room room)
        {
            for (int n = 0; n < before.Length; n++)
            {
                var item = Placement.Find(catalog, before[n].furnitureId);
                if (Placement.IsRoomStyle(item))
                    Placement.Require(before[n].position.x == after[n].position.x && before[n].position.z == after[n].position.z && after[n].quarterTurns == 0,
                        "ROOM_STYLE_FIXED", "Room styles have no movable furniture transform.");
                if (item.placement != "wall") continue;
                var left = before[n].quarterTurns == 0;
                var p = after[n].position;
                if ((left ? Math.Abs(p.x + room.width / 2) : Math.Abs(p.z - room.depth / 2)) >= .001f ||
                    after[n].quarterTurns != before[n].quarterTurns)
                    throw new ValidationException("ATTACHMENT_CONFLICT", "The operation would change an item's attached wall: " + before[n].instanceId, before[n].instanceId);
            }
        }
        public CommandResult Move(IEnumerable<string> ids, float dx, float dz, long? expectedRevision = null)
        {
            var requested = Ids(ids);
            return Execute(expectedRevision, requested, candidate => {
                var selected = Selection(candidate, requested); var before = selected.Select(Copies.Copy).ToArray();
                Translate(candidate, selected, dx, dz); ProtectAttachments(before, selected, candidate.room); return requested;
            });
        }
        public CommandResult Rotate(IEnumerable<string> ids, int steps = 1, long? expectedRevision = null)
        {
            var requested = Ids(ids);
            return Execute(expectedRevision, requested, candidate => {
                var selected = Selection(candidate, requested); var before = selected.Select(Copies.Copy).ToArray();
                var turns = (steps % 4 + 4) % 4;
                if (turns == 0) return requested;
                float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
                foreach (var instance in selected)
                {
                    var item = Placement.Find(catalog, instance.furnitureId);
                    Placement.Require(!Placement.IsRoomStyle(item), "ROOM_STYLE_FIXED", "Room styles have no movable furniture transform.");
                    var w = Math.Max(.01f, item.width * catalog.gridUnit); var d = Math.Max(.01f, item.depth * catalog.gridUnit);
                    if (instance.quarterTurns % 2 == 1) { var t = w; w = d; d = t; }
                    minX = Math.Min(minX, instance.position.x - w / 2); maxX = Math.Max(maxX, instance.position.x + w / 2);
                    minZ = Math.Min(minZ, instance.position.z - d / 2); maxZ = Math.Max(maxZ, instance.position.z + d / 2);
                }
                var cx = (minX + maxX) / 2; var cz = (minZ + maxZ) / 2;
                foreach (var instance in selected)
                {
                    var px = instance.position.x - cx; var pz = instance.position.z - cz;
                    for (int n = 0; n < turns; n++) { var t = px; px = pz; pz = -t; }
                    instance.position = new Position(cx + px, instance.position.y, cz + pz);
                    instance.quarterTurns = (instance.quarterTurns + turns) % 4;
                }
                ProtectAttachments(before, selected, candidate.room); return requested;
            });
        }
        public CommandResult Duplicate(IEnumerable<string> ids, float dx = 0, float dz = 0, long? expectedRevision = null)
        {
            var requested = Ids(ids);
            return Execute(expectedRevision, requested, candidate => {
                var selected = Selection(candidate, requested); var duplicates = selected.Select(Copies.Copy).ToArray();
                Placement.Require(selected.All(i => !Placement.IsRoomStyle(Placement.Find(catalog, i.furnitureId))), "INVALID_ROOM_STYLE", "Room styles cannot be duplicated.");
                Translate(candidate, duplicates, dx, dz); ProtectAttachments(selected, duplicates, candidate.room);
                foreach (var duplicate in duplicates) { duplicate.instanceId = Guid.NewGuid().ToString("N"); candidate.instances.Add(duplicate); }
                return duplicates.Select(i => i.instanceId).ToArray();
            });
        }
        public CommandResult Delete(IEnumerable<string> ids, long? expectedRevision = null)
        {
            var requested = Ids(ids);
            return Execute(expectedRevision, requested, candidate => {
                Selection(candidate, requested); var selected = new HashSet<string>(requested);
                candidate.instances.RemoveAll(i => selected.Contains(i.instanceId)); return requested;
            });
        }
        public CommandResult Replace(Layout replacement, long? expectedRevision = null)
        {
            try
            {
                CheckRevision(expectedRevision);
                var candidate = Copies.Copy(replacement); Placement.ValidateLayout(catalog, candidate, true);
                var ids = candidate.instances.Select(i => i.instanceId).ToArray();
                var affected = layout.instances.Select(i => i.instanceId).Union(ids).ToArray();
                var changed = !Copies.Equal(candidate, layout);
                if (changed) { undo.Push(layout); redo.Clear(); layout = candidate; Revision++; }
                return Result(true, changed ? "OK" : "NO_CHANGE", affected: affected, created: ids);
            }
            catch (ValidationException e) { return Result(false, e.code, e.Message, e.affectedIds); }
        }
        // Open/New/Import replace document identity, unlike an undoable edit within
        // one document. A successful open consumes a revision even for equal content.
        // Unsaved copies have no saved baseline until an explicit save succeeds.
        public CommandResult OpenDocument(Layout replacement, bool saved = false, long? expectedRevision = null)
        {
            try
            {
                CheckRevision(expectedRevision);
                var candidate = Copies.Copy(replacement); Placement.ValidateLayout(catalog, candidate, true);
                var ids = candidate.instances.Select(i => i.instanceId).ToArray();
                var affected = layout.instances.Select(i => i.instanceId).Union(ids).ToArray();
                layout = candidate; Revision++; undo.Clear(); redo.Clear();
                this.saved = saved ? Copies.Copy(candidate) : null;
                SavedRevision = saved ? Revision : -1;
                return Result(true, affected: affected, created: ids);
            }
            catch (ValidationException e) { return Result(false, e.code, e.Message, e.affectedIds); }
        }
        CommandResult History(bool backwards, long? expected)
        {
            try
            {
                CheckRevision(expected);
                var source = backwards ? undo : redo; var destination = backwards ? redo : undo;
                Placement.Require(source.Count > 0, backwards ? "NOTHING_TO_UNDO" : "NOTHING_TO_REDO", "No command is available.");
                var ids = layout.instances.Select(i => i.instanceId).Union(source.Peek().instances.Select(i => i.instanceId)).ToArray();
                destination.Push(layout); layout = source.Pop(); Revision++;
                return Result(true, affected: ids, created: layout.instances.Select(i => i.instanceId).ToArray());
            }
            catch (ValidationException e) { return Result(false, e.code, e.Message, e.affectedIds); }
        }
        public CommandResult Undo(long? expectedRevision = null) => History(true, expectedRevision);
        public CommandResult Redo(long? expectedRevision = null) => History(false, expectedRevision);
        public CommandResult MarkSaved(long? expectedRevision = null)
        {
            try { CheckRevision(expectedRevision); saved = Copies.Copy(layout); SavedRevision = Revision; return Result(true); }
            catch (ValidationException e) { return Result(false, e.code, e.Message); }
        }
        // Source relinking is independent from document authoring. Preflight every
        // retained snapshot so undo remains valid with the new catalog.
        public CommandResult RefreshCatalog(Catalog replacement)
        {
            try
            {
                catalog = CatalogCandidate(replacement); CatalogRevision++;
                return Result(true);
            }
            catch (ValidationException e) { return Result(false, e.code, e.Message, e.affectedIds); }
        }
        public CommandResult PreviewCatalog(Catalog replacement)
        {
            try { CatalogCandidate(replacement); return Result(true); }
            catch (ValidationException e) { return Result(false, e.code, e.Message, e.affectedIds); }
        }
        Catalog CatalogCandidate(Catalog replacement)
        {
            Placement.ValidateCatalog(replacement);
            var candidate = Copies.Copy(replacement);
            Placement.ValidateLayout(candidate, layout, true);
            if (saved != null) Placement.ValidateLayout(candidate, saved, true);
            foreach (var snapshot in undo.Concat(redo)) Placement.ValidateLayout(candidate, snapshot, true);
            return candidate;
        }
    }
}
