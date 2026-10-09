using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Tests
{
    public class CafePlacementTests
    {
        CafeCatalog originalCatalog;
        string originalLayout;
        FieldInfo catalogField;
        [SetUp] public void SetUp()
        {
            originalLayout = CafeApi.ExportJson(); originalCatalog = CafeApi.Catalog;
            catalogField = typeof(CafeApi).GetField("catalog", BindingFlags.Static | BindingFlags.NonPublic);
            catalogField.SetValue(null, new CafeCatalog { gridUnit = .6f, items = new[] {
                new CafeItem { id = "chair", name = "Chair", width = 1, depth = 2, height = 1, placement = "floor", available = true },
                new CafeItem { id = "wall", name = "Wall art", width = 4, height = 3, placement = "wall", available = true },
                new CafeItem { id = "missing", name = "Unavailable", placement = "floor", available = false },
                new CafeItem { id = "floor", name = "Floor", placement = "flooring", available = true }
            } });
            CafeApi.NewLayout("Test cafe", 6, 6, .6f);
        }
        [TearDown] public void TearDown() { catalogField.SetValue(null, originalCatalog); CafeApi.ImportJson(originalLayout); }
        [Test] public void PlacementSnapsNativeGridAndRotatesFootprint()
        {
            var id = CafeApi.Place("chair", 1.21f, .59f); var item = CafeApi.Layout.instances.Single();
            Assert.AreEqual(new Vector3(1.2f,0,.6f), item.position);
            CafeApi.Move(id, 2.4f, 0); CafeApi.Rotate(id); Assert.AreEqual(1, CafeApi.Layout.instances.Single().quarterTurns);
            Assert.Throws<CafeValidationException>(() => CafeApi.Move(id, 3, 0));
        }
        [Test] public void InvalidImportDoesNotModifyOpenLayout()
        {
            CafeApi.Place("chair", 0, 0); var before = CafeApi.ExportJson(); var invalid = CafeApi.Layout;
            invalid.instances[0].furnitureId = "unknown";
            Assert.Throws<CafeValidationException>(() => CafeApi.ImportJson(JsonUtility.ToJson(invalid)));
            Assert.AreEqual(before, CafeApi.ExportJson());
            Assert.Throws<CafeValidationException>(() => CafeApi.ImportJson("not json")); Assert.AreEqual(before, CafeApi.ExportJson());
        }
        [Test] public void NonFiniteCoordinatesAndInvalidRotationAreRejectedAtomically()
        {
            Assert.Throws<CafeValidationException>(() => CafeApi.Place("chair", float.NaN, 0));
            Assert.Throws<CafeValidationException>(() => CafeApi.Place("chair", 0, float.PositiveInfinity));
            Assert.Throws<CafeValidationException>(() => CafeApi.Place("chair", 0, 0, 5));
            Assert.IsEmpty(CafeApi.Layout.instances);
        }
        [Test] public void RoundTripPreservesStableIdsTransformsAndIndependentDuplicates()
        {
            var a = CafeApi.Place("chair", 0, 0, 1); var b = CafeApi.Duplicate(a); CafeApi.Move(b, 1.2f, 1.2f);
            var json = CafeApi.ExportJson(); CafeApi.NewLayout(); CafeApi.ImportJson(json);
            Assert.AreEqual(json, CafeApi.ExportJson()); Assert.AreNotEqual(a, b);
            CafeApi.Delete(a); Assert.AreEqual(b, CafeApi.Layout.instances.Single().instanceId);
        }
        [Test] public void WallAttachmentAndElevationRoundTripOnBothWalls()
        {
            var id = CafeApi.Place("wall", -2.9f, .6f); var item = CafeApi.Layout.instances.Single();
            Assert.AreEqual(-3, item.position.x); Assert.AreEqual(0, item.quarterTurns); Assert.AreEqual(2.4f, item.position.y, .001f);
            CafeApi.Rotate(id); item = CafeApi.Layout.instances.Single(); Assert.AreEqual(3,item.position.z); Assert.AreEqual(1,item.quarterTurns);
            CafeApi.ImportJson(CafeApi.ExportJson()); Assert.Throws<CafeValidationException>(() => CafeApi.Move(id, 0, 3, 0));
        }
        [Test] public void UnavailableModelsAreNotReplacedAndRoomStylesAreSingletons()
        {
            Assert.Throws<CafeValidationException>(() => CafeApi.Place("missing", 0, 0));
            CafeApi.Place("floor",0,0); CafeApi.Place("floor",0,0); Assert.AreEqual(1,CafeApi.Layout.instances.Count);
        }
        [Test] public void DuplicateInstanceIdsAndOffGridImportsAreRejected()
        {
            CafeApi.Place("chair", 0,0); var candidate = CafeApi.Layout; candidate.instances.Add(candidate.instances[0]);
            Assert.Throws<CafeValidationException>(() => CafeApi.ImportJson(JsonUtility.ToJson(candidate)));
            candidate = CafeApi.Layout; candidate.instances[0].position.x = .13f;
            Assert.Throws<CafeValidationException>(() => CafeApi.ImportJson(JsonUtility.ToJson(candidate)));
        }
        [Test] public void UndoRedoRestoresLastApiEditIndependently()
        {
            var id = CafeApi.Place("chair", 0,0); CafeApi.Move(id,1.2f,0);
            CafeApi.UndoLast(); Assert.AreEqual(0,CafeApi.Layout.instances.Single().position.x);
            CafeApi.RedoLast(); Assert.AreEqual(1.2f,CafeApi.Layout.instances.Single().position.x,.001f);
        }
        [Test] public void RoomResizeRejectsClippingAnExistingItem()
        {
            CafeApi.Place("chair", 2.4f,0); var before = CafeApi.ExportJson();
            Assert.Throws<CafeValidationException>(() => CafeApi.ResizeRoom(3,3,4.8f,.6f)); Assert.AreEqual(before,CafeApi.ExportJson());
        }
        [Test] public void WholeRoomSurfacesDoNotRepeatBecauseOfFloatNoise()
        {
            // The acquired JP wallpaper measures 11.999996m for a 12m room.
            // It must make two rear walls, rather than four overhanging walls.
            var floor = CafeApi.Find("floor");
            floor.bounds = new CafeBounds { min = new[] { -.3f, -.285f, -.3f }, max = new[] { 11.699996f, 0f, 11.699996f } };
            var paper = new CafeItem { id = "paper", name = "Full-room wallpaper", placement = "wallpaper", available = true,
                bounds = new CafeBounds { min = new[] { 0f, -.285f, 0f }, max = new[] { .1f, 4.515f, 11.999996f } } };
            CafeApi.Catalog.items = CafeApi.Catalog.items.Concat(new[] { paper }).ToArray();
            CafeApi.NewLayout("Whole-room surfaces",12,12,.6f);
            var floorId = CafeApi.Place("floor",0,0); var paperId = CafeApi.Place("paper",0,0);
            using (var preview = new CafePreview())
            {
                preview.Rebuild(CafeApi.Layout);
                Assert.AreEqual(1,preview.Objects[floorId].transform.childCount);
                Assert.AreEqual(2,preview.Objects[paperId].transform.childCount);
            }
        }
    }
}
