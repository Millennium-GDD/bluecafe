using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafeGeometryTests
    {
        CafeCatalog original;
        FieldInfo catalogField;
        string meshPath;
        CafeLayout layout;
        CafeOrientationAnnotations annotations;

        [SetUp] public void SetUp()
        {
            original = CafeApi.Catalog;
            catalogField = typeof(CafeApi).GetField("catalog", BindingFlags.Static | BindingFlags.NonPublic);
            meshPath = Path.GetTempFileName();
            File.WriteAllText(meshPath, JsonUtility.ToJson(new CafeMeshData {
                vertices = new[] { 0f,0f,0f, 2f,0f,0f, 0f,2f,1f },
                submeshes = new[] { new CafeSubmesh { triangles = new[] { 0,1,2 } } }
            }));
            var floor = new CafeItem { id = "geometry", sourcePrefab = "fixture", placement = "floor", available = true,
                width = 1, depth = 2, height = 1,
                parts = new[] { new CafePart { meshJsonPath = meshPath, position = new[] { 1f,0f,0f }, scale = new[] { 2f,1f,1f } } } };
            var wall = new CafeItem { id = "wall", sourcePrefab = "wall-fixture", placement = "wall", available = true,
                width = 4, depth = 1, height = 3, parts = floor.parts,
                bounds = new CafeBounds { min = new[] { 1f,0f,0f }, max = new[] { 5f,2f,1f } } };
            catalogField.SetValue(null, new CafeCatalog { gridUnit = .6f, items = new[] { floor, wall,
                new CafeItem { id = "missing", placement = "floor", available = false },
                new CafeItem { id = "empty", placement = "floor", available = true } } });
            layout = new CafeLayout { room = new CafeRoom { width = 12, depth = 12 }, instances = {
                new CafeInstance { instanceId = "a", furnitureId = "geometry", position = new Vector3(1.2f,0,.6f), quarterTurns = 1 } } };
            annotations = new CafeOrientationAnnotations { items = new[] { new CafeOrientationAnnotation {
                furnitureId = "geometry", sourcePrefab = "fixture", aliases = new[] { "fixture seat" },
                directions = new[] { new CafeAuditedVector { name = "seat", status = "audited", local = Vector3.right, evidence = "synthetic asymmetry fixture" } },
                anchors = new[] { new CafeAuditedVector { name = "work", status = "audited", local = new Vector3(3,1,0), evidence = "fixture measured point" } }
            } } };
        }
        [TearDown] public void TearDown() { catalogField.SetValue(null, original); File.Delete(meshPath); }

        [Test] public void InspectionUsesActualTransformedMeshAndSeparatePlacementFootprint()
        {
            string before = CafeApi.ExportJson();
            var a = CafeReconstruction.InspectInstance("a", layout, annotations);
            Assert.AreEqual("renderer-bounds", a.geometryStatus);
            Assert.AreEqual(1, a.rendererCount);
            AssertVector(new Vector3(1.2f,0,-4.4f), a.rendererWorldBounds.min);
            AssertVector(new Vector3(2.2f,2,-.4f), a.rendererWorldBounds.max);
            AssertVector(new Vector3(1.2f,0,.6f), a.placementFootprintWorldBounds.size);
            AssertVector(new Vector3(0,0,-1), a.directions.Single(v => v.name == "seat").world);
            AssertVector(new Vector3(1.2f,1,-2.4f), a.anchors.Single().world);
            Assert.AreEqual("unknown", a.directions.Single(v => v.name == "front").status);
            Assert.AreEqual(before, CafeApi.ExportJson(), "Inspection must not commit session changes.");
        }

        [TestCase(0, 1, 0, 0)]
        [TestCase(1, 0, 0, -1)]
        [TestCase(2, -1, 0, 0)]
        [TestCase(3, 0, 0, 1)]
        public void AuditedDirectionsFollowAllPlacementQuarterTurns(int q, float x, float y, float z)
        {
            layout.instances[0].quarterTurns = q;
            AssertVector(new Vector3(x,y,z), CafeReconstruction.InspectInstance("a", layout, annotations).directions.Single(v => v.name == "seat").world);
        }

        [Test] public void WallNativePivotOffsetIsAppliedToAnchorsAndRendererBounds()
        {
            layout.instances[0] = new CafeInstance { instanceId = "a", furnitureId = "wall", position = new Vector3(-6,2.4f,0) };
            annotations.items[0].furnitureId = "wall"; annotations.items[0].sourcePrefab = "wall-fixture";
            var a = CafeReconstruction.InspectInstance("a", layout, annotations);
            AssertVector(new Vector3(-6,1.4f,0), a.nativePivotWorld);
            AssertVector(new Vector3(-5,1.4f,0), a.rendererWorldBounds.min);
            AssertVector(new Vector3(-3,2.4f,0), a.anchors.Single().world);
            AssertVector(new Vector3(0,0,2.4f), a.placementFootprintWorldBounds.size);
        }

        [TestCase(0, 0, 2.4f)]
        [TestCase(1, 2.4f, 0)]
        public void WallHorizontalFootprintIsAnAlongWallSegmentWithoutNormalThickness(int q, float width, float depth)
        {
            var position = q == 0 ? new Vector3(-6,2.4f,0) : new Vector3(0,2.4f,6);
            layout.instances[0] = new CafeInstance { instanceId = "a", furnitureId = "wall", position = position, quarterTurns = q };
            // Wall catalog depth is vertical source size. It cannot change the
            // horizontal placement segment or the actual renderer mesh geometry.
            CafeApi.Find("wall").depth = 30;
            var a = CafeReconstruction.InspectInstance("a", layout, annotations);
            AssertVector(new Vector3(width,0,depth), a.placementFootprintWorldBounds.size);
            AssertVector(position, a.placementFootprintWorldBounds.center);
            AssertVector(q == 0 ? new Vector3(4,2,1) : new Vector3(1,2,4), a.rendererWorldBounds.size);
        }

        [Test] public void MissingAssetsAndMissingRendererGeometryStayExplicit()
        {
            layout.instances[0].furnitureId = "missing";
            var a = CafeReconstruction.InspectInstance("a", layout, annotations);
            Assert.AreEqual("asset-unavailable", a.geometryStatus);
            Assert.IsFalse(a.rendererWorldBounds.available);
            Assert.AreEqual("unknown", a.directions.Single().status);
            layout.instances[0].furnitureId = "empty";
            Assert.AreEqual("no-renderers", CafeReconstruction.InspectInstance("a", layout, annotations).geometryStatus);
        }

        [Test] public void PairReportsSignedEdgeGapsIndependentlyOfPivotDistance()
        {
            layout.instances[0].position = Vector3.zero; layout.instances[0].quarterTurns = 0;
            layout.instances.Add(new CafeInstance { instanceId = "b", furnitureId = "geometry", position = new Vector3(6,0,0) });
            var pair = CafeReconstruction.InspectPair("a", "b", layout, annotations);
            Assert.AreEqual(6, pair.pivotDistanceWorld, .001f);
            AssertVector(new Vector3(2,-2,-1), pair.rendererSignedEdgeGaps);
            AssertVector(new Vector3(5.4f,0,-1.2f), pair.footprintSignedEdgeGaps);
            layout.instances[1].position.x = 4;
            Assert.AreEqual(0, CafeReconstruction.InspectPair("a", "b", layout, annotations).rendererSignedEdgeGaps.x, .001f);
            layout.instances[1].position.x = 3;
            Assert.AreEqual(-1, CafeReconstruction.InspectPair("a", "b", layout, annotations).rendererSignedEdgeGaps.x, .001f);
        }

        [Test] public void AnnotationValidationRejectsGuessesUnsupportedVersionsAndDuplicateIds()
        {
            annotations.items[0].directions[0].status = "unknown";
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateOrientations(annotations));
            annotations.items[0].directions[0].status = "audited";
            annotations.items[0].directions[0].evidence = "";
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateOrientations(annotations));
            annotations.items[0].directions[0].evidence = "fixture";
            annotations.items[0].directions[0].local = Vector3.right * 2;
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateOrientations(annotations));
            annotations.items[0].directions[0].local = Vector3.right;
            annotations.version = 2;
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateOrientations(annotations));
            annotations.version = 1;
            annotations.items = new[] { annotations.items[0], annotations.items[0] };
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.ValidateOrientations(annotations));
        }

        [Test] public void StaleSourceIdentityIsRejectedInsteadOfApplyingPriorAudit()
        {
            annotations.items[0].sourcePrefab = "different-source";
            var error = Assert.Throws<CafeValidationException>(() => CafeReconstruction.InspectInstance("a", layout, annotations));
            Assert.AreEqual("STALE_ANNOTATION", error.code);
        }
        static void AssertVector(Vector3 expected, Vector3 actual) => Assert.Less(Vector3.Distance(expected, actual), .0001f);
    }
}
