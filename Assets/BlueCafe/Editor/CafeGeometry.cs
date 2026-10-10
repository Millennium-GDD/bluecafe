using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BlueCafe
{
    // Derived observations only. Coordinates use the assembled native prefab frame,
    // before placement yaw and before the wall-center pivot offset in CafePreview.
    [Serializable] public class CafeOrientationAnnotations
    {
        public int version = 1;
        public string coordinateFrame = "assembled-native-prefab";
        public CafeOrientationAnnotation[] items = Array.Empty<CafeOrientationAnnotation>();
    }
    [Serializable] public class CafeOrientationAnnotation
    {
        public string furnitureId, sourcePrefab;
        public string[] aliases = Array.Empty<string>();
        public CafeAuditedVector[] directions = Array.Empty<CafeAuditedVector>();
        public CafeAuditedVector[] principalAxes = Array.Empty<CafeAuditedVector>();
        public CafeAuditedVector[] anchors = Array.Empty<CafeAuditedVector>();
    }
    [Serializable] public class CafeAuditedVector
    {
        public string name, status = "unknown", evidence;
        public Vector3 local;
    }
    [Serializable] public class CafeInspectedVector
    {
        public string name, status, evidence;
        public Vector3 local, world;
    }
    [Serializable] public class CafeInspectedBounds
    {
        public bool available;
        public Vector3 min, max, center, size;
        public static CafeInspectedBounds From(Bounds? bounds)
        {
            return bounds.HasValue ? new CafeInspectedBounds { available = true, min = bounds.Value.min,
                max = bounds.Value.max, center = bounds.Value.center, size = bounds.Value.size } : new CafeInspectedBounds();
        }
    }
    [Serializable] public class CafeInstanceInspection
    {
        public string instanceId, furnitureId, placement, sourcePrefab, geometryStatus, orientationStatus;
        public int quarterTurns, rendererCount;
        public Vector3 placementPivotWorld, nativePivotWorld;
        public bool nativePivotApplicable, placementFootprintApplicable;
        public CafeInspectedBounds rendererWorldBounds, placementFootprintWorldBounds;
        public CafeInspectedVector[] directions, principalAxes, anchors;
        public string[] aliases;
    }
    [Serializable] public class CafePairInspection
    {
        public CafeInstanceInspection first, second;
        public Vector3 pivotDeltaWorld;
        public float pivotDistanceWorld;
        public bool rendererGapsAvailable, footprintGapsAvailable;
        // Positive = separated, zero = touching, negative = overlap depth on that
        // axis. AABB gaps do not establish physical collision or semantic adjacency.
        public Vector3 rendererSignedEdgeGaps, footprintSignedEdgeGaps;
    }
    public static partial class CafeReconstruction
    {
        public const string OrientationPath = "docs/reconstruction/orientations.json";

        public static CafeOrientationAnnotations LoadOrientations(string path = OrientationPath)
        {
            if (!File.Exists(path)) throw new CafeValidationException("ANNOTATIONS_UNAVAILABLE", path);
            CafeOrientationAnnotations annotations;
            try { annotations = JsonUtility.FromJson<CafeOrientationAnnotations>(File.ReadAllText(path)); }
            catch (Exception e) { throw new CafeValidationException("INVALID_ANNOTATIONS", e.Message); }
            ValidateOrientations(annotations);
            return annotations;
        }

        public static void ValidateOrientations(CafeOrientationAnnotations annotations)
        {
            GeometryRequire(annotations != null && annotations.version == 1 && annotations.coordinateFrame == "assembled-native-prefab" && annotations.items != null,
                "INVALID_ANNOTATIONS", "Expected version 1 assembled-native-prefab annotations.");
            var ids = new HashSet<string>();
            foreach (var item in annotations.items)
            {
                GeometryRequire(item != null && !string.IsNullOrWhiteSpace(item.furnitureId) && ids.Add(item.furnitureId), "INVALID_ANNOTATIONS", "Furniture ids must be unique and nonempty.");
                GeometryRequire(item.aliases != null && item.aliases.All(a => !string.IsNullOrWhiteSpace(a)) && item.aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() == item.aliases.Length,
                    "INVALID_ANNOTATIONS", "Aliases must be nonempty and unique.");
                ValidateAuditedVectors(item.directions, false);
                ValidateAuditedVectors(item.principalAxes, false);
                ValidateAuditedVectors(item.anchors, true);
                bool audited = item.directions.Concat(item.principalAxes).Concat(item.anchors).Any(v => v.status == "audited");
                GeometryRequire(!audited || !string.IsNullOrWhiteSpace(item.sourcePrefab), "INVALID_ANNOTATIONS", "Audited records need sourcePrefab identity.");
            }
        }

        static void ValidateAuditedVectors(CafeAuditedVector[] vectors, bool points)
        {
            GeometryRequire(vectors != null, "INVALID_ANNOTATIONS", "Vector arrays must be explicit.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vectors)
            {
                GeometryRequire(v != null && !string.IsNullOrWhiteSpace(v.name) && names.Add(v.name), "INVALID_ANNOTATIONS", "Vector names must be unique and nonempty.");
                GeometryRequire(v.status == "unknown" || v.status == "audited", "INVALID_ANNOTATIONS", "Status must be unknown or audited.");
                GeometryRequire(FiniteVector(v.local), "INVALID_ANNOTATIONS", "Coordinates must be finite.");
                if (v.status == "unknown") GeometryRequire(v.local == Vector3.zero, "INVALID_ANNOTATIONS", "Unknown vectors cannot encode a guessed direction or anchor.");
                else
                {
                    GeometryRequire(!string.IsNullOrWhiteSpace(v.evidence), "INVALID_ANNOTATIONS", "Audited vectors require evidence.");
                    GeometryRequire(points || Mathf.Abs(v.local.magnitude - 1) < .001f, "INVALID_ANNOTATIONS", "Audited directions and axes must be unit vectors.");
                }
            }
        }

        public static CafeInstanceInspection InspectInstance(string instanceId, CafeLayout layout = null, CafeOrientationAnnotations annotations = null)
        {
            layout = layout ?? CafeApi.Layout;
            annotations = annotations ?? LoadOrientations();
            ValidateOrientations(annotations);
            var instance = GeometryInstance(layout, instanceId);
            using (var preview = new CafePreview())
            {
                preview.Rebuild(IsolatedGeometryLayout(layout, instance));
                return InspectGeometry(preview, instance, annotations);
            }
        }

        public static CafePairInspection InspectPair(string firstId, string secondId, CafeLayout layout = null, CafeOrientationAnnotations annotations = null)
        {
            layout = layout ?? CafeApi.Layout;
            annotations = annotations ?? LoadOrientations();
            ValidateOrientations(annotations);
            var first = GeometryInstance(layout, firstId);
            var second = GeometryInstance(layout, secondId);
            using (var preview = new CafePreview())
            {
                preview.Rebuild(IsolatedGeometryLayout(layout, first, second));
                var a = InspectGeometry(preview, first, annotations);
                var b = InspectGeometry(preview, second, annotations);
                var result = new CafePairInspection { first = a, second = b,
                    pivotDeltaWorld = b.placementPivotWorld - a.placementPivotWorld,
                    pivotDistanceWorld = Vector3.Distance(a.placementPivotWorld, b.placementPivotWorld),
                    rendererGapsAvailable = a.rendererWorldBounds.available && b.rendererWorldBounds.available,
                    footprintGapsAvailable = a.placementFootprintWorldBounds.available && b.placementFootprintWorldBounds.available };
                if (result.rendererGapsAvailable) result.rendererSignedEdgeGaps = SignedEdgeGaps(a.rendererWorldBounds, b.rendererWorldBounds);
                if (result.footprintGapsAvailable) result.footprintSignedEdgeGaps = SignedEdgeGaps(a.placementFootprintWorldBounds, b.placementFootprintWorldBounds);
                return result;
            }
        }

        // Public for consumers measuring alternate audited AABBs. Unlike center
        // distance minus half-spans this correctly handles containment overlap.
        public static Vector3 SignedEdgeGaps(CafeInspectedBounds a, CafeInspectedBounds b)
        {
            GeometryRequire(a != null && b != null && a.available && b.available, "GEOMETRY_UNAVAILABLE", "Both bounds must be available.");
            return new Vector3(Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x),
                Mathf.Max(b.min.y - a.max.y, a.min.y - b.max.y), Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z));
        }

        static CafeInstance GeometryInstance(CafeLayout layout, string id)
        {
            GeometryRequire(layout != null && layout.instances != null, "INVALID_LAYOUT", "Layout and instances are required.");
            CafeApi.ValidateRoom(layout.room);
            var matches = layout.instances.Where(i => i != null && i.instanceId == id).ToArray();
            GeometryRequire(!string.IsNullOrWhiteSpace(id) && matches.Length == 1, "UNKNOWN_INSTANCE", "Expected one instance: " + id);
            var instance = matches[0];
            GeometryRequire(FiniteVector(instance.position) && instance.quarterTurns >= 0 && instance.quarterTurns <= 3, "INVALID_TRANSFORM", id);
            return instance;
        }

        static CafeLayout IsolatedGeometryLayout(CafeLayout layout, params CafeInstance[] instances)
        {
            // Reuse exact source transforms and renderer assembly, without materializing
            // every unrelated furniture item. No session, placement or scene mutation.
            return new CafeLayout { room = layout.room, instances = instances.Distinct().ToList() };
        }

        static CafeInstanceInspection InspectGeometry(CafePreview preview, CafeInstance instance, CafeOrientationAnnotations annotations)
        {
            var item = CafeApi.Find(instance.furnitureId);
            var go = preview.Objects[instance.instanceId];
            var annotation = annotations.items.FirstOrDefault(a => a.furnitureId == item.id);
            if (annotation != null && !string.IsNullOrWhiteSpace(annotation.sourcePrefab))
                GeometryRequire(annotation.sourcePrefab == item.sourcePrefab, "STALE_ANNOTATION", "Source prefab identity changed: " + item.id);
            // Use the renderer's actual native anchor. Audited masking/scenery
            // geometry can require a source-backed visible-frame wall anchor.
            var nativeOffset = preview.NativePivotOffsetFor(instance.instanceId);
            bool nativeApplicable = !CafeApi.IsRoomStyle(item);
            var bounds = item.available ? preview.BoundsFor(instance.instanceId) : null;
            var result = new CafeInstanceInspection { instanceId = instance.instanceId, furnitureId = item.id,
                sourcePrefab = item.sourcePrefab, placement = item.placement, quarterTurns = instance.quarterTurns,
                placementPivotWorld = go.transform.position, nativePivotApplicable = nativeApplicable,
                nativePivotWorld = nativeApplicable ? go.transform.TransformPoint(nativeOffset) : Vector3.zero,
                rendererCount = go.GetComponentsInChildren<Renderer>().Length,
                rendererWorldBounds = CafeInspectedBounds.From(bounds),
                geometryStatus = !item.available ? "asset-unavailable" : bounds.HasValue ? "renderer-bounds" : "no-renderers",
                placementFootprintApplicable = nativeApplicable,
                placementFootprintWorldBounds = CafeInspectedBounds.From(nativeApplicable ? PlacementFootprint(item, instance) : (Bounds?)null),
                orientationStatus = annotation == null ? "unknown" : "annotated",
                aliases = annotation?.aliases ?? Array.Empty<string>() };
            result.directions = InspectVectors(annotation?.directions, go.transform, nativeOffset, false, nativeApplicable);
            // Always expose the front as unknown if it has not been audited.
            if (!result.directions.Any(v => v.name == "front")) result.directions = result.directions.Concat(new[] { new CafeInspectedVector { name = "front", status = "unknown" } }).ToArray();
            result.principalAxes = InspectVectors(annotation?.principalAxes, go.transform, nativeOffset, false, nativeApplicable);
            result.anchors = InspectVectors(annotation?.anchors, go.transform, nativeOffset, true, nativeApplicable);
            return result;
        }

        static CafeInspectedVector[] InspectVectors(CafeAuditedVector[] vectors, Transform transform, Vector3 nativeOffset, bool points, bool applicable)
        {
            return (vectors ?? Array.Empty<CafeAuditedVector>()).Select(v => new CafeInspectedVector {
                name = v.name, status = applicable ? v.status : "unknown", evidence = v.evidence,
                local = applicable && v.status == "audited" ? v.local : Vector3.zero,
                world = applicable && v.status == "audited" ? (points ? transform.TransformPoint(v.local + nativeOffset) : transform.TransformDirection(v.local)) : Vector3.zero
            }).ToArray();
        }

        static Bounds PlacementFootprint(CafeItem item, CafeInstance instance)
        {
            float unit = CafeApi.Catalog.gridUnit;
            float w = Mathf.Max(.01f, item.width * unit), d = Mathf.Max(.01f, item.depth * unit);
            if (item.placement == "wall")
            {
                // Placement validates only the along-wall width. Catalog wall depth
                // describes vertical source size, not wall-normal thickness. Elevation
                // validity is checked separately by CafeApi using the declared height.
                // q0 is a Z segment on the left wall; q1 is an X segment on the rear.
                float along = w; w = 0; d = along;
            }
            if (instance.quarterTurns % 2 == 1) { float swap = w; w = d; d = swap; }
            // This is the declared horizontal placement footprint, not a mesh volume.
            return new Bounds(instance.position, new Vector3(w, 0, d));
        }

        static bool FiniteVector(Vector3 v) => CafeApi.Finite(v.x) && CafeApi.Finite(v.y) && CafeApi.Finite(v.z);
        static void GeometryRequire(bool condition, string code, string message)
        { if (!condition) throw new CafeValidationException(code, message); }
    }
}
