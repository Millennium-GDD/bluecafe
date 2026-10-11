#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using BlueCafe.Agent;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        readonly List<float> measuredFrames = new List<float>();
        bool measureFrames;
        readonly List<double> previewToRenderMilliseconds = new List<double>();
        bool measurePreviews;
        long previewStarted;
        void PreviewInputStarted() { if (measurePreviews && previewStarted == 0) previewStarted = System.Diagnostics.Stopwatch.GetTimestamp(); }
        void PreviewRendered()
        {
            if (!measurePreviews || previewStarted == 0) return;
            previewToRenderMilliseconds.Add((System.Diagnostics.Stopwatch.GetTimestamp() - previewStarted) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            previewStarted = 0;
        }
        Response ProbeBeta(Arguments a)
        {
            if (a.action == "select-text") { root.Q<TextField>(a.name).SelectAll(); return ProbeReply(); }
            if (a.action == "image-state")
            {
                var img = root.Q<Image>(a.name); if (img == null) throw new Exception("Unknown image " + a.name);
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new ImageProbe { bounds = img.worldBound, width = img.image ? img.image.width : 0, height = img.image ? img.image.height : 0 }) };
            }
            if (a.action == "control-style-state")
            {
                var field = root.Q<TextField>(a.name);
                var picker = root.Q<VisualElement>("appearanceTheme");
                var diagnostics = root.Q<Toggle>("showDiagnostics");
                var scroll = root.Q<ListView>("assetsList")?.Q<ScrollView>();
                var slider = scroll?.verticalScroller.Q<VisualElement>(className: "unity-scroller__slider");
                var input = slider?.Q<VisualElement>(className: "unity-base-field__input");
                var thumb = slider?.Q<VisualElement>(className: "unity-base-slider__dragger");
                Color TextColor(string property)
                {
                    var textInput = field?.Q<VisualElement>(className: "unity-base-text-field__input");
                    return textInput != null && textInput.customStyle.TryGetValue(new CustomStyleProperty<Color>(property), out var color) ? color : Color.clear;
                }
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new ControlStyleState {
                    picker = picker?.worldBound ?? Rect.zero, diagnostics = diagnostics?.worldBound ?? Rect.zero,
                    toggleInput = diagnostics?.Q<VisualElement>(className: "unity-base-field__input")?.resolvedStyle.backgroundColor ?? Color.clear,
                    sliderInput = input?.resolvedStyle.backgroundColor ?? Color.clear, sliderBorder = input?.resolvedStyle.borderLeftWidth ?? 0,
                    thumb = thumb?.worldBound ?? Rect.zero, thumbColor = thumb?.resolvedStyle.backgroundColor ?? Color.clear,
                    cursor = TextColor("--unity-cursor-color"), selection = TextColor("--unity-selection-color"),
                    icons = root.Query<StudioIcon>().ToList().Select(i => i.Kind).ToArray()
                }) };
            }
            if (a.action == "beta-state")
            {
                var data = new BetaState { theme = preferences.theme, dark = root.ClassListContains("theme-dark"), preferences = JsonUtility.ToJson(preferences),
                    canvas = buildCanvas?.worldBound ?? Rect.zero, catalog = catalogPanel?.worldBound ?? Rect.zero, inspector = inspectorPanel?.worldBound ?? Rect.zero, toolbar = floatingToolbar?.worldBound ?? Rect.zero,
                    controls = root.Query<Button>().ToList().Select(b => new BetaControl { name = b.name, text = b.text, tooltip = b.tooltip, enabled = b.enabledInHierarchy, bounds = b.worldBound }).ToArray(),
                    camera = View.Camera.transform.position, size = View.Camera.orthographicSize };
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(data) };
            }
            if (a.action == "profile-start") { measuredFrames.Clear(); measureFrames = true; return ProbeReply(); }
            if (a.action == "preview-profile-start") { previewToRenderMilliseconds.Clear(); previewStarted = 0; measurePreviews = true; return ProbeReply(); }
            if (a.action == "preview-profile-stop") { measurePreviews = false; var samples = previewToRenderMilliseconds.ToArray(); var sorted = samples.OrderBy(value => value).ToArray(); return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new BetaPreviewPerformance { samples = samples, p95ms = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))] }) }; }
            if (a.action == "profile-stop") { measureFrames = false; var samples = measuredFrames.ToArray(); Array.Sort(samples); return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new BetaPerformance { samples = samples, frames = samples.Length, fps = samples.Length / Math.Max(.0001f, samples.Sum()), p95ms = samples.Length == 0 ? 0 : samples[Math.Min(samples.Length - 1, (int)(samples.Length * .95f))] * 1000 }) }; }
            if (a.action == "beta-click") { var button = root.Q<Button>(a.name); if (button == null || !button.enabledInHierarchy) throw new Exception("Unavailable button " + a.name); button.Focus(); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit); return ProbeReply(); }
            if (a.action == "canvas-pan") { View.Pan(new Vector2(a.dx, a.dz)); return ProbeReply(); }
            if (a.action == "canvas-zoom") { View.Zoom(a.x); return ProbeReply(); }
            if (a.action == "copy-buffer") return new Response { success = true, revision = Session.Revision, json = GUIUtility.systemCopyBuffer };
            if (a.action == "element-state") { var element = root.Q<VisualElement>(a.name); if (element == null) throw new Exception("Unknown element " + a.name); return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new BetaElement { bounds = element.worldBound, background = ColorUtility.ToHtmlStringRGBA(element.resolvedStyle.backgroundColor), display = element.resolvedStyle.display.ToString() }) }; }
            return null;
        }
        [Serializable] sealed class BetaControl { public string name, text, tooltip; public bool enabled; public Rect bounds; }
        [Serializable] sealed class BetaState { public string theme, preferences; public bool dark; public Rect canvas, catalog, inspector, toolbar; public Vector3 camera; public float size; public BetaControl[] controls; }
        [Serializable] sealed class BetaPerformance { public float[] samples; public int frames; public float fps, p95ms; }
        [Serializable] sealed class BetaPreviewPerformance { public double[] samples; public double p95ms; }
        [Serializable] sealed class BetaElement { public Rect bounds; public string background, display; }
        [Serializable] sealed class ImageProbe { public Rect bounds; public int width, height; }
        [Serializable] sealed class ControlStyleState { public Rect picker, diagnostics, thumb; public Color toggleInput, sliderInput, thumbColor, cursor, selection; public float sliderBorder; public string[] icons; }
    }
}
#endif
