using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafeAuthoringTests
    {
        CafeCatalog originalCatalog;
        string originalLayout, meshPath;
        FieldInfo catalogField;
        CafeOrientationAnnotations annotations;

        [SetUp] public void SetUp()
        {
            originalLayout = CafeApi.ExportJson(); originalCatalog = CafeApi.Catalog;
            catalogField = typeof(CafeApi).GetField("catalog", BindingFlags.Static | BindingFlags.NonPublic);
            meshPath = Path.GetTempFileName();
            File.WriteAllText(meshPath, JsonUtility.ToJson(new CafeMeshData {
                vertices = new[] { -.6f,0f,-.6f, .6f,0f,-.6f, -.6f,1f,.6f },
                submeshes = new[] { new CafeSubmesh { triangles = new[] { 0,1,2 } } }
            }));
            var parts = new[] { new CafePart { meshJsonPath = meshPath } };
            catalogField.SetValue(null, new CafeCatalog { gridUnit = .6f, items = new[] {
                new CafeItem { id = "chair", sourcePrefab = "chair-fixture", placement = "floor", width = 2, depth = 2, height = 1, available = true, parts = parts },
                new CafeItem { id = "wall", sourcePrefab = "wall-fixture", placement = "wall", width = 2, height = 2, available = true, parts = parts },
                new CafeItem { id = "empty", sourcePrefab = "empty-fixture", placement = "floor", width = 2, depth = 2, available = true },
                new CafeItem { id = "20201", name = "Original localized label", category = "Counter", theme = "Library", placement = "floor", available = false,
                    sourcePrefab = "Assets/_MX/AddressableAsset/Cafe/my_highlander_01_bar_01.prefab" }
            } });
            annotations = new CafeOrientationAnnotations { items = new[] { Annotation("chair", "chair-fixture"), Annotation("wall", "wall-fixture") } };
            CafeApi.NewLayout("Authoring fixture", 12, 12, .6f);
        }

        static CafeOrientationAnnotation Annotation(string id, string prefab) => new CafeOrientationAnnotation {
            furnitureId = id, sourcePrefab = prefab,
            directions = new[] { new CafeAuditedVector { name = "front", status = "audited", local = Vector3.right, evidence = "Synthetic authoring test direction" } }
        };

        [TearDown] public void TearDown()
        {
            catalogField.SetValue(null, originalCatalog); CafeApi.ImportJson(originalLayout);
            File.Delete(meshPath);
        }

        [TestCase("x=min", 0)] [TestCase("z=max", 1)]
        public void ExplicitWallIdentityHeightAndRotationSurviveUndoAndPersistence(string wall, int q)
        {
            string before = CafeApi.ExportJson();
            var id = CafeReconstruction.PlaceOnWall("wall", wall, 1.2f, 1.8f);
            var instance = CafeApi.Layout.instances.Single();
            Assert.AreEqual(q, instance.quarterTurns); Assert.AreEqual(1.8f, instance.position.y, .0001f);
            var expected = wall == "x=min" ? new Vector3(-6,1.8f,1.2f) : new Vector3(1.2f,1.8f,6);
            Assert.That(Vector3.Distance(expected, instance.position), Is.LessThan(.0001f));
            string after = CafeApi.ExportJson();
            CafeApi.UndoLast(); Assert.AreEqual(before, CafeApi.ExportJson());
            CafeApi.RedoLast(); Assert.AreEqual(after, CafeApi.ExportJson());
            CafeApi.NewLayout(); CafeApi.ImportJson(after); Assert.AreEqual(after, CafeApi.ExportJson());
            Assert.AreEqual(id, CafeApi.Layout.instances.Single().instanceId);
        }

        [Test] public void InvalidOrOffGridWallRequestsLeaveLayoutUnchanged()
        {
            string before = CafeApi.ExportJson();
            AssertCode("UNKNOWN_WALL", () => CafeReconstruction.PlaceOnWall("wall", "north", 0));
            AssertCode("INVALID_PLACEMENT", () => CafeReconstruction.PlaceOnWall("chair", "x=min", 0));
            AssertCode("UNACHIEVABLE_WALL_PLACEMENT", () => CafeReconstruction.PlaceOnWall("wall", "x=min", .13f));
            Assert.Throws<CafeValidationException>(() => CafeReconstruction.PlaceOnWall("wall", "z=max", 0, .13f));
            Assert.AreEqual(before, CafeApi.ExportJson());
        }

        [Test] public void FacingUsesAuditedDirectionAndOneUndoRecord()
        {
            var id = CafeApi.Place("chair", 0,0);
            string before = CafeApi.ExportJson();
            CafeReconstruction.FaceToward(id, new Vector3(0,0,3), "front", annotations);
            Assert.AreEqual(3, CafeApi.Layout.instances.Single().quarterTurns);
            string after = CafeApi.ExportJson();
            CafeApi.UndoLast(); Assert.AreEqual(before, CafeApi.ExportJson());
            CafeApi.RedoLast(); Assert.AreEqual(after, CafeApi.ExportJson());
            CafeApi.NewLayout(); CafeApi.ImportJson(after); Assert.AreEqual(after, CafeApi.ExportJson());
        }

        [Test] public void FacingAnotherInstanceUsesItsPlacementPivot()
        {
            var id = CafeApi.Place("chair", 0,0); var target = CafeApi.Place("chair", -3,0);
            CafeReconstruction.FaceToward(id, target, "front", annotations);
            Assert.AreEqual(2, CafeApi.Layout.instances.Single(i => i.instanceId == id).quarterTurns);
        }

        [Test] public void UnknownDiagonalCoincidentAndStaleFacingRequestsAreAtomic()
        {
            var id = CafeApi.Place("chair", 0,0); string before = CafeApi.ExportJson();
            AssertCode("UNKNOWN_DIRECTION", () => CafeReconstruction.FaceToward(id, Vector3.right, "work", annotations));
            AssertCode("UNACHIEVABLE_FACING", () => CafeReconstruction.FaceToward(id, new Vector3(3,0,3), "front", annotations));
            AssertCode("UNACHIEVABLE_FACING", () => CafeReconstruction.FaceToward(id, Vector3.zero, "front", annotations));
            annotations.items[0].sourcePrefab = "stale";
            AssertCode("STALE_ANNOTATION", () => CafeReconstruction.FaceToward(id, Vector3.right, "front", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
        }

        [Test] public void WallFacingOnlyUsesTheTwoSharedLegalAttachments()
        {
            var id = CafeReconstruction.PlaceOnWall("wall", "x=min", 0, 2.4f);
            CafeReconstruction.FaceToward(id, Vector3.zero, "front", annotations);
            Assert.AreEqual(0, CafeApi.Layout.instances.Single().quarterTurns);
            // From transferred (0,2.4,6), q1's audited +X direction is -Z.
            CafeReconstruction.FaceToward(id, new Vector3(0,0,-3), "front", annotations);
            var instance = CafeApi.Layout.instances.Single();
            Assert.AreEqual(1, instance.quarterTurns); Assert.AreEqual(new Vector3(0,2.4f,6), instance.position);
            string before = CafeApi.ExportJson();
            AssertCode("UNACHIEVABLE_FACING", () => CafeReconstruction.FaceToward(id, new Vector3(3,0,6), "front", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
            CafeApi.ValidateLayout(CafeApi.Layout);
        }

        [Test] public void GeometricallyMatchingFacingWithIllegalFootprintIsRejectedAtomically()
        {
            CafeApi.Find("chair").width = 4; CafeApi.Find("chair").depth = 1;
            var id = CafeApi.Place("chair", 0,5.4f);
            string before = CafeApi.ExportJson();
            AssertCode("UNACHIEVABLE_FACING", () => CafeReconstruction.FaceToward(id, Vector3.zero, "front", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
            CafeApi.NewLayout("Narrow rear wall",6,12,.6f);
            id = CafeReconstruction.PlaceOnWall("wall", "x=min", 4.8f, 2.4f);
            before = CafeApi.ExportJson();
            AssertCode("UNACHIEVABLE_FACING", () => CafeReconstruction.FaceToward(id, new Vector3(4.8f,0,0), "front", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
        }

        [TestCase("renderer", "x+", 1.8f, 0f)] [TestCase("footprint", "x-", -1.8f, 0f)]
        [TestCase("renderer", "z+", 0f, 1.8f)] [TestCase("footprint", "z-", 0f, -1.8f)]
        public void ExactAdjacencyUsesBoundsAndOneUndoRecord(string basis, string axis, float x, float z)
        {
            var other = CafeApi.Place("chair", 0,0); var id = CafeApi.Place("chair", 0,0);
            string before = CafeApi.ExportJson();
            CafeReconstruction.PlaceAdjacent(id, other, axis, .6f, basis, annotations);
            var instance = CafeApi.Layout.instances.Single(i => i.instanceId == id);
            Assert.Less(Vector3.Distance(new Vector3(x,0,z), instance.position), .0001f);
            var pair = CafeReconstruction.InspectPair(id, other, annotations: annotations);
            var gaps = basis == "renderer" ? pair.rendererSignedEdgeGaps : pair.footprintSignedEdgeGaps;
            Assert.AreEqual(.6f, axis[0] == 'x' ? gaps.x : gaps.z, .0001f);
            string after = CafeApi.ExportJson();
            CafeApi.UndoLast(); Assert.AreEqual(before, CafeApi.ExportJson());
            CafeApi.RedoLast(); Assert.AreEqual(after, CafeApi.ExportJson());
            CafeApi.NewLayout(); CafeApi.ImportJson(after); Assert.AreEqual(after, CafeApi.ExportJson());
        }

        [Test] public void UnachievableGapBoundsMissingAndOutsideRoomNeverPartiallyMove()
        {
            var other = CafeApi.Place("chair", 0,0); var id = CafeApi.Place("chair", 0,0);
            string before = CafeApi.ExportJson();
            AssertCode("UNACHIEVABLE_ADJACENCY", () => CafeReconstruction.PlaceAdjacent(id, other, "x+", .1f, "renderer", annotations));
            AssertCode("OUTSIDE_ROOM", () => CafeReconstruction.PlaceAdjacent(id, other, "x+", 12, "footprint", annotations));
            AssertCode("INVALID_AXIS", () => CafeReconstruction.PlaceAdjacent(id, other, "x", 0, "footprint", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
            var empty = CafeApi.Place("empty", 0,0); before = CafeApi.ExportJson();
            AssertCode("GEOMETRY_UNAVAILABLE", () => CafeReconstruction.PlaceAdjacent(id, empty, "x+", 0, "renderer", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
            // Failure did not introduce an undo group; the preceding place undoes.
            CafeApi.UndoLast(); Assert.IsFalse(CafeApi.Layout.instances.Any(i => i.instanceId == empty));
        }

        [Test] public void AdjacentWallMovementCannotChangeMeasuredOrientation()
        {
            var wall = CafeReconstruction.PlaceOnWall("wall", "x=min", 0, 2.4f);
            var other = CafeApi.Place("chair", -4.8f,0);
            string before = CafeApi.ExportJson();
            AssertCode("UNACHIEVABLE_ADJACENCY", () => CafeReconstruction.PlaceAdjacent(wall, other, "x+", 0, "renderer", annotations));
            Assert.AreEqual(before, CafeApi.ExportJson());
        }

        [Test] public void SearchFindsOriginalBarByPrefabAliasesThemeAndCategoryWhileUnavailable()
        {
            foreach (string query in new[] { "20201", "Original localized", "my_highlander_01_bar_01", "coffee service", "espresso", "register", "Library", "Counter" })
            {
                var item = CafeApi.Search(query).Single();
                Assert.AreEqual("20201", item.id); Assert.AreEqual("Original localized label", item.name); Assert.IsFalse(item.available);
            }
            Assert.IsEmpty(CafeApi.Search("coffee service", "Chair"));
            Assert.AreEqual("20201", CafeApi.Search("coffee service", "Counter", "Library").Single().id);
            CafeApi.Find("20201").sourcePrefab = "different-source";
            Assert.IsEmpty(CafeApi.Search("coffee service"), "Aliases must not attach to a changed source prefab.");
        }

        static void AssertCode(string expected, TestDelegate action) => Assert.AreEqual(expected, Assert.Throws<CafeValidationException>(action).code);
    }
}
