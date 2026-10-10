using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    // Keep PopupField's value, index and binding contract; delegate the popup to
    // Unity's public menu implementation with content fitting enabled.
    internal static class StudioDropdown
    {
        public static PopupField<T> Create<T>(List<T> choices, int index)
        {
            var field = new PopupField<T>(choices, index);
            GenericDropdownMenu openMenu = null;
            void Open()
            {
                if (!field.enabledInHierarchy || field.panel == null || openMenu?.contentContainer.panel != null) return;
                var input = field.Q<VisualElement>(className: BasePopupField<T, T>.inputUssClassName);
                var text = field.Q<TextElement>(className: BasePopupField<T, T>.textUssClassName);
                var menu = new GenericDropdownMenu();
                foreach (var choice in field.choices)
                {
                    var selected = choice;
                    string label = field.formatListItemCallback?.Invoke(choice) ?? (choice == null ? "" : choice.ToString());
                    menu.AddItem(label, EqualityComparer<T>.Default.Equals(choice, field.value) && !field.showMixedValue, () => field.value = selected);
                }
                Prepare(menu);
                // Measure using the field's attached font, rather than a detached
                // menu label. A narrow closed field must not constrain its menu.
                float labelWidth = 0;
                foreach (var label in menu.contentContainer.Query<Label>(className: GenericDropdownMenu.labelUssClassName).ToList())
                    labelWidth = Mathf.Max(labelWidth, text.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x);
                float maxLabelWidth = Mathf.Max(32, field.panel.visualTree.worldBound.width - 96);
                foreach (var label in menu.contentContainer.Query<Label>(className: GenericDropdownMenu.labelUssClassName).ToList())
                {
                    label.style.minWidth = Mathf.Min(Mathf.Ceil(labelWidth) + 12, maxLabelWidth);
                    label.EnableInClassList("studio-dropdown-label-wrapped", Mathf.Ceil(labelWidth) + 12 > maxLabelWidth);
                    label.tooltip = label.text;
                }
                field.Focus(); openMenu = menu;
                menu.DropDown((input ?? field).worldBound, field, true, true);
            }
            field.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; Open(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            field.RegisterCallback<PointerMoveEvent>(e => { if (e.button != 0 || (e.pressedButtons & 1) == 0) return; Open(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(e => { Open(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Space) return; Open(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            return field;
        }
        public static bool IsMenuTarget(object target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element.ClassListContains(GenericDropdownMenu.ussClassName)) return true;
            return false;
        }
        public static bool DismissTopMenu(VisualElement root)
        {
            var menuRoot = root.panel?.visualTree ?? root;
            var menus = menuRoot.Query<VisualElement>(className: GenericDropdownMenu.ussClassName).ToList();
            if (menus.Count == 0) return false;
            var content = menus[menus.Count - 1].Q<ScrollView>()?.contentContainer;
            if (content == null) return false;
            using (var cancel = NavigationCancelEvent.GetPooled()) content.SendEvent(cancel);
            return true;
        }
        public static void Prepare(GenericDropdownMenu menu)
        {
            var scroll = menu.contentContainer.GetFirstAncestorOfType<ScrollView>();
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            var container = scroll.parent.parent;
            container.AddToClassList("studio-dropdown-menu");
            container.styleSheets.Add(Resources.Load<StyleSheet>("Dropdowns"));
        }
    }
}
