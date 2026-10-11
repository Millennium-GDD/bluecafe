#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BlueCafe.Agent;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        // Development evidence only. Public UI primitives drive browsing; this probe
        // never changes the document, history, camera or selection directly.
        Response ProbeBrowsing(Arguments a)
        {
            Response Reply(object value) => new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(value) };
            if (a.action == "browse-state")
            {
                var list = root.Q<ListView>("assetsList"); var gallery = root.Q<ScrollView>(className: "preset-gallery-scroll");
                var assetScroll = list?.Q<ScrollView>(); var camera = View.Camera;
                var fields = new[] { "assetsSearch", "assetsCategory", "assetsSubcategory", "assetsTheme", "assetsClear" };
                return Reply(new BrowsingState {
                    screen = screen, screenWidth = Screen.width, screenHeight = Screen.height, panelWidth = root.worldBound.width, panelHeight = root.worldBound.height, graphicsDevice = SystemInfo.graphicsDeviceName, processor = SystemInfo.processorType, memoryMB = SystemInfo.systemMemorySize, appData = appData, packFingerprint = Pack.Fingerprint, packRoot = Pack.Root, clipboard = GUIUtility.systemCopyBuffer,
                    build = buildBrowsing, assets = assetsBrowsing, buildQuery = search?.value, buildCategory = topCategory, buildSubcategory = category?.value, buildTheme = theme?.value, buildInCafe = inCafe?.value ?? false, buildOffset = root.Q<ScrollView>(className: "build-card-scroll")?.scrollOffset.y ?? 0, selectedInstances = selection.OrderBy(id => id).ToArray(),
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation, cameraSize = camera.orthographicSize, cameraAspect = camera.aspect, documentHash = HashBytes(System.Text.Encoding.UTF8.GetBytes(DocumentStore.SerializeLayout(Session.Layout))),
                    assetIds = list?.itemsSource?.Cast<Item>().Select(i => i.id).ToArray() ?? Array.Empty<string>(),
                    boundAssetIds = list == null ? Array.Empty<string>() : list.Query<VisualElement>("asset-recycled-row").ToList().Where(e => e.userData is Item && e.worldBound.Overlaps(assetScroll.contentViewport.worldBound)).Select(e => ((Item)e.userData).id).ToArray(),
                    assetOffset = assetScroll?.scrollOffset.y ?? 0, assetScrollMaximum = assetScroll?.verticalScroller.highValue ?? 0,
                    presetOffset = gallery?.scrollOffset.y ?? 0, presetScrollMaximum = gallery?.verticalScroller.highValue ?? 0,
                    presetCards = gallery == null ? Array.Empty<PreviewEvidence>() : gallery.Query<VisualElement>(className: "preset-card").ToList().Select(card => {
                        var image = card.Q<Image>(); var texture = image?.image as Texture2D;
                        return new PreviewEvidence { key = card.name, ready = texture != null, visible = card.worldBound.Overlaps(gallery.contentViewport.worldBound), hash = texture == null ? null : HashBytes(texture.GetRawTextureData()), bounds = card.worldBound, error = image?.ClassListContains("preview-failed") ?? false };
                    }).ToArray(),
                    controls = fields.Select(name => { var element = root.Q<VisualElement>(name); return new BrowseControl { name = name, bounds = element?.worldBound ?? Rect.zero }; }).ToArray(),
                    operationStatus = operationStatus, busy = busy, pathError = root.Q<Label>(className: "settings-path-error")?.text, dialogError = root.Q<Label>("dialogError")?.text, modalOpen = modal != null, dialogPrimaryLabel = root.Q<Button>("dialogPrimary")?.text
                });
            }
            if (a.action == "browse-scroll")
            {
                var list = root.Q<ListView>("assetsList");
                ScrollView scroll = a.workspace == "Assets" ? list?.Q<ScrollView>() : a.workspace == "Build" ? root.Q<ScrollView>(className: "build-card-scroll") : a.workspace == "Settings" ? root.Q<ScrollView>(className: "settings-scroll") : root.Q<ScrollView>(className: "preset-gallery-scroll");
                if (scroll == null) throw new InvalidOperationException("Browsing scroll view is not open.");
                if (a.target == "item" && list != null) list.ScrollToItem(a.width);
                else scroll.scrollOffset = new Vector2(0, a.target == "last" ? scroll.verticalScroller.highValue : a.x);
                return ProbeReply();
            }
            if (a.action == "browse-select")
            {
                var list = root.Q<ListView>("assetsList") ?? throw new InvalidOperationException("Assets is not open.");
                list.SetSelection(a.width); list.ScrollToItem(a.width); list.Focus(); return ProbeReply();
            }
            if (a.action == "browse-filter-benchmark")
            {
                var field = root.Q<TextField>("assetsSearch") ?? throw new InvalidOperationException("Assets is not open.");
                var timer = System.Diagnostics.Stopwatch.StartNew(); field.value = a.query; timer.Stop();
                return Reply(new BrowseTiming { milliseconds = timer.Elapsed.TotalMilliseconds, count = root.Q<ListView>("assetsList").itemsSource.Count });
            }
            if (a.action == "preset-cold-cache")
            {
                if (screen != "Presets") throw new InvalidOperationException("Presets is not open.");
                // Delete only cache keys derived from the bundled examples in this local
                // app data folder. Original asset/source bytes are never touched.
                DisposePageResources();
                foreach (var id in new[] { "momo", "abydos", "library", "ship" })
                {
                    var text = Resources.Load<TextAsset>("Examples/" + id); if (!text) continue;
                    var key = PresetPreviewKey(DocumentStore.ParseLayout(text.text)); var path = Path.Combine(appData, "previews", key + ".png"); if (File.Exists(path)) File.Delete(path);
                }
                ShowScreen("Presets"); return ProbeReply();
            }
            if (a.action == "operation-status") { ShowOperationStatus(); return ProbeReply(); }
            return null;
        }
        static string HashBytes(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        [Serializable] sealed class BrowseControl { public string name; public Rect bounds; }
        [Serializable] sealed class PreviewEvidence { public string key, hash; public bool ready, visible, error; public Rect bounds; }
        [Serializable] sealed class BrowseTiming { public double milliseconds; public int count; }
        [Serializable] sealed class BrowsingState
        {
            public string screen, appData, packFingerprint, clipboard, operationStatus, pathError, documentHash, packRoot, dialogError, dialogPrimaryLabel, buildQuery, buildCategory, buildSubcategory, buildTheme, graphicsDevice, processor;
            public bool busy, modalOpen, buildInCafe;
            public int screenWidth, screenHeight, memoryMB; public float panelWidth, panelHeight;
            public BrowseContext build, assets;
            public string[] selectedInstances, assetIds, boundAssetIds;
            public Vector3 cameraPosition; public Quaternion cameraRotation; public float cameraSize, cameraAspect;
            public float assetOffset, assetScrollMaximum, presetOffset, presetScrollMaximum, buildOffset;
            public PreviewEvidence[] presetCards; public BrowseControl[] controls;
        }
    }
}
#endif
