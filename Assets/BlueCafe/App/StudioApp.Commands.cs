using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        (string label, Action action)[] Commands() => new[] {
            ("Save cafe · Ctrl+S", (Action)Save), ("Undo · Ctrl+Z", (Action)(() => Apply(Session.Undo(), false))),
            ("Redo · Ctrl+Shift+Z", (Action)(() => Apply(Session.Redo(), false))),
            ("Rotate selection · R", (Action)(() => RotateSelection())),
            ("Duplicate selection · Ctrl+D", (Action)(() => Apply(Session.Duplicate(selection)))),
            ("Delete selection · Delete", (Action)(() => Apply(Session.Delete(selection)))),
            ("Focus Catalog search · Ctrl+F", (Action)FocusCatalogSearch),
            ("Fit room", (Action)(() => View?.FrameRoom())),
            ("Toggle God’s-eye view", (Action)(() => { CancelGesture(); View?.ToggleTopDown(); RefreshBuildTools(); })),
            ("Area select furniture", (Action)(() => { if (screen != "Build") ShowScreen("Build"); SetAreaMode(true); })),
            ("Toggle Catalog", (Action)(() => TogglePanel(true))), ("Toggle Inspector", (Action)(() => TogglePanel(false))),
            ("Reset toolbar position", (Action)ResetToolbar),
            ("Build workspace", (Action)(() => ShowScreen("Build"))), ("Rooms workspace", (Action)(() => ShowScreen("Rooms"))),
            ("Assets workspace", (Action)(() => ShowScreen("Assets"))), ("Presets workspace", (Action)(() => ShowScreen("Presets"))),
            ("Import layout", (Action)Import), ("Export layout", (Action)ExportLayout), ("Export image", (Action)ExportImage)
        };
        void FocusCatalogSearch() { if (screen != "Build" && screen != "Assets") ShowScreen("Build"); if (preferences.catalogCollapsed && screen == "Build") TogglePanel(true); search?.Focus(); }
        void CommandPalette()
        {
            var panel = Modal("Commands"); panel.name = "commandPalette";
            var scroll = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.style.maxHeight = 430;
            void Fill(string query)
            {
                scroll.Clear(); var matches = Commands().Where(c => c.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                foreach (var command in matches) { var b = Button(scroll, command.label, () => { CloseModal(); command.action(); }, "command-result"); b.SetEnabled(CommandEnabled(command.label)); }
                if (matches.Length == 0) Label(scroll, "No matching commands.", "muted");
            }
            var query = SearchField(panel, "Search commands", Fill); query.name = "commandSearch"; panel.Add(scroll); Button(panel, "Close", CloseModal); Fill(""); query.Focus();
        }
        void ControlsHelp()
        {
            var p = Modal("Cafe controls");
            Label(p, "Browse and navigate", "subheading"); Label(p, "Ctrl+F focuses Catalog search. Ctrl+K opens searchable commands. Tab moves focus; Enter activates the focused item or action.", "message");
            Label(p, "Furniture", "subheading"); Label(p, "Click or drag original catalog items into the cafe. Drag furniture to move; Shift adds to selection. Area select draws a rectangle on the floor; Shift adds the group. Release to return to normal selection and move the group. R rotates, Ctrl+D duplicates, Delete removes. Right-click opens furniture actions; right-drag pans. Hold the middle mouse button and drag to orbit around the cafe. Scroll zooms. God’s-eye view looks straight down; Oblique view restores the previous camera.", "message");
            Label(p, "Document", "subheading"); Label(p, "Ctrl+Z undoes, Ctrl+Shift+Z redoes, Ctrl+S saves. Escape cancels the current menu, dialog or gesture. Text fields keep their normal editing shortcuts.", "message");
            Label(p, "Workspace", "subheading"); Label(p, "Drag panel dividers or focus them and use arrow keys; Enter collapses. The toolbar grip supports arrow keys and Home to reset. Minimized toolbar keeps keyboard commands available.", "message");
            Button(p, "Close", CloseModal, "primary");
        }
        void FurnitureMenu(Vector2 position)
        {
            string id = View.Pick(viewport.WorldToLocal(position), new Rect(Vector2.zero, viewport.contentRect.size));
            if (id == null) return;
            if (!selection.Contains(id)) { selection.Clear(); selection.Add(id); RefreshInspector(); }
            var menu = new GenericDropdownMenu();
            menu.AddItem("Rotate clockwise · R", false, () => Safe(() => RotateSelection()));
            menu.AddItem("Duplicate · Ctrl+D", false, () => Safe(() => Apply(Session.Duplicate(selection))));
            menu.AddItem("Delete · Delete", false, () => Safe(() => Apply(Session.Delete(selection))));
            var instance = Session.Layout.instances.Single(i => i.instanceId == id); var item = BuildItem(instance.furnitureId);
            menu.AddSeparator(""); menu.AddItem("Copy asset ID and name", false, () => GUIUtility.systemCopyBuffer = item.id + " · " + item.name);
            StudioDropdown.Prepare(menu); menu.DropDown(new Rect(position, Vector2.one), viewport, false, false);
        }
        bool GlobalKey(KeyDownEvent e)
        {
            if (!(e.ctrlKey || e.commandKey)) return false;
            if (e.keyCode == KeyCode.K) { CommandPalette(); e.StopImmediatePropagation(); return true; }
            if (e.keyCode == KeyCode.F) { FocusCatalogSearch(); e.StopImmediatePropagation(); return true; }
            if (e.keyCode == KeyCode.S)
            {
                // Blur delayed number fields first so their validation can reject an invalid
                // transform; saving then records the last valid committed document.
                var focused = root.panel?.focusController.focusedElement as VisualElement; focused?.Blur();
                root.schedule.Execute(() => Save()); e.StopImmediatePropagation(); return true;
            }
            return false;
        }
    }
}
