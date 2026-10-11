using System;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        [Serializable]
        public sealed class BrowseContext
        {
            public string query = "", category = "", subcategory = "", theme = "", selectedId, focusName, sortColumn = "ID";
            public bool inCafe, ascending = true;
            public float scroll;
            public int limit = 60;
        }
        BrowseContext buildBrowsing = new BrowseContext { category = "Furniture" }, assetsBrowsing = new BrowseContext();
        float myCafesScroll, examplesScroll;
        bool presetDetailsVisible = true;
        bool showDiagnostics;
        string myCafeSelection, exampleSelection;

        static int ValidChoice(System.Collections.Generic.List<string> choices, string value) => Math.Max(0, choices.IndexOf(value ?? ""));
        void CaptureBrowsingContext()
        {
            if (screen == "Build" && search != null)
            {
                buildBrowsing.query = search.value; buildBrowsing.category = topCategory; buildBrowsing.subcategory = category?.value ?? "All"; buildBrowsing.theme = theme?.value ?? "All";
                buildBrowsing.inCafe = inCafe?.value ?? false; buildBrowsing.limit = limit;
                buildBrowsing.scroll = workspace.Q<ScrollView>(className: "build-card-scroll")?.scrollOffset.y ?? 0;
                buildBrowsing.selectedId = armed;
                buildBrowsing.focusName = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            }
            else if (screen == "Assets")
            {
                assetsBrowsing.scroll = workspace.Q<ListView>("assetsList")?.Q<ScrollView>()?.scrollOffset.y ?? assetsBrowsing.scroll;
                var focus = root.panel?.focusController.focusedElement as VisualElement;
                assetsBrowsing.focusName = focus?.GetFirstAncestorOfType<ListView>() != null ? "assetsList" : focus?.name;
            }
            else if (screen == "Presets")
            {
                var offset = workspace.Q<ScrollView>(className: "preset-gallery-scroll")?.scrollOffset.y ?? 0;
                if (presetTab == "Examples") { examplesScroll = offset; exampleSelection = selectedPresetKey; }
                else { myCafesScroll = offset; myCafeSelection = selectedPresetKey; }
            }
        }
        void RestoreBuildBrowsingContext()
        {
            if (search == null || Pack == null) return;
            topCategory = buildBrowsing.category == "Decoration" ? "Decoration" : "Furniture";
            UpdateBuildCategories(); search.SetValueWithoutNotify(buildBrowsing.query ?? "");
            category.SetValueWithoutNotify(category.choices[ValidChoice(category.choices, buildBrowsing.subcategory)]);
            theme.SetValueWithoutNotify(theme.choices[ValidChoice(theme.choices, buildBrowsing.theme)]);
            inCafe.SetValueWithoutNotify(buildBrowsing.inCafe); inCafe.EnableInClassList("build-switch-on", inCafe.value);
            limit = Math.Max(60, buildBrowsing.limit);
            armed = Pack.Catalog.items.Any(i => i.id == buildBrowsing.selectedId && i.available && !Placement.IsRoomStyle(i)) ? buildBrowsing.selectedId : null;
            RefreshCards();
            var scroll = workspace.Q<ScrollView>(className: "build-card-scroll");
            if (scroll != null) RestoreBrowseScroll(scroll, buildBrowsing.scroll, () => { if (!string.IsNullOrEmpty(buildBrowsing.focusName)) workspace.Q<VisualElement>(buildBrowsing.focusName)?.Focus(); });
        }
        void RestoreBrowseScroll(ScrollView scroll, float desiredOffset, Action restored = null)
        {
            // Collection geometry is not ready when a workspace is first built.
            // Keep a detached anchor until the scroller has its actual content range.
            bool done = false; int attempts = 0;
            scroll.schedule.Execute(() =>
            {
                if (scroll.panel == null || scroll.contentViewport.contentRect.height <= 0) return;
                attempts++;
                if (desiredOffset > 0 && scroll.verticalScroller.highValue <= 0 && attempts < 12) return;
                scroll.scrollOffset = new Vector2(0, Mathf.Clamp(desiredOffset, 0, Mathf.Max(0, scroll.verticalScroller.highValue)));
                done = true; restored?.Invoke();
            }).Every(16).Until(() => done);
        }
        void ResetBuildFilters()
        {
            buildBrowsing = new BrowseContext { category = "Furniture" };
            RestoreBuildBrowsingContext();
        }
        void CopyText(string text) { GUIUtility.systemCopyBuffer = text ?? ""; Message("Copied to clipboard."); }
        void CopyAssetDetails(VisualElement card, Item item)
        {
            card.AddManipulator(new ContextualMenuManipulator(e => { e.menu.AppendAction("Copy ID", _ => CopyText(item.id)); e.menu.AppendAction("Copy name", _ => CopyText(item.name)); e.menu.AppendAction("Copy asset details", _ => CopyText(item.id + "\n" + item.name + "\n" + Pack.Availability(item.id).Reason)); }));
        }

        string operationStatus = "No verification running.";
        Label operationStatusLabel;
        void UpdateOperationStatus(string text)
        {
            if (text.StartsWith("Verifying prepared asset files", StringComparison.Ordinal) || text.StartsWith("Asset folder:", StringComparison.Ordinal) || text.StartsWith("Asset verification", StringComparison.Ordinal) || text.Contains("assets available. Source files"))
                operationStatus = text;
            if (operationStatusLabel?.panel != null) operationStatusLabel.text = operationStatus;
        }
        void AddOperationStatus(VisualElement parent)
        {
            var row = Box(parent, "operation-status-row"); operationStatusLabel = Label(row, operationStatus, "operation-status-copy"); operationStatusLabel.name = "packOperationStatus";
            var copy = Tool(row, "Copy details", "duplicate", () => CopyText(operationStatus)); copy.name = modal != null && modal.Contains(parent) ? "operationCopyModal" : "operationCopySettings";
            var cancel = Tool(row, "Cancel verification", "close", () => packLoad?.Cancel()); cancel.name = "packCancelVerification";
            row.schedule.Execute(() => { cancel.EnableInClassList("hidden", !busy); operationStatusLabel.text = operationStatus; }).Every(200);
        }
        void ShowOperationStatus()
        {
            var panel = Modal("Asset verification"); AddOperationStatus(panel); Button(panel, "Close", CloseModal);
        }
    }
}
