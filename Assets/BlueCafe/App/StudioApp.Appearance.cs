using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        Task<bool> systemAppearance;
        float nextThemeCheck;
        bool systemDark;
        void InitializeTheme() { ApplyTheme(); PollSystemTheme(); }
        void AppearanceSettings(VisualElement parent)
        {
            var section = Box(parent, "settings-panel settings-appearance");
            Label(section, "Appearance", "settings-title");
            var choices = new System.Collections.Generic.List<string> { "Light", "Dark", "System" };
            if (!choices.Contains(preferences.theme)) preferences.theme = "System";
            var picker = StudioDropdown.Create<string>(choices, choices.IndexOf(preferences.theme)); picker.name = "appearanceTheme"; picker.AddToClassList("settings-theme-picker");
            section.Add(picker); picker.RegisterValueChangedCallback(e => { preferences.theme = e.newValue; ApplyTheme(); SaveViewPreferences(); PollSystemTheme(); });
            var diagnostics = new Toggle { text = "Show revision and render diagnostics", name = "showDiagnostics", value = showDiagnostics }; section.Add(diagnostics);
            diagnostics.RegisterValueChangedCallback(e => { showDiagnostics = e.newValue; preferences.diagnostics = e.newValue; SaveViewPreferences(); if (Session != null) Refresh(); });
        }
        void ApplyTheme()
        {
            bool dark = preferences.theme == "Dark" || preferences.theme == "System" && systemDark;
            root.EnableInClassList("theme-dark", dark);
            var host = root.panel?.visualTree;
            if (host != null)
            {
                host.AddToClassList("studio-theme-host"); host.EnableInClassList("theme-dark", dark);
                var sheet = Resources.Load<StyleSheet>("Themes"); if (sheet != null && !host.styleSheets.Contains(sheet)) host.styleSheets.Add(sheet);
            }
        }
        void PollSystemTheme()
        {
            if (systemAppearance != null && systemAppearance.IsCompleted)
            {
                if (systemAppearance.Status == TaskStatus.RanToCompletion) systemDark = systemAppearance.Result;
                systemAppearance = null; ApplyTheme();
            }
            if (preferences.theme != "System" || systemAppearance != null || Time.realtimeSinceStartup < nextThemeCheck) return;
            nextThemeCheck = Time.realtimeSinceStartup + 5;
            systemAppearance = Task.Run(() => ReadSystemDark());
        }
        static string AppearanceCommand(string command, string arguments)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
                {
                    if (!process.WaitForExit(800)) { process.Kill(); return ""; }
                    return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim().Trim('\'', '"') : "";
                }
            }
            catch (Exception) { return ""; }
        }
        static bool ReadSystemDark()
        {
#if UNITY_STANDALONE_WIN
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
#elif UNITY_STANDALONE_OSX
            return AppearanceCommand("defaults", "read -g AppleInterfaceStyle").Equals("Dark", StringComparison.OrdinalIgnoreCase);
#else
            var preference = AppearanceCommand("gsettings", "get org.gnome.desktop.interface color-scheme");
            if (preference == "prefer-dark") return true;
            if (preference == "prefer-light") return false;
            var kde = AppearanceCommand("kreadconfig6", "--file kdeglobals --group General --key ColorScheme");
            return kde.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0 || AppearanceCommand("gsettings", "get org.gnome.desktop.interface gtk-theme").IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
#endif
        }
    }
}
