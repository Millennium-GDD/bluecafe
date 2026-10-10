using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace BlueCafe.Unity.Tests
{
    public sealed class RuntimeViewTests
    {
        string directory;
        Pack pack;
        RuntimeView view;
        readonly List<Scene> previewScenes = new List<Scene>();
        const string MeshFile = "fixture.mesh.json";
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "bluecafe-render-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // An explicit test fixture, never a runtime missing-asset substitute.
            File.WriteAllText(Path.Combine(directory, MeshFile), "{\"vertices\":[0,0,0,1,0,0,0,1,0],\"normals\":[0,0,1,0,0,1,0,0,1],\"uv\":[0,0,1,0,0,1],\"submeshes\":[{\"triangles\":[0,1,2]}]}");
            var catalog = new Catalog { items = new[] { new Item {
                id = "fixture", name = "Runtime test fixture", placement = "floor", available = true,
                bounds = new BoundsData { min = new[] { 0f, 2f, 0f }, max = new[] { 1f, 3f, 0f } },
                parts = new[] { new Part {
                    meshJsonPath = MeshFile, position = new[] { 0f, 2f, 0f }, rotation = new[] { 0f, 0f, 0f, 1f }, scale = new[] { 1f, 1f, 1f },
                    materials = new[] { new MaterialSpec { name = "Fixture", color = new[] { 1f, 1f, 1f, 1f }, textureScale = new[] { 1f, 1f }, textureOffset = new[] { 0f, 0f } } }
                } }
            } } };
            File.WriteAllText(Path.Combine(directory, "catalog.json"), JsonUtility.ToJson(catalog));
            pack = Pack.Load(directory);
        }
        [TearDown] public void TearDown()
        {
            try { view?.Dispose(); }
            finally
            {
                view = null;
                foreach (var scene in previewScenes) if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                previewScenes.Clear();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        RuntimeView CreateView(long budget = 512L * 1024 * 1024)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            previewScenes.Add(scene);
            return new RuntimeView(pack, budget, scene);
        }
        Layout Layout(params string[] ids) => new Layout { instances = ids.Select(id => new Instance { instanceId = id, furnitureId = "fixture", position = new Position(0, 0, 0) }).ToList() };
        [Test] public void MoveRetainsObjectAndNativeGeometryWhileDeleteReleasesSharedResources()
        {
            view = CreateView(); var layout = Layout("one", "two"); view.Sync(layout, 1);
            Assert.That(view.Unavailable, Is.Empty);
            var first = Resources.FindObjectsOfTypeAll<GameObject>().Single(g => g.name == "Runtime test fixture [one]");
            var mesh = first.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertices[1], Is.EqualTo(Vector3.right));
            Assert.That(mesh.normals, Is.EqualTo(new[] { Vector3.forward, Vector3.forward, Vector3.forward }));
            Assert.That(mesh.uv[2], Is.EqualTo(Vector2.up));
            Assert.That(view.BoundsFor("one").Value.center.y, Is.EqualTo(2.5f).Within(.0001));
            Assert.That(view.MeshCount, Is.EqualTo(1)); Assert.That(view.MaterialCount, Is.EqualTo(1));
            layout.instances[0].position = new Position(3, 0, 0); view.Sync(layout, 2);
            Assert.That(first.transform.position.x, Is.EqualTo(3));
            Assert.That(first.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            layout.instances.RemoveAt(0); view.Sync(layout, 3);
            Assert.That(view.MeshCount, Is.EqualTo(1));
            layout.instances.Clear(); view.Sync(layout, 4);
            Assert.That(view.MeshCount, Is.Zero); Assert.That(view.ResourceBytes, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => view.Sync(layout, 3));
            Assert.That(view.Revision, Is.EqualTo(4));
        }
        [Test] public void SourceDisappearingAfterValidationIsExplicitlyUnavailable()
        {
            File.Delete(Path.Combine(directory, MeshFile)); view = CreateView(); view.Sync(Layout("gone"), 8);
            Assert.That(view.ObjectCount, Is.Zero); Assert.That(view.Unavailable.Count, Is.EqualTo(1));
            Assert.That(view.Unavailable[0].instanceId, Is.EqualTo("gone"));
            Assert.That(view.Unavailable[0].reason, Is.Not.Empty); Assert.That(view.Revision, Is.EqualTo(8));
            Assert.That(view.ResourceBytes, Is.Zero);
        }
        [Test] public void ResourceBudgetFailureDoesNotLeavePartialGeometry()
        {
            view = CreateView(1); view.Sync(Layout("over-budget"), 1);
            Assert.That(view.ObjectCount, Is.Zero); Assert.That(view.Unavailable.Single().reason, Does.StartWith("RESOURCE_BUDGET:"));
            Assert.That(view.ResourceBytes, Is.Zero);
        }
        [Test] public void AddingAnOverBudgetInstancePreservesOriginalProjectionAndReleasesPartialResources()
        {
            var catalog = pack.Catalog;
            var second = Copies.Copy(catalog.items[0]); second.id = "second"; second.name = "Second runtime fixture";
            second.parts[0].meshJsonPath = "second.mesh.json"; second.parts[0].materials[0].name = "Distinct second material";
            File.Copy(Path.Combine(directory, MeshFile), Path.Combine(directory, "second.mesh.json"));
            catalog.items = new[] { catalog.items[0], second };
            File.WriteAllText(Path.Combine(directory, "catalog.json"), JsonUtility.ToJson(catalog));
            pack = Pack.Load(directory);
            // One complete instance (216 bytes mesh + 1024 material), plus exactly
            // the second mesh. Its material cannot fit, exercising partial cleanup.
            view = CreateView(1456); var layout = Layout("original"); view.Sync(layout, 1);
            var first = Resources.FindObjectsOfTypeAll<GameObject>().Single(g => g.name == "Runtime test fixture [original]");
            long originalBytes = view.ResourceBytes;
            layout.instances.Add(new Instance { instanceId = "addition", furnitureId = "second", position = new Position(2, 0, 0) });
            view.Sync(layout, 2);
            Assert.That(first, Is.Not.Null); Assert.That(first.activeSelf, Is.True);
            Assert.That(first.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(view.ObjectCount, Is.EqualTo(1)); Assert.That(view.MeshCount, Is.EqualTo(1));
            Assert.That(view.ResourceBytes, Is.EqualTo(originalBytes));
            Assert.That(view.Unavailable.Single().instanceId, Is.EqualTo("addition"));
            Assert.That(view.Unavailable.Single().reason, Does.StartWith("RESOURCE_BUDGET:"));
            Assert.That(view.Revision, Is.EqualTo(2));
        }
        GameObject PreviewObject() => view.Camera.transform.parent.Cast<Transform>().Single(t => t.name.EndsWith(" [placement preview]", StringComparison.Ordinal)).gameObject;
        Vector2 ViewportPoint(Vector3 world, Rect viewport)
        {
            var point = view.Camera.WorldToViewportPoint(world);
            return new Vector2(point.x * viewport.width, (1 - point.y) * viewport.height);
        }
        [Test] public void PlacementPreviewReusesDetachedGeometryAndMaterialsWithoutChangingOriginalResources()
        {
            // A real PNG fixture includes packed alpha zero. Opaque native materials
            // must continue ignoring packed alpha when preview opacity is applied.
            var png = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            byte[] sourceBytes;
            try { png.SetPixel(0, 0, new Color(.3f, .6f, .9f, 0)); png.Apply(); sourceBytes = png.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(png); }
            var texturePath = Path.Combine(directory, "fixture.png"); File.WriteAllBytes(texturePath, sourceBytes);
            var catalog = pack.Catalog; catalog.items[0].parts[0].materials[0].texturePath = "fixture.png";
            File.WriteAllText(Path.Combine(directory, "catalog.json"), JsonUtility.ToJson(catalog)); pack = Pack.Load(directory);
            view = CreateView(); var layout = Layout("original"); view.Sync(layout, 5);
            var original = view.Camera.transform.parent.Cast<Transform>().Single(t => t.name == "Runtime test fixture [original]").gameObject;
            var originalMaterial = original.GetComponentInChildren<Renderer>().sharedMaterial;
            var originalMesh = original.GetComponentInChildren<MeshFilter>().sharedMesh;
            var vertices = originalMesh.vertices; var originalColor = originalMaterial.color;
            var texture = originalMaterial.mainTexture; var originalQueue = originalMaterial.renderQueue;
            var baselineBytes = view.ResourceBytes;
            var candidate = new Instance { instanceId = "draft", furnitureId = "fixture", position = new Position(3, 0, 0) };
            view.ShowPlacementPreview(candidate, true);
            var ghost = PreviewObject(); var clone = ghost.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(ghost.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(originalMesh));
            Assert.That(clone, Is.Not.SameAs(originalMaterial)); Assert.That(clone.mainTexture, Is.SameAs(texture));
            Assert.That(clone.GetFloat("_UseAlpha"), Is.Zero);
            Assert.That(clone.GetFloat("_PreviewOpacity"), Is.InRange(.01f, .99f));
            var validTint = clone.color;
            candidate.position = new Position(7, 0, 0);
            Assert.That(view.PlacementPreviewInstance.position.x, Is.EqualTo(3), "Input instance must be copied.");
            var snapshot = view.PlacementPreviewInstance; snapshot.furnitureId = "changed"; snapshot.position = new Position(90, 0, 0);
            Assert.That(view.PlacementPreviewInstance.furnitureId, Is.EqualTo("fixture"));
            Assert.That(view.PlacementPreviewInstance.position.x, Is.EqualTo(3), "Evidence instance must be detached.");
            view.ShowPlacementPreview(candidate, false);
            Assert.That(PreviewObject(), Is.SameAs(ghost)); Assert.That(ghost.GetComponentInChildren<Renderer>().sharedMaterial, Is.SameAs(clone));
            Assert.That(ghost.transform.localPosition.x, Is.EqualTo(7)); Assert.That(view.PlacementPreviewValid, Is.False);
            Assert.That(clone.color, Is.Not.EqualTo(validTint));
            Assert.That(originalMaterial.color, Is.EqualTo(originalColor)); Assert.That(originalMaterial.renderQueue, Is.EqualTo(originalQueue));
            Assert.That(originalMaterial.GetFloat("_PreviewOpacity"), Is.EqualTo(1)); Assert.That(originalMaterial.mainTexture, Is.SameAs(texture));
            Assert.That(originalMesh.vertices, Is.EqualTo(vertices)); Assert.That(File.ReadAllBytes(texturePath), Is.EqualTo(sourceBytes));
            view.ClearPlacementPreview();
            Assert.That(ghost == null, Is.True); Assert.That(clone == null, Is.True);
            Assert.That(originalMesh != null && originalMaterial != null && texture != null, Is.True);
            Assert.That(view.HasPlacementPreview, Is.False); Assert.That(view.PlacementPreviewInstance, Is.Null);
            Assert.That(view.ResourceBytes, Is.EqualTo(baselineBytes));
            view.ShowPlacementPreview(candidate, true); ghost = PreviewObject(); clone = ghost.GetComponentInChildren<Renderer>().sharedMaterial;
            view.Sync(Layout(), 6);
            Assert.That(ghost == null && clone == null, Is.True); Assert.That(view.HasPlacementPreview, Is.False);
            Assert.That(view.ResourceBytes, Is.Zero, "Sync must release both ghost-only and removed committed resources.");
            view.ShowPlacementPreview(candidate, true); ghost = PreviewObject(); clone = ghost.GetComponentInChildren<Renderer>().sharedMaterial;
            view.Dispose(); Assert.That(ghost == null && clone == null, Is.True); view = null;
        }
        [Test] public void PlacementPreviewDoesNotAffectCommittedPickingRevisionOrFraming()
        {
            view = CreateView(); view.Sync(Layout("original"), 12);
            var viewport = new Rect(0, 0, 640, 480);
            var originalCenter = view.BoundsFor("original").Value.center;
            var pickPoint = ViewportPoint(originalCenter, viewport);
            Assert.That(view.Pick(pickPoint, viewport), Is.EqualTo("original"));
            // A ghost directly in front of the committed item must not intercept its ray.
            var offset = -view.Camera.transform.forward * 3;
            var candidate = new Instance { instanceId = "draft", furnitureId = "fixture", position = new Position(offset.x, offset.y, offset.z) };
            view.ShowPlacementPreview(candidate, true);
            Assert.That(view.Pick(pickPoint, viewport), Is.EqualTo("original"));
            var cameraPosition = view.Camera.transform.position; var cameraRotation = view.Camera.transform.rotation; var cameraSize = view.Camera.orthographicSize;
            candidate.position = new Position(1000, 0, 1000); view.ShowPlacementPreview(candidate, false);
            var ghostCenter = PreviewObject().GetComponentInChildren<Renderer>().bounds.center;
            Assert.That(view.Pick(ViewportPoint(ghostCenter, viewport), viewport), Is.Null);
            view.FrameRoom();
            Assert.That(view.Camera.transform.position, Is.EqualTo(cameraPosition)); Assert.That(view.Camera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(view.Camera.orthographicSize, Is.EqualTo(cameraSize));
            Assert.That(view.ObjectCount, Is.EqualTo(1)); Assert.That(view.Revision, Is.EqualTo(12));
            Assert.That(view.BoundsFor("draft"), Is.Null); Assert.That(view.Unavailable, Is.Empty);
        }
        [Test] public void PreviewFailureClearsTransientStateAndPreservesCommittedProjection()
        {
            var catalog = pack.Catalog;
            var second = Copies.Copy(catalog.items[0]); second.id = "second"; second.parts[0].meshJsonPath = "second.mesh.json"; second.parts[0].materials[0].name = "Second material";
            var missing = Copies.Copy(second); missing.id = "missing"; missing.available = false;
            File.Copy(Path.Combine(directory, MeshFile), Path.Combine(directory, "second.mesh.json"));
            catalog.items = new[] { catalog.items[0], second, missing };
            File.WriteAllText(Path.Combine(directory, "catalog.json"), JsonUtility.ToJson(catalog)); pack = Pack.Load(directory);
            view = CreateView(1456); view.Sync(Layout("original"), 9);
            var baselineBytes = view.ResourceBytes; var bounds = view.BoundsFor("original");
            var candidate = new Instance { instanceId = "draft", furnitureId = "fixture" };
            view.ShowPlacementPreview(candidate, true);
            candidate.position = new Position(float.NaN, 0, 0);
            Assert.Throws<ArgumentException>(() => view.ShowPlacementPreview(candidate, false));
            Assert.That(view.HasPlacementPreview, Is.False); Assert.That(view.PlacementPreviewInstance, Is.Null);
            candidate.position = new Position(); candidate.furnitureId = "missing";
            Assert.That(Assert.Throws<InvalidDataException>(() => view.ShowPlacementPreview(candidate, true)).Message, Does.StartWith("PREVIEW_UNAVAILABLE:"));
            // The second mesh fits, then material allocation fails: partial assembly
            // cleanup must not evict the committed item's shared resources.
            candidate.furnitureId = "second";
            Assert.That(Assert.Throws<InvalidDataException>(() => view.ShowPlacementPreview(candidate, true)).Message, Does.StartWith("RESOURCE_BUDGET:"));
            Assert.That(view.HasPlacementPreview, Is.False); Assert.That(view.PlacementPreviewInstance, Is.Null);
            Assert.That(view.ObjectCount, Is.EqualTo(1)); Assert.That(view.Revision, Is.EqualTo(9));
            Assert.That(view.BoundsFor("original"), Is.EqualTo(bounds)); Assert.That(view.Unavailable, Is.Empty);
            Assert.That(view.ResourceBytes, Is.EqualTo(baselineBytes)); Assert.That(view.MeshCount, Is.EqualTo(1));
        }
        [Test] public void WallPreviewUsesTheSameNativePivotAndBoundsAsCommittedFurniture()
        {
            var catalog = pack.Catalog; catalog.items[0].placement = "wall";
            File.WriteAllText(Path.Combine(directory, "catalog.json"), JsonUtility.ToJson(catalog)); pack = Pack.Load(directory);
            var layout = Layout("wall"); layout.instances[0].position = new Position(3, 4, 5); layout.instances[0].quarterTurns = 1;
            view = CreateView(); view.Sync(layout, 1);
            var candidate = Copies.Copy(layout.instances[0]); candidate.instanceId = "draft";
            view.ShowPlacementPreview(candidate, true);
            var ghost = PreviewObject(); var anchor = ghost.transform.Find("Native pivot offset");
            Assert.That(anchor, Is.Not.Null); Assert.That(anchor.localPosition, Is.EqualTo(view.NativePivotOffsetFor("wall")));
            Assert.That(anchor.localPosition.y, Is.EqualTo(-2.5f));
            var ghostBounds = ghost.GetComponentInChildren<Renderer>().bounds; var placedBounds = view.BoundsFor("wall").Value;
            Assert.That(Vector3.Distance(ghostBounds.center, placedBounds.center), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(ghostBounds.size, placedBounds.size), Is.LessThan(.0001f));
        }
        [Test] public void AuditedRulesMatchEditorForEverySuppliedMaterial()
        {
            string catalogPath = Path.Combine(Application.dataPath, "BlueCafe/Content/catalog.json");
            if (!File.Exists(catalogPath)) Assert.Ignore("Original local pack is not installed.");
            string json = File.ReadAllText(catalogPath);
            var runtimeCatalog = JsonUtility.FromJson<Catalog>(json);
            var editorCatalog = JsonUtility.FromJson<CafeCatalog>(json);
            var shader = Shader.Find("BlueCafe/BakedTexture"); Assert.That(shader, Is.Not.Null);
            var properties = new[] { "_SrcBlend", "_DstBlend", "_ZWrite", "_ZTest", "_StencilComp", "_StencilPass", "_StencilRef", "_Cull" };
            int audited = 0;
            for (int i = 0; i < runtimeCatalog.items.Length; i++)
            {
                var item = runtimeCatalog.items[i]; var legacy = editorCatalog.items[i];
                Assert.That(RuntimeSurfaceRules.IsAuditedSurface(item), Is.EqualTo(CafeWindowRendering.IsAuditedSurface(legacy)), item.id);
                if (RuntimeSurfaceRules.IsAuditedSurface(item)) audited++;
                if (!RuntimeSurfaceRules.IsAuditedSurface(item) && item.placement != "wallpaper") continue;
                for (int p = 0; p < (item.parts?.Length ?? 0); p++)
                {
                    Assert.That(RuntimeSurfaceRules.IsFramingHelper(item, item.parts[p]), Is.EqualTo(CafeWindowRendering.IsFramingHelper(legacy, legacy.parts[p])), item.id);
                    for (int m = 0; m < (item.parts[p].materials?.Length ?? 0); m++)
                    {
                        var a = new Material(shader); var b = new Material(shader);
                        try
                        {
                            RuntimeSurfaceRules.Apply(item, item.parts[p].materials[m], a);
                            CafeWindowRendering.Apply(legacy, legacy.parts[p].materials[m], b);
                            Assert.That(a.renderQueue, Is.EqualTo(b.renderQueue), item.id);
                            foreach (string property in properties) Assert.That(a.GetInt(property), Is.EqualTo(b.GetInt(property)), item.id + " " + property);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
                    }
                }
            }
            Assert.That(audited, Is.EqualTo(10), "All ten audited native surface identities remain present.");
        }
    }
}
