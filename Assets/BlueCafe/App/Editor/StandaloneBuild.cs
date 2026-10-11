using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BlueCafe.App.Editor
{
    public static class StandaloneBuild
    {
        public const string ScenePath = "Assets/BlueCafe/App/Bootstrap.unity";
        public const string Output = "Builds/Linux/BlueCafe.x86_64";
        [MenuItem("BlueCafe/Build Linux runtime spine")]
        public static void Build() => BuildTo(Output, "evidence/standalone-spine-2026-10-10");
        public static void BuildTo(string output, string evidence) => BuildWithOptions(output, evidence, BuildOptions.Development, null);
        public static void BuildAlphaTo(string output, string evidence) => BuildWithOptions(output, evidence, BuildOptions.None, "0.1.0-alpha.1");
        public static void BuildBetaTo(string output, string evidence) => BuildWithOptions(output, evidence, BuildOptions.None, "0.1.0-beta.1");
        public static string ScheduleTo(string output, string evidence, string version, bool development)
        {
            if (betaBuildQueued) return "A beta build is already scheduled";
            betaBuildQueued = true;
            double after = EditorApplication.timeSinceStartup + 2;
            void Run()
            {
                if (EditorApplication.timeSinceStartup < after || EditorApplication.isCompiling) return;
                EditorApplication.update -= Run; betaBuildQueued = false;
                try { BuildWithOptions(output, evidence, development ? BuildOptions.Development : BuildOptions.None, version); }
                catch (Exception e) { Debug.LogException(e); Directory.CreateDirectory(evidence); File.WriteAllText(Path.Combine(evidence, "build-error.txt"), e.ToString()); }
            }
            EditorApplication.update += Run;
            return "Linux " + version + " build scheduled";
        }
        static bool betaBuildQueued;
        static bool betaRelease;
        static double betaBuildAfter;
        public static string ScheduleBeta(bool release = false)
        {
            if (betaBuildQueued) return "A beta build is already scheduled";
            betaBuildQueued = true; betaRelease = release; betaBuildAfter = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += RunBetaBuild;
            return "Linux beta " + (release ? "release" : "development") + " build scheduled";
        }
        static void RunBetaBuild()
        {
            if (EditorApplication.timeSinceStartup < betaBuildAfter || EditorApplication.isCompiling) return;
            EditorApplication.update -= RunBetaBuild; betaBuildQueued = false;
            try {
                if (betaRelease) BuildBetaTo("Builds/Linux-Beta-Release/BlueCafe.x86_64", "evidence/linux-beta-2026-10-10/release");
                else BuildWithOptions("Builds/Linux-Beta/BlueCafe.x86_64", "evidence/linux-beta-2026-10-10/development", BuildOptions.Development, "0.1.0-beta.1");
            }
            catch (Exception e) { Debug.LogException(e); Directory.CreateDirectory("evidence/linux-beta-2026-10-10"); File.WriteAllText("evidence/linux-beta-2026-10-10/build-error.txt", e.ToString()); }
        }
        [Serializable]
        sealed class BuildEvidence
        {
            public string result, version;
            public long errors, warnings;
            public ulong bytes;
            public bool development;
        }
        internal static BuildReport BuildWithOptions(string output, string evidence, BuildOptions options, string version,
            BuildTarget target = BuildTarget.StandaloneLinux64, Action beforeBuild = null)
        {
            var active = SceneManager.GetActiveScene(); Scene scene = default;
            string company = PlayerSettings.companyName, product = PlayerSettings.productName, originalVersion = PlayerSettings.bundleVersion;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            var screenMode = PlayerSettings.fullScreenMode; bool resizable = PlayerSettings.resizableWindow;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)); Directory.CreateDirectory(evidence);
                const string panelPath = "Assets/BlueCafe/App/Resources/StudioPanel.asset";
                var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
                if (panel == null) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, panelPath); }
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/BlueCafe/App/Resources/StudioTheme.tss");
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1600, 1000); panel.match = .5f;
                EditorUtility.SetDirty(panel); AssetDatabase.SaveAssets();
                if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset("Assets/BlueCafe/Editor/ExportTemplate.unity", ScenePath)) throw new Exception("Missing empty scene template.");
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                foreach (var existing in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(existing);
                var go = new GameObject("BlueCafe"); SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<StudioApp>().cafeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/BlueCafe/Shaders/CafeBaked.shader");
                EditorSceneManager.SaveScene(scene, ScenePath); EditorSceneManager.CloseScene(scene, true);
                PlayerSettings.companyName = "BlueCafe"; PlayerSettings.productName = "BlueCafe";
                if (version != null) PlayerSettings.bundleVersion = version;
                PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 1000;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                beforeBuild?.Invoke();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = target, options = options });
                File.WriteAllText(Path.Combine(evidence, "build-result.json"), JsonUtility.ToJson(new BuildEvidence {
                    result = report.summary.result.ToString(), errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
                    bytes = report.summary.totalSize, development = (options & BuildOptions.Development) != 0, version = PlayerSettings.bundleVersion
                }, true));
                if (report.summary.result != BuildResult.Succeeded) throw new Exception(target + " build: " + report.summary.result);
                return report;
            }
            finally {
                PlayerSettings.companyName = company; PlayerSettings.productName = product; PlayerSettings.bundleVersion = originalVersion;
                PlayerSettings.defaultScreenWidth = width; PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.fullScreenMode = screenMode; PlayerSettings.resizableWindow = resizable;
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }
        static bool uiBuildQueued;
        static double uiBuildAfter;
        public static string ScheduleUi()
        {
            if (uiBuildQueued) return "UI build already scheduled";
            uiBuildQueued = true; uiBuildAfter = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += RunUiBuild;
            return "UI build scheduled";
        }
        static void RunUiBuild()
        {
            if (EditorApplication.timeSinceStartup < uiBuildAfter || EditorApplication.isCompiling) return;
            EditorApplication.update -= RunUiBuild; uiBuildQueued = false;
            try { BuildTo("Builds/Linux-UI/BlueCafe.x86_64", "evidence/standalone-ui-fix-2026-10-10"); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("evidence/standalone-ui-fix-2026-10-10/build-error.txt", e.ToString()); }
        }
        public static string Schedule()
        {
            EditorApplication.delayCall += () => { try { Build(); } catch (Exception e) { Debug.LogException(e); File.WriteAllText("evidence/standalone-spine-2026-10-10/build-error.txt", e.ToString()); } };
            return "Linux build scheduled";
        }
    }
}
