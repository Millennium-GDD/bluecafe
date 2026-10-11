using System;
using System.IO;
using BlueCafe.Core;
using NUnit.Framework;

namespace BlueCafe.Infrastructure.Tests
{
    public class DocumentStoreTests
    {
        string root;
        DocumentStore store;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "bluecafe-store-" + Guid.NewGuid().ToString("N")); store = new DocumentStore(root); }
        [TearDown] public void Teardown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        static Layout Layout(string name = "My original cafe") => new Layout
        {
            name = name,
            instances = new System.Collections.Generic.List<Instance> { new Instance { instanceId = "original-instance-id", furnitureId = "1095", position = new Position(1.2f, 0, -1.8f), quarterTurns = 3 } }
        };
        [Test] public void PortableRoundTripPreservesOriginalIdentityAndExactTransforms()
        {
            var original = Layout(); string path = Path.Combine(root, "export.json");
            DocumentStore.SaveLayout(path, original); var reloaded = DocumentStore.LoadLayout(path);
            Assert.IsTrue(Copies.Equal(original, reloaded));
            string json = File.ReadAllText(path); StringAssert.DoesNotContain("cafeId", json); StringAssert.DoesNotContain("packRoot", json); StringAssert.DoesNotContain("revision", json);
        }
        [TestCase("{\"version\":2,\"room\":{\"width\":18,\"depth\":18,\"wallHeight\":4.8,\"grid\":0.6},\"instances\":[]}")]
        [TestCase("{\"version\":1,\"room\":{},\"instances\":[]}")]
        [TestCase("{\"version\":1,\"room\":{\"width\":NaN,\"depth\":18,\"wallHeight\":4.8,\"grid\":0.6},\"instances\":[]}")]
        [TestCase("{\"version\":1,\"room\":{\"width\":1e999,\"depth\":18,\"wallHeight\":4.8,\"grid\":0.6},\"instances\":[]}")]
        [TestCase("{\"version\":1,\"version\":1,\"room\":{\"width\":18,\"depth\":18,\"wallHeight\":4.8,\"grid\":0.6},\"instances\":[]}")]
        public void InvalidImportRejectsBeforeAnyStoreChange(string json)
        {
            Assert.Throws<DocumentException>(() => DocumentStore.ParseLayout(json)); Assert.IsFalse(Directory.Exists(root));
        }
        [Test] public void MissingPositionAndFractionalRotationCannotBecomeDefaultTransforms()
        {
            string json = DocumentStore.SerializeLayout(Layout());
            Assert.Throws<DocumentException>(() => DocumentStore.ParseLayout(json.Replace("\"quarterTurns\": 3", "\"quarterTurns\": 0.5")));
            Assert.Throws<DocumentException>(() => DocumentStore.ParseLayout(json.Replace("\"x\": 1.2", "\"missingX\": 1.2")));
        }
        [Test] public void SavedCafeIdentityUpdatesIndependentlyFromDesignIds()
        {
            var initial = Layout(); var saved = store.SaveCafe(initial); initial.name = "Renamed cafe";
            var updated = store.SaveCafe(initial, saved.cafeId);
            Assert.AreEqual(saved.cafeId, updated.cafeId); Assert.AreEqual(1, store.ListCafes().Length);
            Assert.IsTrue(Copies.Equal(initial, store.LoadCafe(saved.cafeId))); Assert.AreEqual("original-instance-id", updated.layout.instances[0].instanceId);
        }
        [Test] public void RenamePreservesSavedIdentityAndTransformsAcrossReopen()
        {
            var original = Layout(); var saved = store.SaveCafe(original);
            var renamed = store.RenameCafe(saved.cafeId, "  New cafe name  ");
            original.name = "New cafe name";
            Assert.AreEqual(saved.cafeId, renamed.cafeId);
            Assert.AreEqual(1, store.ListCafes().Length);
            Assert.IsTrue(Copies.Equal(original, new DocumentStore(root).LoadCafe(saved.cafeId)));
        }
        [Test] public void RejectedRenameKeepsBothSavedRecordsByteExact()
        {
            var first = store.SaveCafe(Layout("First")); var second = store.SaveCafe(Layout("Second"));
            string path = Path.Combine(root, "cafes", first.cafeId + ".json");
            string other = Path.Combine(root, "cafes", second.cafeId + ".json");
            byte[] prior = File.ReadAllBytes(path), otherPrior = File.ReadAllBytes(other);
            Assert.AreEqual("NAME_COLLISION", Assert.Throws<DocumentException>(() => store.RenameCafe(first.cafeId, "second")).Code);
            Assert.AreEqual("NAME_REQUIRED", Assert.Throws<DocumentException>(() => store.RenameCafe(first.cafeId, " ")).Code);
            CollectionAssert.AreEqual(prior, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(otherPrior, File.ReadAllBytes(other));
        }
        [Test] public void ExtensionMetadataDoesNotAcquireInstanceRequirements()
        {
            string json = DocumentStore.SerializeLayout(Layout()).Replace("\"quarterTurns\": 3", "\"quarterTurns\": 3, \"observations\": [{\"position\": 7, \"instanceId\": 123, \"quarterTurns\": 0.5}]");
            Assert.IsTrue(Copies.Equal(Layout(), DocumentStore.ParseLayout(json)));
        }
        [Test] public void NameCollisionCannotOverwriteDifferentSavedCafe()
        {
            var saved = store.SaveCafe(Layout());
            var error = Assert.Throws<DocumentException>(() => store.SaveCafe(Layout())); Assert.AreEqual("NAME_COLLISION", error.Code);
            Assert.AreEqual(saved.cafeId, store.ListCafes()[0].cafeId); Assert.AreEqual(1, store.ListCafes().Length);
        }
        [Test] public void FailedWriteLeavesExistingPortableDocumentIntact()
        {
            string path = Path.Combine(root, "export.json"); DocumentStore.SaveLayout(path, Layout()); byte[] prior = File.ReadAllBytes(path);
            var invalid = Layout(); invalid.instances[0].position = new Position(float.NaN, 0, 0);
            Assert.Throws<DocumentException>(() => DocumentStore.SaveLayout(path, invalid)); CollectionAssert.AreEqual(prior, File.ReadAllBytes(path));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        [Test] public void RecoveryRejectsOlderRevisionsAndNeverUpdatesNamedCafe()
        {
            var saved = store.SaveCafe(Layout()); string documentId = Guid.NewGuid().ToString("N"); var edited = Layout("Unsaved change");
            Assert.IsTrue(store.WriteRecovery(edited, documentId, 10, saved.cafeId)); Assert.IsFalse(store.WriteRecovery(Layout(), documentId, 9, saved.cafeId));
            Assert.AreEqual(10, store.LoadRecovery().revision); Assert.AreEqual("Unsaved change", store.LoadRecovery().layout.name);
            Assert.AreEqual("My original cafe", store.LoadCafe(saved.cafeId).name);
        }
        [Test] public void CorruptLatestRecoveryFallsBackToPreviousCompleteSnapshot()
        {
            string id = Guid.NewGuid().ToString("N"); store.WriteRecovery(Layout("Previous"), id, 1); store.WriteRecovery(Layout("Latest"), id, 2);
            File.WriteAllText(Path.Combine(root, "automatic-recovery.json"), "{truncated");
            Assert.AreEqual(1, new DocumentStore(root).LoadRecovery().revision); Assert.AreEqual("Previous", new DocumentStore(root).LoadRecovery().layout.name);
        }
        [Test] public void DiscardClearsBothSnapshotsAndBlocksPendingWritesAcrossRestart()
        {
            string id = Guid.NewGuid().ToString("N"); store.WriteRecovery(Layout(), id, 1); store.WriteRecovery(Layout("Later"), id, 2); store.ClearRecovery(id);
            Assert.IsFalse(store.WriteRecovery(Layout(), id, 3)); Assert.IsNull(new DocumentStore(root).LoadRecovery());
        }
        [Test] public void SaveClearsOlderRecoveryButPermitsNewerEdits()
        {
            string id = Guid.NewGuid().ToString("N"); store.WriteRecovery(Layout(), id, 4); store.ClearRecovery(id, 4);
            Assert.IsFalse(store.WriteRecovery(Layout(), id, 4)); Assert.IsTrue(store.WriteRecovery(Layout("New edit"), id, 5));
        }
        [Test] public void DiscardedPreviousDocumentCannotReturnThroughFallback()
        {
            string oldId = Guid.NewGuid().ToString("N"), newId = Guid.NewGuid().ToString("N");
            store.WriteRecovery(Layout("Old work"), oldId, 1); store.WriteRecovery(Layout("Current work"), newId, 1); store.ClearRecovery(oldId);
            Assert.AreEqual(newId, store.LoadRecovery().documentId);
            File.WriteAllText(Path.Combine(root, "automatic-recovery.json"), "{truncated");
            Assert.IsNull(new DocumentStore(root).LoadRecovery());
        }
        static Layout EmptyLayout() => new Layout { name = "Unsaved initial", instances = new System.Collections.Generic.List<Instance>() };
        [Test] public void FirstStartupCreatesOneSavedEmptyDefaultWithoutMutatingInputOrPackPath()
        {
            store.SaveSettings(new Settings { packRoot = "/local/prepared-pack" });
            var empty = EmptyLayout(); var record = store.EnsureDefaultCafe(empty);
            Assert.AreEqual("Unsaved initial", empty.name);
            Assert.AreEqual("Empty room", record.name); Assert.AreEqual(0, record.layout.instances.Count);
            var settings = store.LoadSettings();
            Assert.AreEqual(record.cafeId, settings.defaultCafeId); Assert.AreEqual(record.cafeId, settings.emptyCafeId);
            Assert.AreEqual("/local/prepared-pack", settings.packRoot);
            Assert.AreEqual(1, store.ListCafes().Length);
        }
        [Test] public void ReopeningDefaultPreservesIdentityAndSavedChangesWithoutWritingAgain()
        {
            var first = store.EnsureDefaultCafe(EmptyLayout());
            var edited = Layout("My starter edited"); store.SaveCafe(edited, first.cafeId);
            string settingsPath = Path.Combine(root, "settings.json"), cafePath = Path.Combine(root, "cafes", first.cafeId + ".json");
            byte[] settings = File.ReadAllBytes(settingsPath), cafeBytes = File.ReadAllBytes(cafePath);
            var reopened = new DocumentStore(root).EnsureDefaultCafe(EmptyLayout());
            Assert.AreEqual(first.cafeId, reopened.cafeId); Assert.IsTrue(Copies.Equal(edited, reopened.layout));
            CollectionAssert.AreEqual(settings, File.ReadAllBytes(settingsPath)); CollectionAssert.AreEqual(cafeBytes, File.ReadAllBytes(cafePath));
            Assert.AreEqual(1, store.ListCafes().Length);
        }
        [Test] public void ChosenSavedCafeIsTheDefaultAndMissingChoiceFallsBackToTrackedStarter()
        {
            var starter = store.EnsureDefaultCafe(EmptyLayout()); var chosen = store.SaveCafe(Layout("Chosen cafe"));
            var settings = store.LoadSettings(); settings.packRoot = "/custom/pack"; settings.defaultCafeId = chosen.cafeId; store.SaveSettings(settings);
            Assert.AreEqual(chosen.cafeId, new DocumentStore(root).EnsureDefaultCafe(EmptyLayout()).cafeId);
            Assert.AreEqual(2, store.ListCafes().Length);
            store.RenameCafe(starter.cafeId, "Starter renamed"); store.DeleteCafe(chosen.cafeId);
            var fallback = new DocumentStore(root).EnsureDefaultCafe(EmptyLayout());
            Assert.AreEqual(starter.cafeId, fallback.cafeId); Assert.AreEqual("Starter renamed", fallback.name);
            Assert.AreEqual(starter.cafeId, store.LoadSettings().defaultCafeId); Assert.AreEqual("/custom/pack", store.LoadSettings().packRoot);
            Assert.AreEqual(1, store.ListCafes().Length);
        }
        [Test] public void DefaultCreationCannotOverwriteAnExistingCafeNamedEmptyRoom()
        {
            var existing = store.SaveCafe(Layout("Empty room"));
            string path = Path.Combine(root, "cafes", existing.cafeId + ".json"); byte[] before = File.ReadAllBytes(path);
            var starter = store.EnsureDefaultCafe(EmptyLayout());
            Assert.AreNotEqual(existing.cafeId, starter.cafeId); Assert.AreEqual("Empty room (2)", starter.name);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path)); Assert.AreEqual(2, store.ListCafes().Length);
            Assert.AreEqual(starter.cafeId, new DocumentStore(root).EnsureDefaultCafe(EmptyLayout()).cafeId);
        }
        [Test] public void OldVersionOneSettingsGainStartupIdentityAndKeepPackPath()
        {
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "settings.json"), "{\"version\":1,\"packRoot\":\"/old/prepared-pack\"}");
            Assert.IsTrue(string.IsNullOrEmpty(store.LoadSettings().defaultCafeId));
            var starter = store.EnsureDefaultCafe(EmptyLayout()); var settings = new DocumentStore(root).LoadSettings();
            Assert.AreEqual(1, settings.version); Assert.AreEqual("/old/prepared-pack", settings.packRoot);
            Assert.AreEqual(starter.cafeId, settings.defaultCafeId); Assert.AreEqual(starter.cafeId, settings.emptyCafeId);
        }
        [Test] public void DeletedStarterCreatesNewIdentityAndRepairsDefault()
        {
            var first = store.EnsureDefaultCafe(EmptyLayout()); store.DeleteCafe(first.cafeId);
            var replacement = new DocumentStore(root).EnsureDefaultCafe(EmptyLayout());
            Assert.AreNotEqual(first.cafeId, replacement.cafeId); Assert.AreEqual("Empty room", replacement.name);
            Assert.AreEqual(replacement.cafeId, store.LoadSettings().defaultCafeId);
            Assert.AreEqual(replacement.cafeId, store.LoadSettings().emptyCafeId);
            Assert.AreEqual(1, store.ListCafes().Length);
        }
        [Test] public void StarterPreservesOriginalFloorAndWallStylesWithoutAddingFurniture()
        {
            var original = EmptyLayout();
            original.instances.Add(new Instance { instanceId = "starter-floor", furnitureId = "1", position = new Position(0, 0, 0) });
            original.instances.Add(new Instance { instanceId = "starter-wall", furnitureId = "2", position = new Position(0, 0, 0) });
            var record = store.EnsureDefaultCafe(original);
            Assert.AreEqual("Empty room", record.name); Assert.AreEqual(2, record.layout.instances.Count);
            var expected = Copies.Copy(original); expected.name = "Empty room";
            Assert.IsTrue(Copies.Equal(expected, record.layout));
            Assert.IsTrue(Copies.Equal(expected, new DocumentStore(root).EnsureDefaultCafe(EmptyLayout()).layout));
            Assert.AreEqual("Unsaved initial", original.name);
        }
        [Test] public void CorruptSelectedDefaultIsReportedWithoutReplacingItOrChangingSettings()
        {
            var starter = store.EnsureDefaultCafe(EmptyLayout()); var chosen = store.SaveCafe(Layout("Chosen cafe"));
            var settings = store.LoadSettings(); settings.defaultCafeId = chosen.cafeId; store.SaveSettings(settings);
            string settingsPath = Path.Combine(root, "settings.json"), chosenPath = Path.Combine(root, "cafes", chosen.cafeId + ".json");
            File.WriteAllText(chosenPath, "{truncated"); byte[] before = File.ReadAllBytes(settingsPath), corrupt = File.ReadAllBytes(chosenPath);
            Assert.Throws<FormatException>(() => new DocumentStore(root).EnsureDefaultCafe(EmptyLayout()));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(settingsPath)); CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(chosenPath));
            Assert.AreEqual(2, Directory.GetFiles(Path.Combine(root, "cafes"), "*.json").Length);
            Assert.AreEqual(starter.cafeId, store.LoadSettings().emptyCafeId);
        }
        [Test] public void InvalidStarterCannotWriteStartupSettingsOrSavedCafe()
        {
            Assert.AreEqual("EMPTY_LAYOUT_REQUIRED", Assert.Throws<DocumentException>(() => store.EnsureDefaultCafe(Layout())).Code);
            Assert.IsFalse(Directory.Exists(root));
        }
        [Test] public void SettingsUseAppDataWithoutEnteringPortableLayouts()
        {
            Assert.AreEqual("", store.LoadSettings().packRoot); store.SaveSettings(new Settings { packRoot = "/local/prepared-pack" });
            Assert.AreEqual("/local/prepared-pack", new DocumentStore(root).LoadSettings().packRoot); StringAssert.DoesNotContain("prepared-pack", DocumentStore.SerializeLayout(Layout()));
        }
    }
}
