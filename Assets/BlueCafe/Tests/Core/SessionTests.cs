using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Core.Tests
{
    public class SessionTests
    {
        Catalog catalog;
        Session session;
        [SetUp] public void SetUp()
        {
            catalog = new Catalog { gridUnit = .6f, items = new[] {
                new Item { id = "chair", name = "Chair", width = 1, depth = 1, height = 1, placement = "floor", available = true },
                new Item { id = "wide", name = "Wide", width = 2, depth = 1, height = 1, placement = "floor", available = true },
                new Item { id = "desk", name = "Three-cell desk", width = 3, depth = 3, height = 1, placement = "floor", available = true },
                new Item { id = "wall", name = "Wall art", width = 2, depth = 1, height = 1, placement = "wall", available = true },
                new Item { id = "missing", name = "Unavailable", placement = "floor", available = false },
                new Item { id = "floor", name = "Floor", placement = "flooring", available = true }
            } };
            session = new Session(catalog, new Layout { room = new Room { width = 6, depth = 6, grid = .6f } });
        }
        string Place(string id, float x = 0, float z = 0, int turns = 0)
        {
            var result = session.Place(id, x, z, turns);
            Assert.IsTrue(result.success, result.code + ": " + result.message);
            return result.instanceIds.Single();
        }
        string Json() => JsonUtility.ToJson(session.Layout);
        Instance Get(string id) => session.Layout.instances.Single(i => i.instanceId == id);
        void UnchangedFailure(Func<CommandResult> command, string code)
        {
            var before = Json(); var revision = session.Revision; var dirty = session.IsDirty; var undo = session.CanUndo; var redo = session.CanRedo;
            var result = command(); Assert.IsFalse(result.success); Assert.AreEqual(code, result.code);
            Assert.AreEqual(revision, result.revision); Assert.AreEqual(revision, session.Revision);
            Assert.AreEqual(before, Json()); Assert.AreEqual(dirty, session.IsDirty);
            Assert.AreEqual(undo, session.CanUndo); Assert.AreEqual(redo, session.CanRedo);
        }
        [Test] public void PlacementPreservesLegacyCentersAndWallElevation()
        {
            var chair = Place("wide", 1.21f, .59f, 1);
            Assert.AreEqual(1.2f, Get(chair).position.x, .0001f); Assert.AreEqual(.6f, Get(chair).position.z, .0001f);
            var wall = Place("wall", -2.9f, .6f);
            Assert.AreEqual(-3, Get(wall).position.x); Assert.AreEqual(2.4f, Get(wall).position.y, .0001f); Assert.AreEqual(0, Get(wall).quarterTurns);
            var back = Place("wall", 0, 2.9f); Assert.AreEqual(3, Get(back).position.z); Assert.AreEqual(1, Get(back).quarterTurns);
        }
        [TestCase("desk", 0)] [TestCase("desk", 1)] [TestCase("desk", 2)] [TestCase("desk", 3)]
        [TestCase("wide", 0)] [TestCase("wide", 1)] [TestCase("wide", 2)] [TestCase("wide", 3)]
        public void FloorFootprintsCanMeetEveryRoomEdgeAtEachRotation(string furnitureId, int turns)
        {
            var item = catalog.items.Single(i => i.id == furnitureId);
            var width = (turns % 2 == 0 ? item.width : item.depth) * catalog.gridUnit;
            var depth = (turns % 2 == 0 ? item.depth : item.width) * catalog.gridUnit;
            var edgeX = 3 - width / 2; var edgeZ = 3 - depth / 2;
            foreach (var x in new[] { -edgeX, edgeX })
            {
                var id = Place(furnitureId, x, 0, turns);
                Assert.AreEqual(x, Get(id).position.x, .0001f);
                Assert.AreEqual(3, Math.Abs(Get(id).position.x) + width / 2, .0001f);
            }
            foreach (var z in new[] { -edgeZ, edgeZ })
            {
                var id = Place(furnitureId, 0, z, turns);
                Assert.AreEqual(z, Get(id).position.z, .0001f);
                Assert.AreEqual(3, Math.Abs(Get(id).position.z) + depth / 2, .0001f);
            }
            Assert.DoesNotThrow(() => Placement.ValidateLayout(catalog, session.Layout));
        }
        [Test] public void LegacyDeskMovesHalfCellToWallAndUndoesExactly()
        {
            var id = Place("desk", -1.8f, 0); var legacy = Json(); session.MarkSaved();
            Assert.IsTrue(session.Move(new[] { id }, -.3f, 0).success);
            Assert.AreEqual(-2.1f, Get(id).position.x, .0001f);
            UnchangedFailure(() => session.Move(new[] { id }, -.3f, 0), "OUTSIDE_ROOM");
            Assert.IsTrue(session.Undo().success); Assert.AreEqual(legacy, Json()); Assert.IsFalse(session.IsDirty);
            Assert.IsTrue(session.Redo().success); Assert.AreEqual(-2.1f, Get(id).position.x, .0001f);
        }
        [Test] public void FloorGroupHalfCellMoveAndCopyPreserveRelativeOffsets()
        {
            var a = Place("chair", -1.2f, -.6f); var b = Place("wide", .3f, .6f, 1);
            var before = Json();
            Assert.IsTrue(session.Move(new[] { a, b }, .31f, -.29f).success);
            Assert.AreEqual(-.9f, Get(a).position.x, .0001f); Assert.AreEqual(.6f, Get(b).position.x, .0001f);
            Assert.AreEqual(-.9f, Get(a).position.z, .0001f); Assert.AreEqual(.3f, Get(b).position.z, .0001f);
            var moved = Json(); var copy = session.Duplicate(new[] { a, b }, -.3f, .3f);
            Assert.IsTrue(copy.success);
            Assert.AreEqual(1.5f, Get(copy.instanceIds[1]).position.x - Get(copy.instanceIds[0]).position.x, .0001f);
            Assert.AreEqual(1.2f, Get(copy.instanceIds[1]).position.z - Get(copy.instanceIds[0]).position.z, .0001f);
            Assert.AreEqual(1, Get(copy.instanceIds[1]).quarterTurns);
            Assert.IsTrue(session.Undo().success); Assert.AreEqual(moved, Json());
            Assert.IsTrue(session.Undo().success); Assert.AreEqual(before, Json());
        }
        [Test] public void WallAndMixedGroupsRetainFullCellMovementAndAttachment()
        {
            var floor = Place("chair", .3f, 0); var wall = Place("wall", -3, 0);
            Assert.IsTrue(session.Move(new[] { floor, wall }, 0, .31f).success);
            Assert.AreEqual(.6f, Get(floor).position.z, .0001f); Assert.AreEqual(.6f, Get(wall).position.z, .0001f);
            Assert.AreEqual(.3f, Get(floor).position.x, .0001f);
            UnchangedFailure(() => session.Move(new[] { floor, wall }, .31f, 0), "ATTACHMENT_CONFLICT");
            var copy = session.Duplicate(new[] { floor, wall }, 0, .31f); Assert.IsTrue(copy.success);
            Assert.AreEqual(1.2f, Get(copy.instanceIds[1]).position.z, .0001f);
            Assert.AreEqual(-3, Get(copy.instanceIds[1]).position.x, .0001f);
            Assert.IsTrue(session.Move(new[] { wall }, 0, .31f).success);
            Assert.AreEqual(1.2f, Get(wall).position.z, .0001f);
        }
        [Test] public void HalfCellLayoutsRoundTripAndStrictImportsKeepHistoryOnFailure()
        {
            var id = Place("desk", -2.1f, .3f, 3); session.MarkSaved(); var json = Json();
            var loaded = JsonUtility.FromJson<Layout>(json); var reopened = new Session(catalog, loaded);
            Assert.AreEqual(json, JsonUtility.ToJson(reopened.Layout)); Assert.IsFalse(reopened.IsDirty);
            var other = new Session(catalog); Assert.IsTrue(other.Replace(loaded).success);
            Assert.AreEqual(json, JsonUtility.ToJson(other.Layout));
            session.Move(new[] { id }, .3f, 0); session.Undo(); Assert.IsTrue(session.CanRedo);
            var invalid = session.Layout; invalid.instances[0].position.x = -1.95f;
            UnchangedFailure(() => session.Replace(invalid), "OFF_GRID");
            invalid = session.Layout; invalid.instances[0].position.x = -2.4f;
            UnchangedFailure(() => session.Replace(invalid), "OUTSIDE_ROOM");
            invalid = session.Layout; invalid.instances[0].position.z = float.PositiveInfinity;
            UnchangedFailure(() => session.Replace(invalid), "INVALID_TRANSFORM");
            Assert.IsTrue(session.Redo().success);
        }
        [Test] public void RigidRotationUsesFootprintCenterAndOneUndoStep()
        {
            var a = Place("chair", -.6f, 0); var b = Place("chair", .6f, 0); var before = Json(); var revision = session.Revision;
            Assert.IsTrue(session.Rotate(new[] { a, b }).success);
            Assert.AreEqual(revision + 1, session.Revision);
            Assert.AreEqual(0, Get(a).position.x, .0001f); Assert.AreEqual(.6f, Get(a).position.z, .0001f);
            Assert.AreEqual(0, Get(b).position.x, .0001f); Assert.AreEqual(-.6f, Get(b).position.z, .0001f);
            Assert.AreEqual(1, Get(a).quarterTurns); Assert.AreEqual(1, Get(b).quarterTurns);
            Assert.IsTrue(session.Undo().success); Assert.AreEqual(before, Json());
            Assert.IsTrue(session.Redo().success); Assert.AreEqual(-.6f, Get(b).position.z, .0001f);
        }
        [Test] public void AsymmetricFootprintCenterIsNotReplacedWithAveragePivot()
        {
            // Unequal widths place the footprint center at x=.45 rather than x=.3.
            var a = Place("chair", -1.2f, 0); var b = Place("chair", 1.8f, 0);
            // Use two wide items with turns making one plan footprint narrower.
            var candidate = session.Layout; candidate.instances[0].furnitureId = "wide"; candidate.instances[0].quarterTurns = 1;
            candidate.instances[1].furnitureId = "wide"; candidate.instances[1].quarterTurns = 0;
            Assert.IsTrue(session.Replace(candidate).success);
            // This center falls between half cells; exact rotation must reject it.
            UnchangedFailure(() => session.Rotate(new[] { a, b }), "OFF_GRID");
        }
        [Test] public void SharedTranslationSnappingPreservesOffsets()
        {
            var a = Place("chair", -1.2f, -.6f); var b = Place("wide", .6f, .6f);
            Assert.IsTrue(session.Move(new[] { a, b }, .59f, .61f).success);
            Assert.AreEqual(-.6f, Get(a).position.x, .0001f); Assert.AreEqual(1.2f, Get(b).position.x, .0001f);
            Assert.AreEqual(1.8f, Get(b).position.x - Get(a).position.x, .0001f);
            Assert.AreEqual(1.2f, Get(b).position.z - Get(a).position.z, .0001f);
        }
        [Test] public void MixedSelectionRejectsDetachmentAtomically()
        {
            var floor = Place("chair", 0, 0); var wall = Place("wall", -3, 0);
            UnchangedFailure(() => session.Move(new[] { floor, wall }, .6f, 0), "ATTACHMENT_CONFLICT");
            UnchangedFailure(() => session.Rotate(new[] { floor, wall }), "ATTACHMENT_CONFLICT");
            Assert.IsTrue(session.Move(new[] { floor, wall }, 0, .6f).success);
            Assert.AreEqual(.6f, Get(floor).position.z, .0001f); Assert.AreEqual(.6f, Get(wall).position.z, .0001f);
        }
        [Test] public void InvalidDestinationAndNonFiniteCommandsKeepRedoHistory()
        {
            var a = Place("chair", 0, 0); var b = Place("chair", 2.4f, 0);
            session.Move(new[] { a }, .6f, 0); session.Undo(); Assert.IsTrue(session.CanRedo);
            UnchangedFailure(() => session.Move(new[] { a, b }, .6f, 0), "OUTSIDE_ROOM");
            UnchangedFailure(() => session.Move(new[] { a }, float.NaN, 0), "INVALID_TRANSFORM");
            UnchangedFailure(() => session.Place("chair", float.PositiveInfinity, 0), "INVALID_TRANSFORM");
            UnchangedFailure(() => session.Place("chair", 0, 0, 4), "INVALID_ROTATION");
            Assert.IsTrue(session.Redo().success);
        }
        [Test] public void CopiesPreserveTransformsAndNewIdsAsOneOperation()
        {
            var a = Place("chair", -.6f, 0, 1); var b = Place("wide", .6f, 0, 1);
            var before = Json(); var first = session.Duplicate(new[] { a, b }, 0, .6f); Assert.IsTrue(first.success);
            Assert.AreEqual(2, first.instanceIds.Length); Assert.IsFalse(first.instanceIds.Contains(a)); Assert.IsFalse(first.instanceIds.Contains(b));
            Assert.AreEqual(1, Get(first.instanceIds[0]).quarterTurns); Assert.AreEqual(.6f, Get(first.instanceIds[1]).position.z, .0001f);
            Assert.AreEqual(0, Get(a).position.z); Assert.AreEqual(0, Get(b).position.z);
            var second = session.Duplicate(new[] { a, b }, 0, 1.2f); Assert.IsTrue(second.success);
            Assert.IsEmpty(first.instanceIds.Intersect(second.instanceIds)); Assert.AreEqual(6, session.Layout.instances.Count);
            session.Undo(); Assert.AreEqual(4, session.Layout.instances.Count); session.Undo(); Assert.AreEqual(before, Json());
        }
        [Test] public void FailedCopyAndPartialDeleteCommitNothing()
        {
            var a = Place("chair", 2.4f, 0); var b = Place("chair", 0, 0);
            UnchangedFailure(() => session.Duplicate(new[] { a, b }, .6f, 0), "OUTSIDE_ROOM");
            UnchangedFailure(() => session.Delete(new[] { a, "unknown" }), "UNKNOWN_INSTANCE");
            var before = Json(); Assert.IsTrue(session.Delete(new[] { a, b }).success); Assert.IsEmpty(session.Layout.instances);
            session.Undo(); Assert.AreEqual(before, Json());
        }
        [Test] public void VersionOneJsonRoundTripPreservesIdentityAndExactTransforms()
        {
            Place("chair", 1.2f, -.6f, 3); Place("wall", -3, .6f); Place("floor");
            var json = Json(); var loaded = JsonUtility.FromJson<Layout>(json); var other = new Session(catalog);
            Assert.IsTrue(other.Replace(loaded).success); Assert.AreEqual(json, JsonUtility.ToJson(other.Layout));
            Assert.AreEqual(1, loaded.version); Assert.IsTrue(json.Contains("\"x\"")); Assert.IsFalse(json.Contains("assetPath"));
        }
        [Test] public void ImportPreflightPreservesStateForUnknownIdsDuplicateIdsAndOffGrid()
        {
            var a = Place("chair"); var replacement = session.Layout; replacement.instances[0].furnitureId = "unknown";
            UnchangedFailure(() => session.Replace(replacement), "UNKNOWN_ITEM");
            replacement = session.Layout; replacement.instances.Add(Copies.Copy(replacement.instances[0]));
            UnchangedFailure(() => session.Replace(replacement), "INVALID_INSTANCE");
            replacement = session.Layout; replacement.instances[0].position.x = .13f;
            UnchangedFailure(() => session.Replace(replacement), "OFF_GRID");
            replacement = session.Layout; replacement.instances[0].position.y = float.NaN;
            UnchangedFailure(() => session.Replace(replacement), "INVALID_TRANSFORM");
            replacement = session.Layout; replacement.version = 2;
            UnchangedFailure(() => session.Replace(replacement), "INVALID_LAYOUT");
            replacement = session.Layout; replacement.room.width = 1;
            replacement.instances[0].furnitureId = "wide";
            UnchangedFailure(() => session.Replace(replacement), "OUTSIDE_ROOM");
            Assert.AreEqual(a, session.Layout.instances.Single().instanceId);
        }
        [Test] public void KnownUnavailableImportRetainsTransformButCannotBePlaced()
        {
            var candidate = session.Layout;
            candidate.instances.Add(new Instance { instanceId = "missing-stable", furnitureId = "missing", position = new Position(.6f, 0, 1.2f), quarterTurns = 2 });
            Assert.IsTrue(session.Replace(candidate).success);
            Assert.AreEqual(.6f, Get("missing-stable").position.x); Assert.AreEqual(2, Get("missing-stable").quarterTurns);
            Assert.Throws<ValidationException>(() => Placement.ValidateLayout(catalog, candidate));
            Assert.DoesNotThrow(() => Placement.ValidateLayout(catalog, candidate, true));
            UnchangedFailure(() => session.Place("missing", 0, 0), "ASSET_UNAVAILABLE");
        }
        [Test] public void RevisionConflictsCannotBecomeCurrentAgainAfterUndo()
        {
            var a = Place("chair"); var expected = session.Revision;
            session.Move(new[] { a }, .6f, 0); session.Undo(); Assert.Greater(session.Revision, expected);
            UnchangedFailure(() => session.Delete(new[] { a }, expected), "REVISION_CONFLICT");
            UnchangedFailure(() => session.MarkSaved(expected), "REVISION_CONFLICT");
            Assert.IsTrue(session.Move(new[] { a }, .6f, 0, session.Revision).success);
        }
        [Test] public void SavedStateTracksContentAcrossUndoAndFailedSave()
        {
            Assert.IsFalse(session.IsDirty); var a = Place("chair"); Assert.IsTrue(session.IsDirty);
            Assert.IsTrue(session.MarkSaved(session.Revision).success); var savedRevision = session.SavedRevision; Assert.IsFalse(session.IsDirty);
            session.Move(new[] { a }, .6f, 0); Assert.IsTrue(session.IsDirty);
            session.Undo(); Assert.IsFalse(session.IsDirty); Assert.AreEqual(savedRevision, session.SavedRevision);
            session.Redo(); Assert.IsTrue(session.IsDirty);
            UnchangedFailure(() => session.MarkSaved(savedRevision), "REVISION_CONFLICT");
        }
        [Test] public void CatalogRelinkPreservesDocumentUndoAndDirtyState()
        {
            var a = Place("chair"); session.MarkSaved(); session.Move(new[] { a }, .6f, 0);
            var before = Json(); var revision = session.Revision; var replacement = Copies.Copy(catalog);
            replacement.items.Single(i => i.id == "chair").available = false;
            Assert.IsTrue(session.PreviewCatalog(replacement).success); Assert.IsTrue(session.Find("chair").available); Assert.AreEqual(0, session.CatalogRevision);
            Assert.IsTrue(session.RefreshCatalog(replacement).success);
            Assert.AreEqual(revision, session.Revision); Assert.AreEqual(1, session.CatalogRevision); Assert.AreEqual(before, Json()); Assert.IsTrue(session.IsDirty);
            Assert.IsFalse(session.Find("chair").available); Assert.IsTrue(session.Undo().success); Assert.IsFalse(session.IsDirty);
            Assert.IsTrue(session.Redo().success); Assert.AreEqual(before, Json());
            replacement.items = replacement.items.Where(i => i.id != "chair").ToArray();
            UnchangedFailure(() => session.RefreshCatalog(replacement), "UNKNOWN_ITEM"); Assert.AreEqual(1, session.CatalogRevision);
        }
        [Test] public void DetachedSnapshotsCannotChangeCommittedStateOrResources()
        {
            catalog.supplements = new[] { new SupplementData { region = "jp", release = "1.73.459696", milestone = "r96", patchVersion = 32, added = 50, sourceCatalog = "https://source.example/catalog", evidence = "evidence/jp.json" } };
            catalog.items[0].parts = new[] { new Part { position = new[] { 1f, 2f, 3f }, materials = new[] { new MaterialSpec { color = new[] { 1f, 1f, 1f, 1f } } } } };
            session = new Session(catalog); var a = Place("chair"); var before = Json();
            catalog.items[0].available = false;
            var snapshot = session.Layout; snapshot.instances[0].position.x = 99; snapshot.room.width = 1; snapshot.instances.Clear();
            var resource = session.Catalog; resource.items[0].available = false; resource.items[0].parts[0].position[0] = 99;
            resource.items[0].parts[0].materials[0].color[0] = 99;
            resource.supplements[0].release = "changed";
            Assert.AreEqual(before, Json()); Assert.IsTrue(session.Find("chair").available);
            Assert.AreEqual(1, session.Find("chair").parts[0].position[0]); Assert.AreEqual(1, session.Find("chair").parts[0].materials[0].color[0]);
            Assert.AreEqual("1.73.459696", session.Catalog.supplements[0].release);
            Assert.IsTrue(session.Delete(new[] { a }).success);
        }
        [Test] public void InvalidSelectionAndEmptyHistoryAreStructuredFailures()
        {
            UnchangedFailure(() => session.Undo(), "NOTHING_TO_UNDO"); UnchangedFailure(() => session.Redo(), "NOTHING_TO_REDO");
            UnchangedFailure(() => session.Move(null, 0, 0), "EMPTY_SELECTION");
            var a = Place("chair"); UnchangedFailure(() => session.Delete(new[] { a, a }), "INVALID_SELECTION");
        }
        [Test] public void StylesRemainSingletonsAndCannotJoinFurnitureCopies()
        {
            var old = Place("floor"); var replacement = session.Place("floor", 0, 0);
            Assert.IsTrue(replacement.success); var floor = replacement.instanceIds.Single(); Assert.AreEqual(1, session.Layout.instances.Count);
            CollectionAssert.AreEquivalent(new[] { old, floor }, replacement.affectedIds);
            UnchangedFailure(() => session.Duplicate(new[] { floor }), "INVALID_ROOM_STYLE");
            UnchangedFailure(() => session.Move(new[] { floor }, .6f, 0), "ROOM_STYLE_FIXED");
        }
        [Test] public void NoChangeCommandsPreserveRevisionSavedStateAndRedo()
        {
            var a = Place("chair"); session.MarkSaved();
            session.Move(new[] { a }, .6f, 0); session.Undo();
            var revision = session.Revision;
            foreach (var result in new[] { session.Move(new[] { a }, .01f, 0), session.Rotate(new[] { a }, 4), session.Replace(session.Layout) })
            {
                Assert.IsTrue(result.success); Assert.AreEqual("NO_CHANGE", result.code); Assert.AreEqual(revision, result.revision);
            }
            Assert.IsFalse(session.IsDirty); Assert.IsTrue(session.CanRedo); Assert.IsTrue(session.Redo().success);
        }
        [Test] public void OpeningAnotherDocumentClearsHistoryWithoutResettingRevision()
        {
            var a = Place("chair"); session.MarkSaved(); session.Move(new[] { a }, .6f, 0);
            var previousRevision = session.Revision; var replacement = new Layout { name = "Cafe B", room = new Room { width = 6, depth = 6 } };
            Assert.IsTrue(session.OpenDocument(replacement, true, previousRevision).success);
            Assert.AreEqual(previousRevision + 1, session.Revision); Assert.AreEqual(session.Revision, session.SavedRevision);
            Assert.IsFalse(session.IsDirty); Assert.IsFalse(session.CanUndo); Assert.IsFalse(session.CanRedo);
            UnchangedFailure(() => session.Undo(), "NOTHING_TO_UNDO");
            UnchangedFailure(() => session.Place("chair", 0, 0, expectedRevision: previousRevision), "REVISION_CONFLICT");
            Assert.IsTrue(session.OpenDocument(session.Layout, false).success);
            Assert.AreEqual(previousRevision + 2, session.Revision); Assert.AreEqual(-1, session.SavedRevision); Assert.IsTrue(session.IsDirty);
            Assert.IsTrue(session.PreviewCatalog(catalog).success); Assert.IsTrue(session.RefreshCatalog(catalog).success);
            session.MarkSaved(); Assert.IsFalse(session.IsDirty);
        }
        [Test] public void FailedDocumentOpenPreservesHistoryAndSavedBaseline()
        {
            var a = Place("chair"); session.MarkSaved(); var baseline = Json(); var savedRevision = session.SavedRevision;
            session.Move(new[] { a }, .6f, 0); session.Move(new[] { a }, .6f, 0); session.Undo();
            var invalid = session.Layout; invalid.instances[0].furnitureId = "unknown";
            UnchangedFailure(() => session.OpenDocument(invalid, true), "UNKNOWN_ITEM");
            Assert.AreEqual(savedRevision, session.SavedRevision); Assert.IsTrue(session.Redo().success); Assert.IsTrue(session.Undo().success);
            Assert.IsTrue(session.Undo().success); Assert.AreEqual(baseline, Json()); Assert.IsFalse(session.IsDirty);
        }
    }
}
