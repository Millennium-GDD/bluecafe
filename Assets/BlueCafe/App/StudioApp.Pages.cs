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
            public string key, name, cafeId, updatedUtc, exampleId;
            public Layout layout;
            public bool example;
        }

        void SettingsPage()
        {
            var page = Box(workspace, "settings-page");
            Label(page, "Settings", "page-heading");
            Label(page, "Your local asset library and saved cafes.", "page-intro");
            var scroll = new ScrollView(); scroll.AddToClassList("settings-scroll"); page.Add(scroll);
            AppearanceSettings(scroll);
            var panel = Box(scroll, "settings-panel");
            var titleRow = Box(panel, "row"); titleRow.Add(new StudioIcon("Assets")); Label(titleRow, "Asset folder", "settings-title");
            Label(panel, "Choose a prepared BlueCafe pack. Original source files stay in this folder.", "settings-copy");
            var pathRow = Box(panel, "settings-path-row");
            var path = new TextField { name = "settingsPackPath", value = Pack?.Root ?? store.LoadSettings().packRoot ?? "", tooltip = "Folder containing catalog.json" }; path.AddToClassList("settings-path"); pathRow.Add(path);
            Tool(pathRow, "Copy path", "duplicate", () => CopyText(path.value));
            var pathError = Label(panel, "", "error settings-path-error"); string attemptedPath = null;
            void VerifyFolder()
            {
                if (string.IsNullOrWhiteSpace(path.value)) { pathError.text = "Choose a prepared asset folder containing catalog.json."; path.Focus(); return; }
                if (!Directory.Exists(path.value)) { pathError.text = "This folder does not exist. Check the full path or choose another folder."; path.Focus(); return; }
                attemptedPath = path.value; pathError.text = ""; LoadPack(path.value);
            }
            var choose = Tool(pathRow, "Choose folder", "folder", async () => { try { var chosen = await NativeDialogs.Choose("Choose prepared BlueCafe pack", folder: true); if (!string.IsNullOrEmpty(chosen)) { path.value = chosen; VerifyFolder(); } } catch (Exception e) { pathError.text = e.Message; } });
            var actions = Box(panel, "row"); var use = Tool(actions, "Use folder", "check", VerifyFolder, "primary"); use.name = "settingsUseFolder";
            var verify = Tool(actions, "Verify files", "verify", VerifyFolder);
            path.RegisterValueChangedCallback(_ => pathError.text = "");
            panel.schedule.Execute(() => { choose.SetEnabled(!busy); use.SetEnabled(!busy && !string.IsNullOrWhiteSpace(path.value)); verify.SetEnabled(!busy && !string.IsNullOrWhiteSpace(path.value)); if (path.value == attemptedPath && operationStatus.StartsWith("Asset folder:", StringComparison.Ordinal)) pathError.text = operationStatus; }).Every(250);
            AddOperationStatus(panel);
            var summary = Box(panel, "settings-summary");
            if (Pack != null)
            {
                Label(summary, Pack.AvailableCount.ToString("N0") + " of " + Pack.Catalog.items.Length.ToString("N0") + " assets available", "settings-summary-title");
                Label(summary, "Region: " + (Pack.Catalog.region ?? "Not declared") + "    Version: " + (Pack.Catalog.gameVersion ?? "Not declared"), "muted");
                Label(summary, Pack.MeshCount.ToString("N0") + " mesh files · " + Pack.ImageCount.ToString("N0") + " image files", "muted");
            }
            else Label(summary, "No verified asset folder", "settings-summary-title");
            FolderRequirements(panel, true);
            var storage = Box(scroll, "settings-panel");
            Label(storage, "Saved cafes", "settings-title"); Label(storage, "My cafes and automatic recovery are stored on this computer.", "settings-copy");
            var storagePath = Path.Combine(appData, "cafes"); var storedPath = Label(storage, storagePath, "settings-storage-path"); storedPath.tooltip = storagePath; Tool(storage, "Copy storage path", "duplicate", () => CopyText(storagePath));
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

        void FolderRequirements(VisualElement parent, bool showHeading = false)
        {
            var requirements = Box(parent, "folder-requirements"); requirements.name = "folderRequirements";
            if (showHeading) Label(requirements, "Folder requirements", "subheading");
            Label(requirements, "catalog.json (version 1), referenced mesh JSON, PNG textures and thumbnails, and source metadata.", "requirements-copy");
            Label(requirements, "Missing geometry stays unavailable. A missing thumbnail does not prevent placement. Raw game files must be prepared separately.", "requirements-copy");
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
            var state = assetsBrowsing; state.query = state.query ?? "";
            var page = Box(workspace, "assets-page");
            var heading = Box(page, "page-heading-row"); Label(heading, "Assets", "page-heading");
            Label(heading, Pack.AvailableCount.ToString("N0") + " available", "page-heading-count"); Box(heading, "spacer"); Tool(heading, "Verify files", "verify", () => LoadPack(Pack.Root));
            Label(page, "Browse the original assets in your prepared local pack.", "page-intro");
            var pathRow = Box(page, "asset-pack-line"); pathRow.Add(new StudioIcon("folder")); var pathLabel = Label(pathRow, Pack.Root, "asset-pack-path"); pathLabel.tooltip = Pack.Root;
            Tool(pathRow, "Copy path", "duplicate", () => CopyText(Pack.Root));
            var help = IconButton(pathRow, "question", () =>
            {
                var panel = Modal("Folder requirements"); FolderRequirements(panel); var actions = Box(panel, "row"); Button(actions, "Close", CloseModal, "primary");
            }, "Folder requirements", "asset-folder-help"); help.name = "assetsFolderHelp";
            Tool(pathRow, "Change folder", "Settings", () => ShowScreen("Settings"));
            var table = Box(page, "assets-table"); var filters = Box(table, "asset-filters"); Action<bool> refresh = null;
            var q = SearchField(filters, "Search name or ID", value => { state.query = value; refresh?.Invoke(true); }); q.SetValueWithoutNotify(state.query); q.AddToClassList("asset-search"); q.name = "assetsSearch"; search = q;
            var catChoices = new List<string> { "Category: All", "Furniture", "Decoration", "Interior" };
            var cat = StudioDropdown.Create<string>(catChoices, ValidChoice(catChoices, state.category)); cat.AddToClassList("asset-filter"); cat.name = "assetsCategory"; filters.Add(cat);
            var subChoices = Pack.Catalog.items.Select(PageSubcategory).Distinct().OrderBy(x => x).Prepend("Subcategory: All").ToList();
            var sub = StudioDropdown.Create<string>(subChoices, ValidChoice(subChoices, state.subcategory)); sub.AddToClassList("asset-filter"); sub.name = "assetsSubcategory"; filters.Add(sub);
            var themeChoices = Pack.Catalog.items.Select(i => i.theme).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).Prepend("Theme: All").ToList();
            var themes = StudioDropdown.Create<string>(themeChoices, ValidChoice(themeChoices, state.theme)); themes.AddToClassList("asset-filter"); themes.name = "assetsTheme"; filters.Add(themes);
            var used = new Toggle("In this cafe") { value = state.inCafe, name = "assetsInCafe" }; used.AddToClassList("asset-in-cafe"); filters.Add(used);
            var clear = Tool(filters, "Clear", "filter", () => { q.SetValueWithoutNotify(""); state.query = ""; cat.SetValueWithoutNotify(cat.choices[0]); sub.SetValueWithoutNotify(sub.choices[0]); themes.SetValueWithoutNotify(themes.choices[0]); used.SetValueWithoutNotify(false); state.inCafe = false; refresh(true); }, "asset-clear"); clear.name = "assetsClear"; clear.tooltip = "Reset filters";
            var header = Box(table, "asset-table-header");
            foreach (var col in new[] { "ID", "Icon", "Name", "Category", "Subcategory", "Theme", "Status" })
            {
                string column = col; var button = Button(header, col, () => { if (column == "Icon") return; state.ascending = state.sortColumn != column || !state.ascending; state.sortColumn = column; refresh(true); }, "asset-column-header asset-col-" + col.ToLowerInvariant()); button.name = "assetSort" + col; if (col == "Icon") button.SetEnabled(false);
            }
            var matches = Array.Empty<Item>(); var bound = new Dictionary<VisualElement, string>();
            var rows = new ListView { name = "assetsList", fixedItemHeight = 57, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, selectionType = SelectionType.Single, horizontalScrollingEnabled = false };
            rows.AddToClassList("asset-table-rows"); table.Add(rows);
            rows.makeItem = () => new VisualElement();
            rows.unbindItem = (row, index) => { bound.Remove(row); row.Clear(); };
            rows.bindItem = (row, index) =>
            {
                if (index < 0 || index >= matches.Length) return; var item = matches[index];
                bound[row] = item.id; BeginThumbnailScope(bound.Values); row.Clear(); row.AddToClassList("asset-table-row"); row.EnableInClassList("asset-unavailable-row", !item.available); row.tooltip = item.id + " · " + item.name;
                Label(row, item.id, "asset-col-id"); var image = new Image { image = Thumbnail(item), scaleMode = ScaleMode.ScaleToFit, tooltip = Pack.Availability(item.id).ThumbnailAvailable ? item.name : Pack.Availability(item.id).ThumbnailReason }; image.AddToClassList("asset-col-icon"); if (image.image == null) { var missing = new Label("No image"); missing.AddToClassList("asset-missing-image"); image.Add(missing); } row.Add(image);
                var name = Label(row, item.name, "asset-col-name"); name.tooltip = item.name;
                Label(row, PageCategory(item), "asset-col-category"); Label(row, PageSubcategory(item), "asset-col-subcategory"); var themeLabel = Label(row, item.theme ?? "—", "asset-col-theme"); themeLabel.tooltip = item.theme;
                var status = Box(row, "asset-col-status"); var marker = new StudioIcon(item.available ? "check" : "warning"); marker.AddToClassList(item.available ? "available-icon" : "unavailable-icon"); status.Add(marker); Label(status, item.available ? "Available" : "Unavailable", "asset-status-text"); status.tooltip = item.available ? "Original asset files verified" : Pack.Availability(item.id).Reason;
                // Context callbacks are installed once and resolve the currently bound item.
                row.userData = item;
                if (row.name != "asset-recycled-row") { row.name = "asset-recycled-row"; row.AddManipulator(new ContextualMenuManipulator(e => { var current = row.userData as Item; if (current == null) return; e.menu.AppendAction("Copy ID", _ => CopyText(current.id)); e.menu.AppendAction("Copy name", _ => CopyText(current.name)); e.menu.AppendAction("Copy asset details", _ => CopyText(current.id + "\n" + current.name + "\n" + Pack.Availability(current.id).Reason)); })); }
            };
            rows.selectionChanged += selected => { state.selectedId = selected.OfType<Item>().FirstOrDefault()?.id; };
            var empty = Label(table, "No assets match these filters. Clear the filters to browse your pack.", "page-empty");
            var bottom = Box(table, "asset-browse-summary"); var count = Label(bottom, "", "asset-page-count"); Box(bottom, "spacer"); Label(bottom, "Search name or ID · Home / End jump to the first / last result", "asset-browse-help");
            void UpdateCount() { var scroll = rows.Q<ScrollView>(); int first = matches.Length == 0 ? 0 : Mathf.Clamp((int)((scroll?.scrollOffset.y ?? 0) / 57) + 1, 1, matches.Length); count.text = matches.Length == 0 ? "0 results" : first.ToString("N0") + " of " + matches.Length.ToString("N0") + " results"; }
            refresh = reset =>
            {
                state.category = cat.index == 0 ? "" : cat.value; state.subcategory = sub.index == 0 ? "" : sub.value; state.theme = themes.index == 0 ? "" : themes.value; state.inCafe = used.value;
                var ids = Session.Layout.instances.Select(i => i.furnitureId).ToHashSet();
                IEnumerable<Item> found = Pack.Catalog.items.Where(i => (state.query.Length == 0 || ((i.name ?? "") + " " + i.id).IndexOf(state.query, StringComparison.OrdinalIgnoreCase) >= 0) && (cat.index == 0 || PageCategory(i) == cat.value) && (sub.index == 0 || PageSubcategory(i) == sub.value) && (themes.index == 0 || i.theme == themes.value) && (!used.value || ids.Contains(i.id)));
                Func<Item, string> key = i => state.sortColumn == "Name" ? i.name : state.sortColumn == "Category" ? PageCategory(i) : state.sortColumn == "Subcategory" ? PageSubcategory(i) : state.sortColumn == "Theme" ? i.theme : state.sortColumn == "Status" ? (i.available ? "Available" : "Unavailable") : i.id;
                if (state.sortColumn == "ID") found = state.ascending ? found.OrderBy(i => long.TryParse(i.id, out var n) ? n : long.MaxValue).ThenBy(i => i.id) : found.OrderByDescending(i => long.TryParse(i.id, out var n) ? n : long.MaxValue).ThenByDescending(i => i.id);
                else found = state.ascending ? found.OrderBy(key, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.id) : found.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.id);
                matches = found.ToArray(); bound.Clear(); rows.itemsSource = matches; rows.Rebuild(); empty.EnableInClassList("hidden", matches.Length > 0);
                int selectedIndex = Array.FindIndex(matches, i => i.id == state.selectedId); if (selectedIndex >= 0) rows.SetSelectionWithoutNotify(new[] { selectedIndex }); else { state.selectedId = null; rows.ClearSelection(); }
                if (reset) { state.scroll = 0; rows.ScrollToItem(0); }
                foreach (var button in header.Children().OfType<Button>()) button.text = button.name == "assetSort" + state.sortColumn ? state.sortColumn + (state.ascending ? " ↑" : " ↓") : button.name.Substring("assetSort".Length);
                UpdateCount();
            };
            cat.RegisterValueChangedCallback(_ => refresh(true)); sub.RegisterValueChangedCallback(_ => refresh(true)); themes.RegisterValueChangedCallback(_ => refresh(true)); used.RegisterValueChangedCallback(_ => refresh(true)); refresh(false);
            float retainedOffset = state.scroll;
            rows.schedule.Execute(() => { var scroll = rows.Q<ScrollView>(); if (scroll == null) return; RestoreBrowseScroll(scroll, retainedOffset, () => { state.scroll = scroll.scrollOffset.y; scroll.verticalScroller.valueChanged += value => { state.scroll = value; UpdateCount(); }; if (!string.IsNullOrEmpty(state.focusName)) workspace.Q<VisualElement>(state.focusName)?.Focus(); UpdateCount(); }); });
        }

        void PresetsPage()
        {
            if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; }
            Action draw = null;
            var body = Box(workspace, "preset-body"); var page = Box(body, "presets-page"); var detail = new ScrollView(); detail.AddToClassList("preset-detail"); detail.EnableInClassList("hidden", !presetDetailsVisible); body.Add(detail);
            var heading = Box(page, "page-heading-row"); Label(heading, "Presets", "page-heading"); Box(heading, "spacer"); Tool(heading, "Import", "import", Import); Tool(heading, "New cafe", "plus", () => ReplacePrompt(NewCafe), "primary"); var detailToggle = Tool(heading, presetDetailsVisible ? "Hide details" : "Show details", "Presets", () => { presetDetailsVisible = !presetDetailsVisible; detail.EnableInClassList("hidden", !presetDetailsVisible); }, "preset-detail-toggle"); detailToggle.name = "presetDetailsToggle"; detailToggle.schedule.Execute(() => detailToggle.Q<Label>(className: "tool-label").text = presetDetailsVisible ? "Hide details" : "Show details").Every(200);
            var controls = Box(page, "preset-controls"); var tabs = Box(controls, "preset-tabs"); var my = Button(tabs, "My cafes", () => { CaptureBrowsingContext(); presetTab = "My cafes"; selectedPresetKey = myCafeSelection; PresetsPageRefresh(); }, "preset-tab"); var examples = Button(tabs, "Examples", () => { CaptureBrowsingContext(); presetTab = "Examples"; selectedPresetKey = exampleSelection; PresetsPageRefresh(); }, "preset-tab"); my.name = "presetsMyCafes"; examples.name = "presetsExamples";
            var query = SearchField(controls, "Search presets", value => { presetQuery = value; if (presetTab == "Examples") examplesScroll = 0; else myCafesScroll = 0; PresetsPageRefresh(); }); query.SetValueWithoutNotify(presetQuery); query.AddToClassList("preset-search"); query.name = "presetsSearch";
            var sort = StudioDropdown.Create<string>(new List<string> { "Name", "Items", "Recently saved" }, Math.Max(0, new[] { "Name", "Items", "Recently saved" }.ToList().IndexOf(presetSort))); sort.name = "presetsSort"; sort.AddToClassList("preset-sort"); controls.Add(sort);
            var scroll = new ScrollView(); scroll.AddToClassList("preset-gallery-scroll"); page.Add(scroll); var grid = Box(scroll, "preset-gallery");
            var empty = Label(page, "", "preset-empty"); var hint = Label(page, "Double-click a cafe to open it. Choose once to see details.", "preset-page-hint");
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
                        try { var layout = DocumentStore.ParseLayout(source.text); entries.Add(new PresetEntry { key = "example:" + id, name = layout.name, layout = layout, example = true, exampleId = id }); }
                        catch (Exception e) { Message("Example " + id + ": " + e.Message); }
                    }
                }
                else foreach (var r in store.ListCafes()) entries.Add(new PresetEntry { key = r.cafeId, cafeId = r.cafeId, name = r.name, layout = r.layout, updatedUtc = r.updatedUtc });
                entries = entries.Where(e => e.name.IndexOf(presetQuery, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                entries = presetSort == "Items" ? entries.OrderByDescending(e => e.layout.instances.Count).ThenBy(e => e.name).ToList() : presetSort == "Recently saved" ? entries.OrderByDescending(e => e.updatedUtc).ThenBy(e => e.name).ToList() : entries.OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
                if (!entries.Any(e => e.key == selectedPresetKey)) selectedPresetKey = entries.FirstOrDefault()?.key;
                var visible = entries.ToArray(); var images = new Dictionary<PresetEntry, Image>();
                foreach (var entry in visible)
                {
                    var card = Box(grid, "preset-card"); card.EnableInClassList("selected", entry.key == selectedPresetKey); card.name = "preset-" + entry.key; card.focusable = true;
                    var img = new Image { scaleMode = ScaleMode.ScaleToFit }; img.AddToClassList("preset-card-preview"); var waiting = new Label("Rendering preview…") { name = "previewWaiting" }; waiting.AddToClassList("preset-preview-waiting"); img.Add(waiting); card.Add(img); images.Add(entry, img);
                    var caption = Box(card, "preset-card-caption"); var nameRow = Box(caption, "row"); var name = Label(nameRow, entry.name, "preset-card-name"); name.tooltip = entry.name; Label(nameRow, entry.example ? "Example" : "Saved", "preset-badge");
                    Label(caption, entry.layout.room.width + " × " + entry.layout.room.depth + " m · " + entry.layout.instances.Count + " items", "preset-card-meta");
                    card.RegisterCallback<ClickEvent>(e => { selectedPresetKey = entry.key; foreach (var other in grid.Children()) other.EnableInClassList("selected", other.name == "preset-" + entry.key); DrawPresetDetail(detail, entry); if (e.clickCount == 2) OpenPreset(entry); });
                    card.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space) { selectedPresetKey = entry.key; foreach (var other in grid.Children()) other.EnableInClassList("selected", other.name == "preset-" + entry.key); DrawPresetDetail(detail, entry); e.StopPropagation(); } });
                    if (presetPreviews.TryGetValue(PresetPreviewKey(entry.layout), out var cached)) { img.image = cached; waiting.RemoveFromHierarchy(); }
                }
                empty.text = entries.Count == 0 ? (presetQuery.Length > 0 ? "No cafes match your search." : presetTab == "My cafes" ? "Your saved cafes will appear here. Save the current cafe or import a portable layout to begin." : "No example layouts are available.") : "";
                empty.EnableInClassList("hidden", entries.Count > 0); hint.text = entries.Count.ToString("N0") + " cafes · Double-click a cafe to open it. Choose once to see details.";
                var selected = entries.FirstOrDefault(e => e.key == selectedPresetKey); if (selected != null) DrawPresetDetail(detail, selected); else { Label(detail, "Cafe preview", "preset-detail-title"); Label(detail, "Choose a saved cafe or example to see its layout and Interior.", "settings-copy"); }
                Message(entries.Count + (presetTab == "Examples" ? " examples" : " saved cafes"));
                var retained = images.Keys.Select(e => PresetPreviewKey(e.layout)).ToHashSet(); foreach (var old in presetPreviews.Keys.Where(k => !retained.Contains(k)).ToArray()) { Destroy(presetPreviews[old]); presetPreviews.Remove(old); }
                float savedScroll = presetTab == "Examples" ? examplesScroll : myCafesScroll; RestoreBrowseScroll(scroll, savedScroll);
                presetRendering = StartCoroutine(RenderPresetPreviews(images, detail));
            };
            scroll.verticalScroller.valueChanged += value => { if (presetTab == "Examples") examplesScroll = value; else myCafesScroll = value; };
            sort.RegisterValueChangedCallback(e => { presetSort = e.newValue; if (presetTab == "Examples") examplesScroll = 0; else myCafesScroll = 0; draw(); }); draw();
            body.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target != body) return; if (presetRendering != null) { StopCoroutine(presetRendering); presetRendering = null; } });
        }
        string PresetPreviewKey(Layout layout)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("preview-v3-isolated:" + Pack.Fingerprint + DocumentStore.SerializeLayout(layout)))).Replace("-", "").ToLowerInvariant();
        }
        IEnumerator RenderPresetPreviews(Dictionary<PresetEntry, Image> images, VisualElement detail)
        {
            // Preview only cards currently on screen; a large local collection remains browsable
            // without synchronously rendering hundreds of original-asset scenes.
            while (screen == "Presets" && detail.panel != null)
            {
                yield return null;
                var viewportBounds = workspace.Q<ScrollView>(className: "preset-gallery-scroll")?.contentViewport.worldBound ?? workspace.worldBound;
                var visible = images.Where(pair => pair.Value.panel != null && pair.Value.worldBound.Overlaps(viewportBounds)).ToArray();
                var retained = visible.Select(pair => PresetPreviewKey(pair.Key.layout)).ToHashSet();
                if (presetPreviews.Count > 24)
                    foreach (var old in presetPreviews.Keys.Where(key => !retained.Contains(key)).ToArray()) { foreach (var pair in images.Where(pair => PresetPreviewKey(pair.Key.layout) == old)) pair.Value.image = null; Destroy(presetPreviews[old]); presetPreviews.Remove(old); }
                foreach (var pair in visible)
                {
                    var key = PresetPreviewKey(pair.Key.layout);
                    if (pair.Value.image != null || pair.Value.ClassListContains("preview-failed")) continue;
                    if (!presetPreviews.TryGetValue(key, out var texture))
                    {
                        try
                        {
                            var cache = Path.Combine(appData, "previews", key + ".png");
                            if (!File.Exists(cache)) using (var projection = new RuntimeView(Pack)) { projection.Sync(pair.Key.layout, 0); projection.Camera.backgroundColor = new Color(.82f, .91f, .98f); projection.Capture(cache, 720, 380); }
                            texture = new Texture2D(2, 2, TextureFormat.RGB24, false); if (!texture.LoadImage(File.ReadAllBytes(cache))) { Destroy(texture); texture = null; throw new IOException("Cannot decode rendered preview."); }
                            presetPreviews[key] = texture;
                        }
                        catch (Exception e)
                        {
                            if (texture && !presetPreviews.ContainsKey(key)) { Destroy(texture); texture = null; }
                            pair.Value.AddToClassList("preview-failed"); pair.Value.tooltip = "Preview unavailable: " + e.Message; var waiting = pair.Value.Q<Label>("previewWaiting"); if (waiting != null) waiting.text = "Preview unavailable\n" + e.Message;
                            var failure = e.Message; pair.Value.AddManipulator(new ContextualMenuManipulator(evt => { evt.menu.AppendAction("Copy preview error", _ => CopyText(failure)); evt.menu.AppendAction("Retry preview", _ => { pair.Value.RemoveFromClassList("preview-failed"); if (waiting != null) waiting.text = "Rendering preview…"; }); }));
                        }
                    }
                    pair.Value.image = texture; if (texture) pair.Value.Q<Label>("previewWaiting")?.RemoveFromHierarchy();
                    yield return null;
                    if (screen != "Presets" || detail.panel == null) yield break;
                }
            }
            presetRendering = null;
        }
        void DrawPresetDetail(VisualElement detail, PresetEntry entry)
        {
            detail.Clear(); Label(detail, entry.name, "preset-detail-title");
            if (!entry.example)
            {
                Tool(detail, "Open", "folder", () => OpenPreset(entry), "primary preset-open preset-action").name = "presetOpen";
                Tool(detail, "Rename", "pen", () => RenamePreset(entry), "preset-rename preset-action").name = "presetRename";
                bool startup = store.LoadSettings().defaultCafeId == entry.cafeId;
                var setDefault = Tool(detail, startup ? "Startup cafe" : "Open at startup", startup ? "check" : "save", () =>
                {
                    SetDefaultCafe(entry.cafeId);
                    DrawPresetDetail(detail, entry);
                }, "preset-startup preset-action");
                setDefault.name = "presetSetDefault";
                setDefault.tooltip = startup ? "This saved cafe opens when BlueCafe starts" : "Open this saved cafe when BlueCafe starts";
                setDefault.SetEnabled(!startup);
            }
            Tool(detail, "Open as new cafe", "plus", () => OpenPreset(entry, true), entry.example ? "primary preset-open preset-action" : "preset-open preset-action").name = "presetOpenCopy";
            Tool(detail, "Export preset", "export", () => InputModal("Export preset", "JSON path", Path.Combine(appData, "Exports", SafePresetFilename(entry.name) + ".layout.json"), path => { DocumentStore.SaveLayout(path, entry.layout); Message("Exported " + path); }), "preset-export preset-action");
            var revealStatus = Label(detail, "", "preset-action-hint");
            revealStatus.style.display = DisplayStyle.None;
            Button reveal = null;
            reveal = Tool(detail, "Open in file manager", "folder", async () =>
            {
                reveal.SetEnabled(false); revealStatus.style.display = DisplayStyle.Flex; revealStatus.text = "Opening file manager…";
                try
                {
                    string path = PresetFilePath(entry);
                    bool selected = await NativeDialogs.RevealFile(path);
                    revealStatus.text = (selected ? "Revealed " : "Opened containing folder for ") + Path.GetFileName(path);
                    Message(revealStatus.text);
                }
                catch (Exception e) { revealStatus.text = e.Message; Message(e.Message); }
                finally { reveal.SetEnabled(true); }
            }, "preset-action");
            reveal.name = "presetRevealFile";
            reveal.tooltip = entry.example ? "Reveal a local copy of this bundled example's original JSON" : "Reveal this saved cafe's JSON file";
            Label(detail, "Opening prompts you to save unsaved work. Export leaves the current cafe open.", "preset-action-hint");
            Tool(detail, "Enlarge preview", "frame", () => EnlargePresetPreview(entry), "preset-action").name = "presetEnlargePreview";
            var facts = new Foldout { text = "Cafe details", value = false }; facts.AddToClassList("preset-detail-facts"); detail.Add(facts);
            PresetStat(facts, "Rooms", entry.layout.room.width + " × " + entry.layout.room.depth + " m"); PresetStat(facts, "Presets", entry.layout.instances.Count + " items");
            int unavailable = entry.layout.instances.Count(i => !Pack.Catalog.items.Any(p => p.id == i.furnitureId && p.available));
            PresetStat(facts, unavailable == 0 ? "check" : "warning", unavailable == 0 ? "All assets available" : unavailable + " items unavailable");
            var interior = Box(facts, "preset-interior"); Label(interior, "Interior", "subheading"); DrawInterior(interior, entry.layout, false);
        }
        string PresetFilePath(PresetEntry entry)
        {
            if (!entry.example)
            {
                if (!Guid.TryParseExact(entry.cafeId, "N", out _)) throw new InvalidOperationException("This saved preset identity is unavailable.");
                return Path.Combine(store.Root, "cafes", entry.cafeId + ".json");
            }
            var source = Resources.Load<TextAsset>("Examples/" + entry.exampleId);
            if (source == null) throw new FileNotFoundException("This bundled example is unavailable: " + entry.name);
            var bytes = source.bytes;
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var folder = Path.Combine(store.Root, "Examples"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, entry.exampleId + "-" + hash + ".layout.json");
            // Reuse the original copy, but preserve any local edits instead of overwriting them.
            if (File.Exists(path))
            {
                if (File.ReadAllBytes(path).SequenceEqual(bytes)) return path;
                path = Path.Combine(folder, entry.exampleId + "-" + hash + "-" + Guid.NewGuid().ToString("N") + ".layout.json");
            }
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes, 0, bytes.Length);
            return path;
        }
        void OpenPreset(PresetEntry entry, bool copy = false)
        {
            ReplacePrompt(() =>
            {
                if (!entry.example) { OpenCafe(entry.cafeId, copy); return; }
                var candidate = Copies.Copy(entry.layout); Placement.ValidateLayout(Pack.Catalog, candidate, true);
                var result = Session.OpenDocument(candidate); if (!result.success) throw new Exception(result.message);
                FinishReplacement(result); Refresh(); View.FrameRoom(); ShowScreen("Build");
            });
        }
        void RenamePreset(PresetEntry entry)
        {
            InputModal("Rename preset", "Cafe name", entry.name, name =>
            {
                var renamed = RenameSavedPreset(entry.cafeId, name);
                entry.name = renamed.name; entry.layout = renamed.layout;
                ShowScreen("Presets"); Message("Renamed to " + renamed.name + ".");
            });
        }
        void EnlargePresetPreview(PresetEntry entry)
        {
            var panel = Modal(entry.name); panel.AddToClassList("preset-preview-modal"); panel.name = "presetPreviewModal";
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, name = "presetEnlargedImage" }; image.AddToClassList("preset-preview-enlarged"); panel.Add(image);
            var loading = Label(image, "Rendering larger preview…", "preset-preview-waiting");
            var actions = Box(panel, "row"); Button(actions, "Close", CloseModal).name = "presetPreviewClose";
            Texture2D enlarged = null;
            panel.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == panel && enlarged) { Destroy(enlarged); enlarged = null; } });
            panel.schedule.Execute(() =>
            {
                if (panel.panel == null) return;
                try
                {
                    var cache = Path.Combine(appData, "previews", PresetPreviewKey(entry.layout) + "-large.png");
                    if (!File.Exists(cache)) using (var projection = new RuntimeView(Pack)) { projection.Sync(entry.layout, 0); projection.Camera.backgroundColor = new Color(.82f, .91f, .98f); projection.Capture(cache, 1600, 900); }
                    enlarged = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!enlarged.LoadImage(File.ReadAllBytes(cache))) throw new IOException("Cannot decode rendered preview.");
                    image.image = enlarged; loading.RemoveFromHierarchy();
                }
                catch (Exception e) { if (enlarged) { Destroy(enlarged); enlarged = null; } loading.text = "Preview unavailable: " + e.Message; }
            });
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
