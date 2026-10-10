using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace BlueCafe.App
{
    // Linux desktop adapter for the portable alpha. Other platforms supply a native adapter in beta.
    internal static class NativeDialogs
    {
        static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        public static Task<string> Choose(string title, bool folder = false, bool save = false)
        {
            var platform = Application.platform;
            return Task.Run(() => {
                if (platform != RuntimePlatform.LinuxPlayer && platform != RuntimePlatform.LinuxEditor)
                    throw new NotSupportedException("Native file dialogs are available in the Linux alpha. Enter a path here on other platforms.");
                var info = new ProcessStartInfo("zenity", "--file-selection --title=" + Quote(title) + (folder ? " --directory" : "") + (save ? " --save --confirm-overwrite" : "")) {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                };
                using (var process = Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
                    if (process.ExitCode == 1) return null;
                    if (process.ExitCode != 0) throw new InvalidOperationException("The native dialog is unavailable. Enter a path, or install the desktop zenity dialog package.");
                    return output.TrimEnd('\r', '\n');
                }
            });
        }
    }
}
