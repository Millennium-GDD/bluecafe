using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;
using RuntimeView = BlueCafe.Unity.RuntimeView;
using Layout = BlueCafe.Core.Layout;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        string presetTab = "My cafes", presetQuery = "", presetSort = "Name", selectedPresetKey;
        readonly Dictionary<string, Texture2D> presetPreviews = new Dictionary<string, Texture2D>();
        Coroutine presetRendering;
        sealed class PresetEntry
        {
            public string key, name, cafeId, updatedUtc;
            public Layout layout;
            public bool example;
        }

        void SettingsPage()
        {
            var page = Box(workspace, "settings-page");
            Label(page, "Settings", "page-heading");
            Label(page, "Your local asset library and saved cafes.", "page-intro");
            var scroll = new ScrollView(); scroll.AddToClassList("settings-scroll"); page.Add(scroll);
            var panel = Box(scroll, "settings-panel");
            var titleRow = Box(panel, "row"); titleRow.Add(new StudioIcon("Assets")); Label(titleRow, "Asset folder", "settings-title");
            Label(panel, "Choose a prepared BlueCafe pack. Original source files stay in this folder.", "settings-copy");
            var pathRow = Box(panel, "settings-path-row");
            var path = new TextField { value = Pack?.Root ?? store.LoadSettings().packRoot ?? "", tooltip = "Folder containing catalog.json" }; path.AddToClassList("settings-path"); pathRow.Add(path);
            Tool(pathRow, "Choose folder", "folder", async () => { try { var chosen = await NativeDialogs.Choose("Choose prepared BlueCafe pack", folder: true); if (!string.IsNullOrEmpty(chosen)) { path.value = chosen; LoadPack(chosen); } } catch (Exception e) { Message(e.Message); } });
            var actions = Box(panel, "row"); Tool(actions, "Use folder", "check", () => LoadPack(path.value), "primary");
            var verify = Tool(actions, "Verify files", "verify", () => LoadPack(path.value)); verify.SetEnabled(!string.IsNullOrWhiteSpace(path.value));
            path.RegisterValueChangedCallback(e => verify.SetEnabled(!string.IsNullOrWhiteSpace(e.newValue)));
            var cancel = Tool(actions, "Cancel verification", "close", () => packLoad?.Cancel()); cancel.SetEnabled(busy);
            panel.schedule.Execute(() => { cancel.SetEnabled(busy); verify.SetEnabled(!busy && !string.IsNullOrWhiteSpace(path.value)); }).Every(250);
            var summary = Box(panel, "settings-summary");
            if (Pack != null)
            {
                Label(summary, Pack.AvailableCount.ToString("N0") + " of " + Pack.Catalog.items.Length.ToString("N0") + " assets available", "settings-summary-title");
                Label(summary, "Region: " + (Pack.Catalog.region ?? "Not declared") + "    Version: " + (Pack.Catalog.gameVersion ?? "Not declared"), "muted");
                Label(summary, Pack.MeshCount.ToString("N0") + " mesh files · " + Pack.ImageCount.ToString("N0") + " image files", "muted");
            }
            else Label(summary, "No verified asset folder", "settings-summary-title");
            FolderRequirements(panel);
            var storage = Box(scroll, "settings-panel");
            Label(storage, "Saved cafes", "settings-title"); Label(storage, "My cafes and automatic recovery are stored on this computer.", "settings-copy");
            Label(storage, Path.Combine(appData, "cafes"), "settings-storage-path");
            Label(storage, "Portable layouts contain furniture IDs and transforms. Import and export do not include asset files.", "settings-copy");
            var storageActions = Box(storage, "row"); Tool(storageActions, "Browse My cafes", "Presets", () => ShowScreen("Presets"));
            var recovery = Box(scroll, "settings-panel"); Label(recovery, "Automatic recovery", "settings-title");
            Label(recovery, "Unsaved work is recovered after 2 seconds of inactivity, or every 30 seconds while editing. Save and explicit Discard clear the recovery copy.", "settings-copy");
            if (pendingRecovery != null)
            {
                Label(recovery, "An earlier recovery copy is waiting. Automatic recovery is paused to preserve it; save current work manually until you restore or discard that copy.", "settings-copy");
                var review = Tool(recovery, "Review recovery", "undo", ReviewRecovery); review.name = "settingsReviewRecovery";
            }
        }

        void FolderRequirements(VisualElement parent)
        {
            var fold = new Foldout { text = "Folder requirements", value = false }; fold.AddToClassList("folder-requirements");
            Label(fold, "catalog.json (version 1), referenced mesh JSON, PNG textures and thumbnails, and source metadata.", "requirements-copy");
            Label(fold, "Missing geometry stays unavailable. A missing thumbnail does not prevent placement. Raw game files must be prepared separately.", "requirements-copy"); parent.Add(fold);
        }

        static string PageCategory(Item item)
        {
            if (Placement.IsRoomStyle(item)) return "Interior";
            if (item.placement == "wall" || new[] { "Prop", "Trophy", "FloorDecoration", "Decoration" }.Contains(item.category)) return "Decoration";
            return "Furniture";
        }
        static string PageSubcategory(Item item)
        {
            if (item.placement == "flooring") return "Flooring";
            if (item.placement == "wallpaper") return "Wallpaper";
            if (item.placement == "background") return "Background";
            return Regex.Replace(item.category ?? "Uncategorized", "([a-z])([A-Z])", "$1 $2");
        }
        void AssetsPage()
        {
            var page = Box(workspace, "assets-page");
            var heading = Box(page, "page-heading-row"); Label(heading, "Assets", "page-heading");
            Label(heading, Pack.AvailableCount.ToString("N0") + " available", "page-heading-count"); Box(heading, "spacer"); Tool(heading, "Verify files", "verify", () => LoadPack(Pack.Root));
            Label(page, "Browse the original assets in your prepared local pack.", "page-intro");
            var pathRow = Box(page, "asset-pack-line"); var folderIcon = new StudioIcon("folder"); pathRow.Add(folderIcon); Label(pathRow, Pack.Root, "asset-pack-path");
            var help = IconButton(pathRow, "question", () =>
            {
                var panel = Modal("Folder requirements");
                Label(panel, "catalog.json (version 1), referenced mesh JSON, PNG textures and thumbnails, and source metadata.", "requirements-copy");
                Label(panel, "Missing geometry stays unavailable. A missing thumbnail does not prevent placement. Raw game files must be prepared separately.", "requirements-copy");
                var actions = Box(panel, "row"); Button(actions, "Close", CloseModal, "primary");
            }, "Folder requirements", "asset-folder-help"); help.name = "assetsFolderHelp";
            Tool(pathRow, "Change folder", "Settings", () => ShowScreen("Settings"));
            var table = Box(page, "assets-table");
            var filters = Box(table, "asset-filters");
            string query = "", sortColumn = "ID"; bool ascending = true; int pageIndex = 0, perPage = 10; Action refresh = null;
            var q = SearchField(filters, "Search name or ID", value => { query = value; pageIndex = 0; refresh?.Invoke(); }); q.AddToClassList("asset-search"); q.name = "assetsSearch";
            var cat = StudioDropdown.Create<string>(new List<string> { "Category: All", "Furniture", "Decoration", "Interior" }, 0); cat.AddToClassList("asset-filter"); cat.name = "assetsCategory"; filters.Add(cat);
            var sub = StudioDropdown.Create<string>(Pack.Catalog.items.Select(PageSubcategory).Distinct().OrderBy(s => s).Prepend("Subcategory: All").ToList(), 0); sub.AddToClassList("asset-filter"); sub.name = "assetsSubcategory"; filters.Add(sub);
            var themes = StudioDropdown.Create<string>(Pack.Catalog.items.Select(i => i.theme).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).Prepend("Theme: All").ToList(), 0); themes.AddToClassList("asset-filter"); themes.name = "assetsTheme"; filters.Add(themes);
            Tool(filters, "Clear", "filter", () => { q.SetValueWithoutNotify(""); query = ""; cat.SetValueWithoutNotify(cat.choices[0]); sub.SetValueWithoutNotify(sub.choices[0]); themes.SetValueWithoutNotify(themes.choices[0]); pageIndex = 0; refresh(); });
            var header = Box(table, "asset-table-header");
            foreach (var col in new[] { "ID", "Icon", "Name", "Category", "Subcategory", "Theme", "Status" })
            {
                string column = col; var b = Button(header, col, () => { if (column == "Icon") return; ascending = sortColumn != column || !ascending; sortColumn = column; pageIndex = 0; refresh(); }, "asset-column-header asset-col-" + col.ToLowerInvariant()); b.name = "assetSort" + col;
            }
            var rows = new ScrollView(); rows.AddToClassList("asset-table-rows"); table.Add(rows);
            var bottom = Box(table, "asset-pagination"); var count = Label(bottom, "", "asset-page-count"); Box(bottom, "spacer"); Label(bottom, "Rows per page", "asset-page-label");
            var rowCount = StudioDropdown.Create<int>(new List<int> { 10, 25, 50 }, 0); rowCount.name = "assetsPerPage"; rowCount.AddToClassList("asset-page-size"); bottom.Add(rowCount);
            var previous = IconButton(bottom, "left", () => { pageIndex--; refresh(); }, "Previous page"); previous.name = "assetsPrevious";
            var next = IconButton(bottom, "right", () => { pageIndex++; refresh(); }, "Next page"); next.name = "assetsNext";
            refresh = () =>
            {
                rows.Clear();
                IEnumerable<Item> found = Pack.Catalog.items.Where(i => (query.Length == 0 || ((i.name ?? "") + " " + i.id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) && (cat.index == 0 || PageCategory(i) == cat.value) && (sub.index == 0 || PageSubcategory(i) == sub.value) && (themes.index == 0 || i.theme == themes.value));
                Func<Item, string> key = i => sortColumn == "Name" ? i.name : sortColumn == "Category" ? PageCategory(i) : sortColumn == "Subcategory" ? PageSubcategory(i) : sortColumn == "Theme" ? i.theme : sortColumn == "Status" ? (i.available ? "Available" : "Unavailable") : i.id;
                if (sortColumn == "ID") found = ascending ? found.OrderBy(i => long.TryParse(i.id, out var n) ? n : long.MaxValue).ThenBy(i => i.id) : found.OrderByDescending(i => long.TryParse(i.id, out var n) ? n : long.MaxValue).ThenByDescending(i => i.id);
                else found = ascending ? found.OrderBy(key, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.id) : found.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.id);
                var matches = found.ToArray(); pageIndex = Mathf.Clamp(pageIndex, 0, Math.Max(0, (matches.Length - 1) / perPage)); var visible = matches.Skip(pageIndex * perPage).Take(perPage).ToArray(); BeginThumbnailScope(visible.Select(i => i.id));
                foreach (var i in visible)
                {
                    var row = Box(rows, "asset-table-row");
                    Label(row, i.id, "asset-col-id"); var image = new Image { image = Thumbnail(i), scaleMode = ScaleMode.ScaleToFit, tooltip = Pack.Availability(i.id).ThumbnailAvailable ? i.name : Pack.Availability(i.id).ThumbnailReason }; image.AddToClassList("asset-col-icon"); if (image.image == null) { var missing = new Label("No image"); missing.AddToClassList("asset-missing-image"); image.Add(missing); } row.Add(image);
                    var name = Label(row, i.name, "asset-col-name"); name.tooltip = i.name;
                    Label(row, PageCategory(i), "asset-col-category"); Label(row, PageSubcategory(i), "asset-col-subcategory"); Label(row, i.theme ?? "—", "asset-col-theme");
                    var status = Box(row, "asset-col-status"); var marker = new StudioIcon(i.available ? "check" : "warning"); marker.AddToClassList(i.available ? "available-icon" : "unavailable-icon"); status.Add(marker); Label(status, i.available ? "Available" : "Unavailable", "asset-status-text"); status.tooltip = i.available ? "Original asset files verified" : Pack.Availability(i.id).Reason;
                    if (!i.available) row.AddToClassList("asset-unavailable-row");
                }
                if (matches.Length == 0) Label(rows, "No assets match these filters. Clear the filters to browse your pack.", "page-empty");
                foreach (var b in header.Children().OfType<Button>()) b.text = b.name == "assetSort" + sortColumn ? sortColumn + (ascending ? " ↑" : " ↓") : b.name.Substring("assetSort".Length);
                int start = matches.Length == 0 ? 0 : pageIndex * perPage + 1; count.text = start + "–" + (pageIndex * perPage + visible.Length) + " of " + matches.Length.ToString("N0"); previous.SetEnabled(pageIndex > 0); next.SetEnabled((pageIndex + 1) * perPage < matches.Length);
                Message(Pack.Catalog.items.Length.ToString("N0") + " catalog items · " + Pack.AvailableCount.ToString("N0") + " available");
            };
            cat.RegisterValueChangedCallback(_ => { pageIndex = 0; refresh(); }); sub.RegisterValueChangedCallback(_ => { pageIndex = 0; refresh(); }); themes.RegisterValueChangedCallback(_ => { pageIndex = 0; refresh(); }); rowCount.RegisterValueChangedCallback(e => { perPage = e.newValue; pageIndex = 0; refresh(); }); refresh();
        }

        void PresetsPage()
        {
            if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; }
            Action draw = null;
            var body = Box(workspace, "preset-body"); var page = Box(body, "presets-page"); var detail = new ScrollView(); detail.AddToClassList("preset-detail"); body.Add(detail);
            var heading = Box(page, "page-heading-row"); Label(heading, "Presets", "page-heading"); Box(heading, "spacer"); Tool(heading, "Import", "import", Import); Tool(heading, "New cafe", "plus", () => ReplacePrompt(NewCafe), "primary");
            var controls = Box(page, "preset-controls"); var tabs = Box(controls, "preset-tabs"); var my = Button(tabs, "My cafes", () => { presetTab = "My cafes"; selectedPresetKey = null; PresetsPageRefresh(); }, "preset-tab"); var examples = Button(tabs, "Examples", () => { presetTab = "Examples"; selectedPresetKey = null; PresetsPageRefresh(); }, "preset-tab"); my.name = "presetsMyCafes"; examples.name = "presetsExamples";
            var query = SearchField(controls, "Search presets", value => { presetQuery = value; PresetsPageRefresh(); }); query.SetValueWithoutNotify(presetQuery); query.AddToClassList("preset-search"); query.name = "presetsSearch";
            var sort = StudioDropdown.Create<string>(new List<string> { "Name", "Items", "Recently saved" }, Math.Max(0, new[] { "Name", "Items", "Recently saved" }.ToList().IndexOf(presetSort))); sort.name = "presetsSort"; sort.AddToClassList("preset-sort"); controls.Add(sort);
            var scroll = new ScrollView(); scroll.AddToClassList("preset-gallery-scroll"); page.Add(scroll); var grid = Box(scroll, "preset-gallery");
            var empty = Label(page, "", "preset-empty"); var hint = Label(page, "Choose a cafe to preview. Your current cafe stays open until you choose Open.", "preset-page-hint");
            List<PresetEntry> entries = new List<PresetEntry>();
            void PresetsPageRefresh() { draw?.Invoke(); }
            draw = () =>
            {
                if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; }
                entries.Clear(); grid.Clear(); detail.Clear(); my.EnableInClassList("active", presetTab == "My cafes"); examples.EnableInClassList("active", presetTab == "Examples");
                if (presetTab == "Examples")
                {
                    foreach (var id in new[] { "momo", "abydos", "library", "ship" })
                    {
                        var source = Resources.Load<TextAsset>("Examples/" + id); if (source == null) continue;
                        try { var layout = DocumentStore.ParseLayout(source.text); entries.Add(new PresetEntry { key = "example:" + id, name = layout.name, layout = layout, example = true }); }
                        catch (Exception e) { Message("Example " + id + ": " + e.Message); }
                    }
                }
                else foreach (var r in store.ListCafes()) entries.Add(new PresetEntry { key = r.cafeId, cafeId = r.cafeId, name = r.name, layout = r.layout, updatedUtc = r.updatedUtc });
                entries = entries.Where(e => e.name.IndexOf(presetQuery, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                entries = presetSort == "Items" ? entries.OrderByDescending(e => e.layout.instances.Count).ThenBy(e => e.name).ToList() : presetSort == "Recently saved" ? entries.OrderByDescending(e => e.updatedUtc).ThenBy(e => e.name).ToList() : entries.OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
                if (!entries.Any(e => e.key == selectedPresetKey)) selectedPresetKey = entries.FirstOrDefault()?.key;
                var visible = entries.Take(12).ToArray(); var images = new Dictionary<PresetEntry, Image>();
                foreach (var entry in visible)
                {
                    var card = Box(grid, "preset-card"); card.EnableInClassList("selected", entry.key == selectedPresetKey); card.name = "preset-" + entry.key; card.focusable = true;
                    var img = new Image { scaleMode = ScaleMode.ScaleToFit }; img.AddToClassList("preset-card-preview"); var waiting = new Label("Rendering preview…") { name = "previewWaiting" }; waiting.AddToClassList("preset-preview-waiting"); img.Add(waiting); card.Add(img); images.Add(entry, img);
                    var caption = Box(card, "preset-card-caption"); var nameRow = Box(caption, "row"); var name = Label(nameRow, entry.name, "preset-card-name"); name.tooltip = entry.name; Label(nameRow, entry.example ? "Example" : "Saved", "preset-badge");
                    Label(caption, entry.layout.room.width + " × " + entry.layout.room.depth + " m · " + entry.layout.instances.Count + " items", "preset-card-meta");
                    card.RegisterCallback<ClickEvent>(_ => { selectedPresetKey = entry.key; foreach (var other in grid.Children()) other.EnableInClassList("selected", other.name == "preset-" + entry.key); DrawPresetDetail(detail, entry); });
                    card.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space) { selectedPresetKey = entry.key; foreach (var other in grid.Children()) other.EnableInClassList("selected", other.name == "preset-" + entry.key); DrawPresetDetail(detail, entry); e.StopPropagation(); } });
                    if (presetPreviews.TryGetValue(PresetPreviewKey(entry.layout), out var cached)) { img.image = cached; waiting.RemoveFromHierarchy(); }
                }
                empty.text = entries.Count == 0 ? (presetQuery.Length > 0 ? "No cafes match your search." : presetTab == "My cafes" ? "Your saved cafes will appear here. Save the current cafe or import a portable layout to begin." : "No example layouts are available.") : "";
                empty.EnableInClassList("hidden", entries.Count > 0); hint.text = entries.Count > 12 ? "Showing the first 12 results. Search to find another cafe." : "Choose a cafe to preview. Your current cafe stays open until you choose Open.";
                var selected = entries.FirstOrDefault(e => e.key == selectedPresetKey); if (selected != null) DrawPresetDetail(detail, selected); else { Label(detail, "Cafe preview", "preset-detail-title"); Label(detail, "Choose a saved cafe or example to see its layout and Interior.", "settings-copy"); }
                Message(entries.Count + (presetTab == "Examples" ? " examples" : " saved cafes"));
                if (selected != null && !images.ContainsKey(selected)) images.Add(selected, detail.Q<Image>("presetDetailPreview"));
                var retained = images.Keys.Select(e => PresetPreviewKey(e.layout)).ToHashSet(); foreach (var old in presetPreviews.Keys.Where(k => !retained.Contains(k)).ToArray()) { Destroy(presetPreviews[old]); presetPreviews.Remove(old); }
                presetRendering = StartCoroutine(RenderPresetPreviews(images, detail));
            };
            sort.RegisterValueChangedCallback(e => { presetSort = e.newValue; draw(); }); draw();
            body.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target != body) return; if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; } });
        }
        string PresetPreviewKey(Layout layout)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("preview-v2:" + Pack.Fingerprint + DocumentStore.SerializeLayout(layout)))).Replace("-", "").ToLowerInvariant();
        }
        IEnumerator RenderPresetPreviews(Dictionary<PresetEntry, Image> images, VisualElement detail)
        {
            foreach (var pair in images)
            {
                yield return null;
                if (screen != "Presets" || pair.Value.panel == null) yield break;
                var key = PresetPreviewKey(pair.Key.layout);
                if (!presetPreviews.TryGetValue(key, out var texture))
                {
                    try
                    {
                        var cache = Path.Combine(appData, "previews", key + ".png");
                        if (!File.Exists(cache)) using (var projection = new RuntimeView(Pack)) { projection.Sync(pair.Key.layout, 0); projection.Camera.backgroundColor = new Color(.82f, .91f, .98f); projection.Capture(cache, 720, 380); }
                        texture = new Texture2D(2, 2, TextureFormat.RGB24, false); if (!texture.LoadImage(File.ReadAllBytes(cache))) { Destroy(texture); texture = null; throw new IOException("Cannot decode rendered preview."); }
                        presetPreviews[key] = texture;
                    }
                    catch (Exception e) { if (texture && !presetPreviews.ContainsKey(key)) { Destroy(texture); texture = null; } pair.Value.tooltip = "Preview unavailable: " + e.Message; var waiting = pair.Value.Q<Label>("previewWaiting"); if (waiting != null) waiting.text = "Preview unavailable"; Message("Preview unavailable for " + pair.Key.name + ": " + e.Message); }
                }
                pair.Value.image = texture; if (texture) pair.Value.Q<Label>("previewWaiting")?.RemoveFromHierarchy();
                if (selectedPresetKey == pair.Key.key) { var selectedImage = detail.Q<Image>("presetDetailPreview"); if (selectedImage != null) selectedImage.image = texture; }
            }
            presetRendering = null;
        }
        void DrawPresetDetail(VisualElement detail, PresetEntry entry)
        {
            detail.Clear(); Label(detail, entry.name, "preset-detail-title");
            var image = new Image { name = "presetDetailPreview", scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("preset-detail-preview"); if (presetPreviews.TryGetValue(PresetPreviewKey(entry.layout), out var preview)) image.image = preview; detail.Add(image);
            PresetStat(detail, "Rooms", entry.layout.room.width + " × " + entry.layout.room.depth + " m"); PresetStat(detail, "Presets", entry.layout.instances.Count + " items");
            int unavailable = entry.layout.instances.Count(i => !Pack.Catalog.items.Any(p => p.id == i.furnitureId && p.available));
            PresetStat(detail, unavailable == 0 ? "check" : "warning", unavailable == 0 ? "All assets available" : unavailable + " items unavailable");
            if (!entry.example) Tool(detail, "Open", "folder", () => ReplacePrompt(() => OpenCafe(entry.cafeId, false)), "primary preset-open");
            Tool(detail, "Open as new cafe", "plus", () => ReplacePrompt(() => { if (!entry.example) { OpenCafe(entry.cafeId, true); return; } var candidate = Copies.Copy(entry.layout); Placement.ValidateLayout(Pack.Catalog, candidate, true); var result = Session.OpenDocument(candidate); if (!result.success) throw new Exception(result.message); FinishReplacement(result); Refresh(); View.FrameRoom(); ShowScreen("Build"); }), entry.example ? "primary preset-open" : "preset-open");
            Tool(detail, "Export preset", "export", () => InputModal("Export preset", "JSON path", Path.Combine(appData, "Exports", SafePresetFilename(entry.name) + ".layout.json"), path => { DocumentStore.SaveLayout(path, entry.layout); Message("Exported " + path); }), "preset-export");
            Label(detail, "Opening prompts you to save unsaved work. Export leaves the current cafe open.", "preset-action-hint");
            var interior = Box(detail, "preset-interior"); Label(interior, "Interior", "subheading"); DrawInterior(interior, entry.layout, false);
        }
        void PresetStat(VisualElement parent, string icon, string value) { var row = Box(parent, "preset-stat"); row.Add(new StudioIcon(icon)); Label(row, value, "preset-stat-text"); }
        static string SafePresetFilename(string name) { foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-'); return name.Replace('/', '-').Replace('\\', '-'); }
        void DisposePageResources()
        {
            if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; }
            foreach (var texture in presetPreviews.Values) if (texture) Destroy(texture); presetPreviews.Clear();
        }
    }
}
