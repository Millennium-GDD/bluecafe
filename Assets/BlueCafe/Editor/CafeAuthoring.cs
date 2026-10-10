using System;
using System.Linq;
using UnityEngine;

namespace BlueCafe
{
    public static partial class CafeReconstruction
    {
        const float AuthoringTolerance = .0001f;

        // These commands preflight a detached layout, then invoke exactly one shared
        // authoring operation. Failed requests never write a session or an undo record.
        public static string PlaceOnWall(string furnitureId, string wallId, float alongWorld, float elevation = -1)
        {
            var item = CafeApi.Find(furnitureId);
            GeometryRequire(item.placement == "wall", "INVALID_PLACEMENT", "PlaceOnWall requires wall furniture.");
            GeometryRequire(wallId == "x=min" || wallId == "z=max", "UNKNOWN_WALL", "Supported walls: x=min, z=max.");
            var layout = CafeApi.Layout;
            var position = wallId == "x=min" ? new Vector3(-layout.room.width / 2, elevation, alongWorld)
                : new Vector3(alongWorld, elevation, layout.room.depth / 2);
            int q = wallId == "x=min" ? 0 : 1;
            var constrained = CafeApi.Constrain(item, position, q, layout.room);
            GeometryRequire(FiniteVector(position) && CafeApi.Finite(elevation) && elevation >= -1,
                "INVALID_TRANSFORM", "Wall position and elevation must be finite.");
            if (elevation == -1) position.y = Mathf.Round(layout.room.wallHeight / 2 / layout.room.grid) * layout.room.grid;
            GeometryRequire(Mathf.Abs(constrained.x - position.x) < AuthoringTolerance && Mathf.Abs(constrained.z - position.z) < AuthoringTolerance,
                "UNACHIEVABLE_WALL_PLACEMENT", "The specified along-wall position must be on the room grid.");
            // At a corner shared placement selects x=min; do not silently attach to
            // a different wall than the explicit request.
            int actualQ = Mathf.Abs(constrained.x + layout.room.width / 2) < .001f ? 0 : 1;
            GeometryRequire(actualQ == q, "UNACHIEVABLE_WALL_PLACEMENT", "Shared placement resolves this position to another wall.");
            layout.instances.Add(new CafeInstance { instanceId = Guid.NewGuid().ToString("N"), furnitureId = furnitureId,
                position = position, quarterTurns = q });
            CafeApi.ValidateLayout(layout);
            return CafeApi.Place(furnitureId, position.x, position.z, q, position.y);
        }

        public static void FaceToward(string instanceId, string targetInstanceId, string direction = "front", CafeOrientationAnnotations annotations = null)
        {
            var layout = CafeApi.Layout;
            var target = GeometryInstance(layout, targetInstanceId);
            FaceToward(instanceId, target.position, direction, annotations);
        }

        // Facing is horizontal: target elevation is ignored, while an audited
        // nonhorizontal direction cannot be satisfied by a placement quarter turn.
        public static void FaceToward(string instanceId, Vector3 targetWorld, string direction = "front", CafeOrientationAnnotations annotations = null)
        {
            var layout = CafeApi.Layout;
            var instance = GeometryInstance(layout, instanceId);
            var item = CafeApi.Find(instance.furnitureId);
            GeometryRequire(!CafeApi.IsRoomStyle(item), "INVALID_PLACEMENT", "Room styles have no authoring direction.");
            GeometryRequire(FiniteVector(targetWorld), "INVALID_TRANSFORM", "Target must be finite.");
            annotations = annotations ?? LoadOrientations();
            ValidateOrientations(annotations);
            var annotation = annotations.items.FirstOrDefault(a => a.furnitureId == item.id);
            GeometryRequire(annotation == null || string.IsNullOrWhiteSpace(annotation.sourcePrefab) || annotation.sourcePrefab == item.sourcePrefab,
                "STALE_ANNOTATION", "Source prefab identity changed: " + item.id);
            var vector = annotation?.directions.FirstOrDefault(v => string.Equals(v.name, direction, StringComparison.OrdinalIgnoreCase));
            GeometryRequire(vector != null && vector.status == "audited", "UNKNOWN_DIRECTION", "An audited direction is required: " + direction);
            GeometryRequire(Mathf.Abs(vector.local.y) < AuthoringTolerance, "UNACHIEVABLE_FACING", "Quarter turns cannot make this direction horizontal.");
            bool wall = item.placement == "wall";
            int count = wall ? 2 : 4;
            for (int steps = 0; steps < count; steps++)
            {
                var candidate = JsonUtility.FromJson<CafeLayout>(JsonUtility.ToJson(layout));
                var selected = GeometryInstance(candidate, instanceId);
                if (wall && steps == 1)
                {
                    bool left = selected.quarterTurns == 0;
                    selected.position = left ? new Vector3(selected.position.z, selected.position.y, candidate.room.depth / 2)
                        : new Vector3(-candidate.room.width / 2, selected.position.y, selected.position.x);
                    selected.quarterTurns = left ? 1 : 0;
                }
                else if (!wall) selected.quarterTurns = (selected.quarterTurns + steps) % 4;
                var delta = targetWorld - selected.position; delta.y = 0;
                if (delta.sqrMagnitude < AuthoringTolerance * AuthoringTolerance) continue;
                var facing = Quaternion.Euler(0, selected.quarterTurns * 90, 0) * vector.local;
                if (Vector3.Distance(facing, delta.normalized) > AuthoringTolerance) continue;
                try { CafeApi.ValidateLayout(candidate); }
                catch (CafeValidationException) { continue; }
                if (steps != 0) CafeApi.Rotate(instanceId, steps);
                return;
            }
            throw new CafeValidationException("UNACHIEVABLE_FACING", "No legal quarter turn faces the target using the audited " + direction + " direction.");
        }

        // Signed axis names describe which side of other the selected item goes.
        // The cross-axis position and elevation are preserved. Bounds are measured
        // geometry or the declared placement footprint, never guessed furniture fronts.
        public static void PlaceAdjacent(string instanceId, string otherInstanceId, string axis, float gap = 0,
            string basis = "renderer", CafeOrientationAnnotations annotations = null)
        {
            GeometryRequire(instanceId != otherInstanceId, "INVALID_RELATION", "Adjacency requires two different instances.");
            GeometryRequire(axis == "x+" || axis == "x-" || axis == "z+" || axis == "z-", "INVALID_AXIS", "Use x+, x-, z+ or z-.");
            GeometryRequire(basis == "renderer" || basis == "footprint", "INVALID_BASIS", "Use renderer or footprint bounds.");
            GeometryRequire(CafeApi.Finite(gap) && gap >= 0, "INVALID_GAP", "Adjacency gap must be finite and nonnegative.");
            var layout = CafeApi.Layout;
            var instance = GeometryInstance(layout, instanceId);
            var item = CafeApi.Find(instance.furnitureId);
            GeometryRequire(!CafeApi.IsRoomStyle(item), "INVALID_PLACEMENT", "Room styles cannot be moved adjacent to furniture.");
            var pair = InspectPair(instanceId, otherInstanceId, layout, annotations);
            var a = basis == "renderer" ? pair.first.rendererWorldBounds : pair.first.placementFootprintWorldBounds;
            var b = basis == "renderer" ? pair.second.rendererWorldBounds : pair.second.placementFootprintWorldBounds;
            GeometryRequire(a.available && b.available, "GEOMETRY_UNAVAILABLE", "Requested bounds are unavailable.");
            int index = axis[0] == 'x' ? 0 : 2;
            int cross = index == 0 ? 2 : 0;
            GeometryRequire(Mathf.Min(a.max[cross], b.max[cross]) - Mathf.Max(a.min[cross], b.min[cross]) >= -AuthoringTolerance,
                "UNACHIEVABLE_ADJACENCY", "Bounds must overlap on the other horizontal axis; move to that axis explicitly first.");
            float translation = axis[1] == '+' ? b.max[index] + gap - a.min[index] : b.min[index] - gap - a.max[index];
            var desired = instance.position;
            desired[index] += translation;
            var constrained = CafeApi.Constrain(item, desired, instance.quarterTurns, layout.room);
            constrained.y = instance.position.y;
            GeometryRequire(Vector3.Distance(constrained, desired) < AuthoringTolerance,
                "UNACHIEVABLE_ADJACENCY", "Requested bounds gap cannot be achieved on the placement grid and wall attachment.");
            int q = item.placement == "wall" ? (Mathf.Abs(constrained.x + layout.room.width / 2) < .001f ? 0 : 1) : instance.quarterTurns;
            GeometryRequire(q == instance.quarterTurns, "UNACHIEVABLE_ADJACENCY", "Adjacency would change wall attachment and measured orientation.");
            instance.position = desired;
            CafeApi.ValidateLayout(layout);
            // Same rotation and renderer assembly; translating the pivot translates
            // these bounds identically. No post-commit checks or second mutation.
            CafeApi.Move(instanceId, desired.x, desired.z, desired.y);
        }

        internal static string SearchableItemText(CafeItem item, CafeOrientationAnnotations annotations)
        {
            var derived = annotations?.items.FirstOrDefault(a => a.furnitureId == item.id &&
                (string.IsNullOrWhiteSpace(a.sourcePrefab) || a.sourcePrefab == item.sourcePrefab));
            return string.Join(" ", new[] { item.name, item.id, item.sourcePrefab, item.category, item.theme }) + " " +
                string.Join(" ", derived?.aliases ?? Array.Empty<string>());
        }
    }
}
