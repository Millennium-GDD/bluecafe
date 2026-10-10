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
        [Test] public void SettingsUseAppDataWithoutEnteringPortableLayouts()
        {
            Assert.AreEqual("", store.LoadSettings().packRoot); store.SaveSettings(new Settings { packRoot = "/local/prepared-pack" });
            Assert.AreEqual("/local/prepared-pack", new DocumentStore(root).LoadSettings().packRoot); StringAssert.DoesNotContain("prepared-pack", DocumentStore.SerializeLayout(Layout()));
        }
    }
}
