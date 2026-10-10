using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueCafe.Core;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Infrastructure.Tests
{
    public class PackTests
    {
        string root;
        const string MeshJson = "{\"vertices\":[0,0,0,1,0,0,0,1,0],\"normals\":[],\"uv\":[],\"submeshes\":[{\"triangles\":[0,1,2]}]}";
        static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "bluecafe-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "mesh.json"), MeshJson);
            File.WriteAllBytes(Path.Combine(root, "image.png"), Png);
            WriteCatalog();
        }
        [TearDown] public void Teardown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        static Item Item(string id = "1001") => new Item
        {
            id = id, name = "Original furniture", placement = "floor", width = 1, depth = 1, height = 1, available = true, thumbnailPath = "image.png", bounds = new BoundsData { min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 1f, 1f } },
            parts = new[] { new Part { meshJsonPath = "mesh.json", position = new[] { 0f, 0f, 0f }, rotation = new[] { 0f, 0f, 0f, 1f }, scale = new[] { 1f, 1f, 1f },
                materials = new[] { new MaterialSpec { texturePath = "image.png", color = new[] { 1f, 1f, 1f, 1f }, textureScale = new[] { 1f, 1f }, textureOffset = new[] { 0f, 0f } } } } }
        };
        void WriteCatalog(params Item[] items)
        {
            if (items.Length == 0) items = new[] { Item() };
            File.WriteAllText(Path.Combine(root, "catalog.json"), JsonUtility.ToJson(new Catalog { items = items }));
        }
        [Test] public void LegacyPathsRelocateInMemoryWithoutChangingSourceBytes()
        {
            var item = Item(); item.thumbnailPath = Pack.LegacyPrefix + "image.png"; item.parts[0].meshJsonPath = Pack.LegacyPrefix + "mesh.json"; item.parts[0].materials[0].texturePath = Pack.LegacyPrefix + "image.png";
            WriteCatalog(item); var original = File.ReadAllBytes(Path.Combine(root, "catalog.json"));
            var pack = Pack.Load(root);
            Assert.IsTrue(pack.Availability(item.id).Available); Assert.IsTrue(pack.Availability(item.id).ThumbnailAvailable);
            Assert.AreEqual("mesh.json", pack.Catalog.items[0].parts[0].meshJsonPath);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(root, "catalog.json")));
            Assert.AreEqual(pack.Fingerprint, Pack.Load(root).Fingerprint);
        }
        [TestCase("../outside.mesh.json")]
        [TestCase("Meshes/../../outside.mesh.json")]
        [TestCase("/tmp/outside.mesh.json")]
        [TestCase("C:/outside.mesh.json")]
        [TestCase("Meshes\\outside.mesh.json")]
        [TestCase("Meshes//outside.mesh.json")]
        public void UnsafePathRejectsWholeCandidate(string path)
        {
            var item = Item(); item.parts[0].meshJsonPath = path; WriteCatalog(item);
            var error = Assert.Throws<PackException>(() => Pack.Load(root)); Assert.AreEqual("UNSAFE_PACK_PATH", error.Code);
        }
        [Test] public void DuplicateIdsAndUnknownVersionRejectAdoption()
        {
            WriteCatalog(Item(), Item()); Assert.Throws<PackException>(() => Pack.Load(root));
            WriteCatalog(); string path = Path.Combine(root, "catalog.json"); File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\":1", "\"version\":2"));
            Assert.AreEqual("UNSUPPORTED_CATALOG_VERSION", Assert.Throws<PackException>(() => Pack.Load(root)).Code);
        }
        [Test] public void MissingThumbnailDoesNotDisableGeometry()
        {
            var item = Item(); item.thumbnailPath = "missing-thumbnail.png"; WriteCatalog(item);
            var availability = Pack.Load(root).Availability(item.id);
            Assert.IsTrue(availability.Available); Assert.IsFalse(availability.ThumbnailAvailable);
            StringAssert.Contains("missing-thumbnail.png", availability.ThumbnailReason);
        }
        [Test] public void MissingRequiredTextureDisablesEntireItem()
        {
            var item = Item(); item.parts[0].materials[0].texturePath = "missing-texture.png"; WriteCatalog(item);
            var pack = Pack.Load(root); Assert.IsFalse(pack.Catalog.items[0].available);
            StringAssert.Contains("missing-texture.png", pack.Availability(item.id).Reason); Assert.IsTrue(pack.Availability(item.id).ThumbnailAvailable);
        }
        [TestCase("{\"vertices\":[0,0,0],\"submeshes\":[{\"triangles\":[0,1,2]}]}")]
        [TestCase("{\"vertices\":[0,0,0],\"submeshes\":[{\"triangles\":[0,0.2,0]}]}")]
        [TestCase("{\"vertices\":[0,NaN,0],\"submeshes\":[{\"triangles\":[0,0,0]}]}")]
        public void CorruptMeshDisablesItemWithoutHidingItsRecord(string json)
        {
            File.WriteAllText(Path.Combine(root, "mesh.json"), json); var pack = Pack.Load(root);
            Assert.AreEqual(1, pack.Catalog.items.Length); Assert.IsFalse(pack.Availability("1001").Available); StringAssert.Contains("mesh.json", pack.Availability("1001").Reason);
        }
        [Test] public void CorruptPngAndChangedSourceInvalidateAvailabilityAndFingerprint()
        {
            var valid = Pack.Load(root); var broken = (byte[])Png.Clone(); broken[45] ^= 1;
            File.WriteAllBytes(Path.Combine(root, "image.png"), broken); var rescan = Pack.Load(root);
            Assert.IsFalse(rescan.Availability("1001").Available); Assert.IsFalse(rescan.Availability("1001").ThumbnailAvailable);
            Assert.AreNotEqual(valid.Fingerprint, rescan.Fingerprint);
        }
        [Test] public async Task CancelledBackgroundLoadDoesNotPublishCandidate()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await Pack.LoadAsync(root, cancellation.Token); Assert.Fail("Cancelled load published a pack."); }
                catch (OperationCanceledException) { }
            }
        }
        [Test] public void SymbolicLinkToOutsidePackRejectsCandidate()
        {
            if (!File.Exists("/bin/ln")) Assert.Ignore("This symlink failure exercise requires Unix ln.");
            string outside = Path.Combine(Path.GetTempPath(), "bluecafe-outside-" + Guid.NewGuid().ToString("N") + ".json");
            string link = Path.Combine(root, "linked-mesh.json");
            try
            {
                File.WriteAllText(outside, MeshJson);
                var start = new System.Diagnostics.ProcessStartInfo { FileName = "/bin/ln", Arguments = "-s \"" + outside.Replace("\"", "\\\"") + "\" \"" + link.Replace("\"", "\\\"") + "\"", UseShellExecute = false };
                using (var process = System.Diagnostics.Process.Start(start)) { process.WaitForExit(); if (process.ExitCode != 0) Assert.Ignore("Could not create a symlink on this host."); }
                var item = Item(); item.parts[0].meshJsonPath = "linked-mesh.json"; WriteCatalog(item);
                Assert.AreEqual("UNSAFE_PACK_PATH", Assert.Throws<PackException>(() => Pack.Load(root)).Code);
            }
            finally { if (File.Exists(link)) File.Delete(link); if (File.Exists(outside)) File.Delete(outside); }
        }
        [Test] public void SourceMetadataArraysDoNotAcquireFurnitureRecordRequirements()
        {
            string path = Path.Combine(root, "catalog.json");
            string json = File.ReadAllText(path).Replace("\"parts\":", "\"sourceColliders\":[{\"id\":17,\"name\":\"Native collider\"}],\"templates\":[{\"id\":123}],\"parts\":");
            File.WriteAllText(path, json); Assert.IsTrue(Pack.Load(root).Availability("1001").Available);
        }
        [Test] public void DuplicateJsonKeysAndMissingStructureAreRejected()
        {
            File.WriteAllText(Path.Combine(root, "catalog.json"), "{\"version\":1,\"version\":1,\"gridUnit\":0.6,\"items\":[]}"); Assert.Throws<PackException>(() => Pack.Load(root));
            File.WriteAllText(Path.Combine(root, "catalog.json"), "{\"version\":1,\"gridUnit\":0.6}"); Assert.Throws<PackException>(() => Pack.Load(root));
        }
    }
}
