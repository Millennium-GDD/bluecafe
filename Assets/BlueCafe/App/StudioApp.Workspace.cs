using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        [Serializable] sealed class ViewPreferences
        {
            public int version = 1;
            public string theme = "System";
            public float catalogWidth = 270, inspectorWidth = 270, toolbarX = .5f, toolbarY = .02f;
            public bool catalogCollapsed, inspectorCollapsed, toolbarMinimized, roomExpanded, interiorExpanded, diagnostics;
        }
        ViewPreferences preferences = new ViewPreferences();
        VisualElement buildCanvas, catalogPanel, inspectorPanel, catalogDivider, inspectorDivider, floatingToolbar, toolbarCommands;
        Button toolbarMinimize, catalogToggle, inspectorToggle;
        string preferencePath => Path.Combine(appData, "view-preferences.json");
        void LoadViewPreferences()
        {
            try { if (File.Exists(preferencePath)) preferences = JsonUtility.FromJson<ViewPreferences>(File.ReadAllText(preferencePath)) ?? new ViewPreferences(); }
            catch (Exception) { preferences = new ViewPreferences(); }
            if (preferences.version != 1) preferences = new ViewPreferences();
            showDiagnostics = preferences.diagnostics;
            preferences.catalogWidth = FiniteWidth(preferences.catalogWidth);
            preferences.inspectorWidth = FiniteWidth(preferences.inspectorWidth);
            preferences.toolbarX = float.IsNaN(preferences.toolbarX) || float.IsInfinity(preferences.toolbarX) ? .5f : Mathf.Clamp01(preferences.toolbarX);
            preferences.toolbarY = float.IsNaN(preferences.toolbarY) || float.IsInfinity(preferences.toolbarY) ? .02f : Mathf.Clamp01(preferences.toolbarY);
        }
        static float FiniteWidth(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 270 : Mathf.Clamp(value, 230, 440);
        void SaveViewPreferences()
        {
            string temporary = preferencePath + ".tmp";
            try
            {
                Directory.CreateDirectory(appData);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) { writer.Write(JsonUtility.ToJson(preferences, true)); writer.Flush(); stream.Flush(true); }
                if (File.Exists(preferencePath)) File.Replace(temporary, preferencePath, null); else File.Move(temporary, preferencePath);
            }
            catch (Exception e) { Message("View preferences could not be saved: " + e.Message); }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { } }
        }
        void ConfigureBuildPanels(VisualElement row, VisualElement catalog, VisualElement canvas, VisualElement detail)
        {
            catalogPanel = catalog; inspectorPanel = detail; buildCanvas = canvas;
            canvas.style.overflow = Overflow.Hidden;
            catalogDivider = Divider(row, catalog, true); inspectorDivider = Divider(row, detail, false);
            row.Insert(row.IndexOf(catalog) + 1, catalogDivider); row.Insert(row.IndexOf(detail), inspectorDivider);
            catalogToggle = IconButton(canvas, "left", () => TogglePanel(true), "Collapse Catalog", "panel-edge-toggle panel-edge-left"); catalogToggle.name = "toggleCatalog";
            inspectorToggle = IconButton(canvas, "right", () => TogglePanel(false), "Collapse Inspector", "panel-edge-toggle panel-edge-right"); inspectorToggle.name = "toggleInspector";
            row.RegisterCallback<GeometryChangedEvent>(_ => ApplyPanelSizes()); ApplyPanelSizes();
        }
        VisualElement Divider(VisualElement row, VisualElement panel, bool left)
        {
            var divider = new VisualElement { name = left ? "catalogDivider" : "inspectorDivider", focusable = true, tooltip = "Drag to resize. Arrow keys resize; Enter collapses." };
            divider.AddToClassList("panel-divider"); int captured = -1; float start = 0, width = 0;
            divider.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; CancelGesture(); captured = e.pointerId; start = e.position.x; width = left ? preferences.catalogWidth : preferences.inspectorWidth; divider.CapturePointer(captured); e.StopPropagation(); });
            divider.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId != captured) return; var value = FiniteWidth(width + (e.position.x - start) * (left ? 1 : -1)); if (left) preferences.catalogWidth = value; else preferences.inspectorWidth = value; ApplyPanelSizes(); e.StopPropagation(); });
            divider.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId != captured) return; captured = -1; divider.ReleasePointer(e.pointerId); SaveViewPreferences(); e.StopPropagation(); });
            divider.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) TogglePanel(left); else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow) { float delta = e.keyCode == KeyCode.LeftArrow ? -10 : 10; if (left) preferences.catalogWidth = FiniteWidth(preferences.catalogWidth + delta); else preferences.inspectorWidth = FiniteWidth(preferences.inspectorWidth - delta); ApplyPanelSizes(); SaveViewPreferences(); } else return; e.StopPropagation(); });
            return divider;
        }
        void TogglePanel(bool left)
        {
            if (left) preferences.catalogCollapsed = !preferences.catalogCollapsed;
            else if (screen == "Build" && inspectorPanel?.resolvedStyle.display == DisplayStyle.None)
            {
                preferences.inspectorCollapsed = false;
                if (workspace.contentRect.width < 930) preferences.catalogCollapsed = true;
            }
            else preferences.inspectorCollapsed = !preferences.inspectorCollapsed;
            ApplyPanelSizes(); SaveViewPreferences();
        }
        void ApplyPanelSizes()
        {
            if (screen != "Build" || catalogPanel?.panel == null || inspectorPanel?.panel == null) return;
            float available = workspace.contentRect.width;
            bool narrow = available < 930;
            bool hideLeft = preferences.catalogCollapsed, hideRight = preferences.inspectorCollapsed || narrow && !hideLeft;
            catalogPanel.style.display = hideLeft ? DisplayStyle.None : DisplayStyle.Flex;
            inspectorPanel.style.display = hideRight ? DisplayStyle.None : DisplayStyle.Flex;
            catalogDivider.style.display = hideLeft ? DisplayStyle.None : DisplayStyle.Flex;
            inspectorDivider.style.display = hideRight ? DisplayStyle.None : DisplayStyle.Flex;
            float budget = Mathf.Max(230, available - 370 - 12);
            float total = (hideLeft ? 0 : preferences.catalogWidth) + (hideRight ? 0 : preferences.inspectorWidth);
            float factor = total > budget ? budget / total : 1;
            catalogPanel.style.width = Mathf.Max(230, preferences.catalogWidth * factor); inspectorPanel.style.width = Mathf.Max(230, preferences.inspectorWidth * factor);
            UpdatePanelToggle(catalogToggle, hideLeft, true);
            UpdatePanelToggle(inspectorToggle, hideRight, false);
            ClampToolbar();
        }
        static void UpdatePanelToggle(Button button, bool collapsed, bool left)
        {
            if (button == null) return;
            button.tooltip = (collapsed ? "Expand " : "Collapse ") + (left ? "Catalog" : "Inspector");
            button.Q<StudioIcon>().Kind = collapsed == left ? "right" : "left";
        }
        void ConfigureFloatingToolbar(VisualElement floating, VisualElement commands)
        {
            floatingToolbar = floating; toolbarCommands = commands;
            floating.name = "selectionToolbar"; floating.pickingMode = PickingMode.Position;
            floating.style.left = 0; floating.style.right = StyleKeyword.Auto; floating.style.alignItems = Align.FlexStart;
            var grip = new Button { text = "::", name = "toolbarGrip", tooltip = "Move selection toolbar. Arrow keys move; Home resets.", focusable = true };
            grip.AddToClassList("toolbar-grip"); commands.Insert(0, grip);
            toolbarMinimize = Button(commands, "−", () => { preferences.toolbarMinimized = !preferences.toolbarMinimized; ApplyToolbarState(); SaveViewPreferences(); }, tooltip: "Minimize or restore selection toolbar"); toolbarMinimize.name = "toolbarMinimize"; commands.Insert(1, toolbarMinimize);
            int captured = -1; Vector2 start = default, origin = default;
            grip.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; captured = e.pointerId; start = e.position; origin = floating.layout.position; grip.Focus(); grip.CapturePointer(captured); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            grip.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId != captured) return; SetToolbarPosition(origin + (Vector2)e.position - start); e.StopPropagation(); });
            grip.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId != captured) return; captured = -1; grip.ReleasePointer(e.pointerId); SaveViewPreferences(); e.StopPropagation(); });
            grip.RegisterCallback<KeyDownEvent>(e => { var delta = Vector2.zero; if (e.keyCode == KeyCode.Home) ResetToolbar(); else if (e.keyCode == KeyCode.LeftArrow) delta.x = -12; else if (e.keyCode == KeyCode.RightArrow) delta.x = 12; else if (e.keyCode == KeyCode.UpArrow) delta.y = -12; else if (e.keyCode == KeyCode.DownArrow) delta.y = 12; else return; if (delta != Vector2.zero) { SetToolbarPosition(floating.layout.position + delta); SaveViewPreferences(); } e.StopPropagation(); });
            floating.RegisterCallback<GeometryChangedEvent>(_ => ClampToolbar()); buildCanvas.RegisterCallback<GeometryChangedEvent>(_ => ClampToolbar()); ApplyToolbarState();
        }
        void ApplyToolbarState()
        {
            if (toolbarCommands == null) return;
            foreach (var child in toolbarCommands.Children()) child.style.display = preferences.toolbarMinimized && child != buildSelectTool && child.name != "toolbarGrip" && child != toolbarMinimize ? DisplayStyle.None : DisplayStyle.Flex;
            toolbarMinimize.text = preferences.toolbarMinimized ? "+" : "−"; ClampToolbar();
        }
        void SetToolbarPosition(Vector2 point)
        {
            if (buildCanvas == null || floatingToolbar == null) return;
            float dx = Mathf.Max(0, buildCanvas.contentRect.width - floatingToolbar.layout.width), dy = Mathf.Max(0, buildCanvas.contentRect.height - floatingToolbar.layout.height);
            preferences.toolbarX = dx > 0 ? Mathf.Clamp01(point.x / dx) : 0; preferences.toolbarY = dy > 0 ? Mathf.Clamp01(point.y / dy) : 0; ClampToolbar();
        }
        void ClampToolbar()
        {
            if (floatingToolbar == null || buildCanvas == null || screen != "Build") return;
            float width = Mathf.Max(0, buildCanvas.contentRect.width), height = Mathf.Max(0, buildCanvas.contentRect.height);
            if (width < 1 || height < 1 || float.IsNaN(floatingToolbar.layout.width) || float.IsNaN(floatingToolbar.layout.height)) return;
            float toolbarWidth = preferences.toolbarMinimized ? Mathf.Min(220, width - 12) : Mathf.Min(390, width - 12);
            if (Mathf.Abs(floatingToolbar.resolvedStyle.width - toolbarWidth) > .1f) floatingToolbar.style.width = toolbarWidth;
            toolbarCommands.style.width = Length.Percent(100);
            float x = Mathf.Max(0, width - toolbarWidth) * preferences.toolbarX, y = Mathf.Max(0, height - floatingToolbar.layout.height) * preferences.toolbarY;
            if (Mathf.Abs(floatingToolbar.resolvedStyle.left - x) > .1f) floatingToolbar.style.left = x;
            if (Mathf.Abs(floatingToolbar.resolvedStyle.top - y) > .1f) floatingToolbar.style.top = y;
        }
        void ResetToolbar() { preferences.toolbarX = .5f; preferences.toolbarY = .02f; preferences.toolbarMinimized = false; ApplyToolbarState(); SaveViewPreferences(); }
        Foldout InspectorFoldout(VisualElement parent, string text, bool interior)
        {
            var foldout = new Foldout { text = text, value = interior ? preferences.interiorExpanded : preferences.roomExpanded }; foldout.AddToClassList("build-section"); parent.Add(foldout);
            foldout.RegisterValueChangedCallback(e => { if (interior) preferences.interiorExpanded = e.newValue; else preferences.roomExpanded = e.newValue; SaveViewPreferences(); }); return foldout;
        }
        bool CommandEnabled(string label)
        {
            if (Session == null) return false;
            if (label.StartsWith("Undo", StringComparison.Ordinal)) return Session.CanUndo;
            if (label.StartsWith("Redo", StringComparison.Ordinal)) return Session.CanRedo;
            if (label.StartsWith("Rotate", StringComparison.Ordinal) || label.StartsWith("Duplicate", StringComparison.Ordinal) || label.StartsWith("Delete", StringComparison.Ordinal)) return selection.Count > 0;
            return true;
        }
        void RefreshCommandStates()
        {
            if (root == null) return;
            foreach (var button in root.Query<Button>().ToList())
                if (button.name == "commandUndo" || button.name == "commandRedo" || button.name == "buildRotate" || button.name == "buildDuplicate" || button.name == "buildDelete") button.SetEnabled(CommandEnabled(button.tooltip ?? button.text));
        }
    }
}
