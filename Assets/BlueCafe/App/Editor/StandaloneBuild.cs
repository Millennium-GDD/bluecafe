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
        [Serializable]
        sealed class BuildEvidence
        {
            public string result, version;
            public long errors, warnings;
            public ulong bytes;
            public bool development;
        }
        static void BuildWithOptions(string output, string evidence, BuildOptions options, string version)
        {
            var active = SceneManager.GetActiveScene(); Scene scene = default;
            string company = PlayerSettings.companyName, product = PlayerSettings.productName, originalVersion = PlayerSettings.bundleVersion;
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
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.StandaloneLinux64, options = options });
                File.WriteAllText(Path.Combine(evidence, "build-result.json"), JsonUtility.ToJson(new BuildEvidence {
                    result = report.summary.result.ToString(), errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
                    bytes = report.summary.totalSize, development = (options & BuildOptions.Development) != 0, version = PlayerSettings.bundleVersion
                }, true));
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Linux build: " + report.summary.result);
            }
            finally { PlayerSettings.companyName = company; PlayerSettings.productName = product; PlayerSettings.bundleVersion = originalVersion; if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
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
