using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BlueCafe.App.Editor
{
    /// <summary>Windows x64 Mono Player; downloaded content stays in the external prepared pack.</summary>
    [InitializeOnLoad]
    public static class WindowsStandaloneBuild
    {
        public const string Output = "Builds/Windows/BlueCafe.exe";
        public const string Evidence = "evidence/windows-build";
        public const string Version = "0.1.0-beta.5";
        const string RestoreKey = "BlueCafe.WindowsStandaloneBuild.Restore";
        static bool queued;
        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static WindowsStandaloneBuild()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(RestoreKey, "")))
                EditorApplication.delayCall += RecoverSettings;
        }

        [MenuItem("BlueCafe/Build Windows x64 release")]
        public static void Build() => BuildTo(Output, Evidence, Version);

        /// <summary>Batch entry point for unity build --execute-method.</summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            BuildTo(Argument(args, "-buildOutput") ?? Output,
                Argument(args, "-blueCafeEvidence") ?? Evidence,
                Argument(args, "-blueCafeVersion") ?? Version,
                args.Contains("-blueCafeDevelopment"));
        }

        static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }

        /// <summary>Schedule outside CLI eval so a long build does not block the eval response.</summary>
        public static string ScheduleTo(string output = Output, string evidence = Evidence,
            string version = Version, bool development = false)
        {
            if (queued) throw new InvalidOperationException("A Windows build is already scheduled.");
            output = Absolute(output); evidence = Absolute(evidence);
            RequireChild(evidence, Path.Combine(ProjectRoot, "evidence"));
            RequireChild(Path.Combine(evidence, "build-result.json"), evidence);
            RequireChild(Path.Combine(evidence, "build-error.txt"), evidence);
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "build-result.json"), JsonUtility.ToJson(
                new BuildEvidence { result = "Queued", output = output, version = version, development = development }, true));
            queued = true;
            double after = EditorApplication.timeSinceStartup + 2;
            void Run()
            {
                if (EditorApplication.timeSinceStartup < after || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) return;
                EditorApplication.update -= Run; queued = false;
                try { BuildTo(output, evidence, version, development); }
                catch (Exception error) { Debug.LogException(error); }
            }
            EditorApplication.update += Run;
            return "Windows x64 " + version + " build scheduled; read " + evidence + "/build-result.json for completion.";
        }

        [Serializable] sealed class FileEvidence { public string path, sha256; }
        [Serializable] sealed class BuildEvidence
        {
            public string result = "Started", version, target = "StandaloneWindows64", backend = "Mono";
            public string apiCompatibility = "NET_Unity_4_8";
            public string originalActiveTarget;
            public string output, sourceFingerprint, error;
            public long errors, warnings;
            public ulong bytes;
            public bool development;
            public bool settingsRestored;
            public FileEvidence[] files = Array.Empty<FileEvidence>();
        }

        [Serializable] sealed class RestoreState
        {
            public int target, backend, apiCompatibility;
            public string reportPath, errorPath, report;
        }

        public static void BuildTo(string output, string evidence, string version = Version, bool development = false)
        {
            output = Absolute(output); evidence = Absolute(evidence);
            RequireChild(evidence, Path.Combine(ProjectRoot, "evidence"));
            string reportPath = Path.Combine(evidence, "build-result.json");
            string errorPath = Path.Combine(evidence, "build-error.txt");
            RequireChild(reportPath, evidence); RequireChild(errorPath, evidence);
            Directory.CreateDirectory(evidence);
            var state = new BuildEvidence { output = output, version = version, development = development };
            // Invalidate any previous success before preflight or build can fail.
            File.WriteAllText(reportPath, JsonUtility.ToJson(state, true));
            try
            {
                Validate(output, version);
                var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
                var apiCompatibility = PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone);
                var activeTarget = EditorUserBuildSettings.activeBuildTarget;
                state.originalActiveTarget = activeTarget.ToString();
                try
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                    // Windows System theme reads Registry, which is outside the .NET Standard profile.
                    PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
                    state.apiCompatibility = PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone).ToString();
                    var report = StandaloneBuild.BuildWithOptions(output, evidence,
                        development ? BuildOptions.Development : BuildOptions.None, version,
                        BuildTarget.StandaloneWindows64, () => state.sourceFingerprint = Fingerprint());
                    state.errors = report.summary.totalErrors; state.warnings = report.summary.totalWarnings;
                    state.bytes = report.summary.totalSize;
                    if (state.errors != 0)
                        throw new IOException("Windows build reported " + state.errors + " errors despite its summary result.");
                    string folder = Path.GetDirectoryName(output);
                    if (!File.Exists(output) || !File.Exists(Path.Combine(folder, "UnityPlayer.dll")) ||
                        !Directory.Exists(Path.Combine(folder, "BlueCafe_Data")) || !Directory.Exists(Path.Combine(folder, "MonoBleedingEdge")))
                        throw new IOException("Windows build did not produce a complete Mono Player.");
                    state.files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .Select(path => new FileEvidence { path = path.Substring(folder.Length + 1).Replace('\\', '/'), sha256 = Hash(path) }).ToArray();
                    state.result = "Succeeded";
                }
                catch (Exception error)
                {
                    state.result = "Failed"; state.errors = Math.Max(1, state.errors); state.error = error.ToString();
                    throw;
                }
                finally
                {
                    var restore = new RestoreState { target = (int)activeTarget, backend = (int)backend,
                        apiCompatibility = (int)apiCompatibility, reportPath = reportPath, errorPath = errorPath,
                        report = JsonUtility.ToJson(state, true) };
                    // The synchronous target switch can unload this domain before its finally continues.
                    SessionState.SetString(RestoreKey, JsonUtility.ToJson(restore));
                    RestoreSettings(restore, state);
                }
            }
            catch (Exception error)
            {
                RecordFailure(state, error);
                File.WriteAllText(errorPath, state.error);
                throw;
            }
            finally
            {
                if (state.result == "Succeeded" && !state.settingsRestored) state.result = "Restoring";
                File.WriteAllText(reportPath, JsonUtility.ToJson(state, true));
            }
        }

        static void RestoreSettings(RestoreState restore, BuildEvidence state)
        {
            var pending = JsonUtility.FromJson<BuildEvidence>(restore.report);
            pending.result = "Restoring";
            try
            {
                File.WriteAllText(restore.reportPath, JsonUtility.ToJson(pending, true));
                if (!string.IsNullOrEmpty(state.error)) File.WriteAllText(restore.errorPath, state.error);
                var target = (BuildTarget)restore.target;
                if (EditorUserBuildSettings.activeBuildTarget != target &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target))
                    throw new InvalidOperationException("Failed to restore active build target " + target + ".");
            }
            finally
            {
                // Restore the target first: .NET Standard cannot compile Windows' Registry theme API.
                try { PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, (ApiCompatibilityLevel)restore.apiCompatibility); }
                finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, (ScriptingImplementation)restore.backend); }
            }
            if (EditorUserBuildSettings.activeBuildTarget != (BuildTarget)restore.target ||
                PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone) != (ApiCompatibilityLevel)restore.apiCompatibility ||
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != (ScriptingImplementation)restore.backend)
                throw new InvalidOperationException("Windows build did not restore the original Editor target/profile/backend.");
            state.settingsRestored = true;
            File.WriteAllText(restore.reportPath, JsonUtility.ToJson(state, true));
            SessionState.EraseString(RestoreKey);
        }

        static void RecoverSettings()
        {
            string saved = SessionState.GetString(RestoreKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            var restore = JsonUtility.FromJson<RestoreState>(saved);
            var state = JsonUtility.FromJson<BuildEvidence>(restore.report);
            try { RestoreSettings(restore, state); }
            catch (Exception error)
            {
                RecordFailure(state, error);
                File.WriteAllText(restore.errorPath, state.error);
                File.WriteAllText(restore.reportPath, JsonUtility.ToJson(state, true));
                SessionState.EraseString(RestoreKey);
                Debug.LogException(error);
            }
        }

        static void RecordFailure(BuildEvidence state, Exception error)
        {
            state.result = "Failed"; state.errors = Math.Max(1, state.errors);
            string detail = error.ToString();
            state.error = string.IsNullOrEmpty(state.error) || state.error == detail
                ? detail : state.error + "\nSettings restoration failure:\n" + detail;
        }

        static void Validate(string output, string version)
        {
            RequireChild(output, Path.Combine(ProjectRoot, "Builds"));
            if (Path.GetFileName(output) != "BlueCafe.exe") throw new ArgumentException("Windows output must be named BlueCafe.exe.");
            if (string.IsNullOrEmpty(version) || version.Length > 80 || version[0] < '0' || version[0] > '9' ||
                version.Any(c => !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_')) || version == "." || version == "..")
                throw new ArgumentException("Version must start with a digit and use letters, digits, dots, hyphens or underscores.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Exit Play mode and wait for compilation/builds before building Windows.");
            var folder = Path.GetDirectoryName(output);
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                throw new IOException("Preserve the previous output and choose a fresh empty Windows build folder: " + folder);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new NotSupportedException("Windows Build Support (Mono) is not installed for Unity " + Application.unityVersion + ". Add it through Unity Hub before building.");
        }

        static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A build path is required.");
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path));
        }

        static void RequireChild(string path, string root)
        {
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("Path must be inside " + root + ": " + path);
            // Reject links before creating or writing files through them.
            for (var current = new DirectoryInfo(Path.GetDirectoryName(path)); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Build/evidence paths must not traverse symbolic links: " + current.FullName);
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Build/evidence paths must not be symbolic links: " + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static string Fingerprint()
        {
            var folders = new[] { "Assets/BlueCafe/Core", "Assets/BlueCafe/Infrastructure", "Assets/BlueCafe/Unity",
                "Assets/BlueCafe/App", "Assets/BlueCafe/Agent", "Assets/BlueCafe/Shaders", "Packages", "ProjectSettings" };
            var files = folders.SelectMany(folder => Directory.GetFiles(Path.Combine(ProjectRoot, folder), "*", SearchOption.AllDirectories))
                .OrderBy(path => path, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                foreach (var path in files)
                {
                    var entry = Encoding.UTF8.GetBytes(path.Substring(ProjectRoot.Length + 1).Replace('\\', '/') + "\0" + Hash(path) + "\n");
                    sha.TransformBlock(entry, 0, entry.Length, entry, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
