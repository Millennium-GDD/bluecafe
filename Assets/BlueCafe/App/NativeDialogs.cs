using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace BlueCafe.App
{
    // File picking currently uses the Linux adapter; file-manager reveal supports desktop platforms.
    internal static class NativeDialogs
    {
        static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        /// <summary>Returns true when the file was selected, false when only its containing folder could be opened.</summary>
        public static Task<bool> RevealFile(string path)
        {
            var platform = Application.platform;
            return Task.Run(() =>
            {
                path = Path.GetFullPath(path);
                if (!File.Exists(path)) throw new FileNotFoundException("This preset file is unavailable: " + path, path);
                if (platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows paths cannot contain quotes. Explorer forwards this request to an existing process.
                    using (var process = Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = false }))
                        if (process == null) throw new InvalidOperationException("Windows Explorer could not be opened.");
                    return true;
                }
                if (platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor)
                {
                    if (!RunFileManager("/usr/bin/open", "-R " + Quote(path)))
                        throw new InvalidOperationException("Finder could not reveal the preset file: " + path);
                    return true;
                }
                if (platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor)
                {
                    // Escape the Unix filename before building the URI: Uri(path) treats a literal
                    // backslash as a separator, and dbus-send treats commas as array separators.
                    var uri = "file://" + Uri.EscapeDataString(path).Replace("%2F", "/");
                    if (RunFileManager("dbus-send", "--session --print-reply --reply-timeout=3000 --dest=org.freedesktop.FileManager1 /org/freedesktop/FileManager1 org.freedesktop.FileManager1.ShowItems " + Quote("array:string:" + uri) + " string:"))
                        return true;
                    if (RunFileManager("xdg-open", Quote(Path.GetDirectoryName(path)))) return false;
                    throw new InvalidOperationException("The file manager is unavailable. Preset file: " + path);
                }
                throw new NotSupportedException("Opening a file manager is unavailable on this platform. Preset file: " + path);
            });
        }
        static bool RunFileManager(string executable, string arguments)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }))
                {
                    if (process == null) return false;
                    // Drain both streams while waiting, so a desktop helper cannot block on a full pipe.
                    _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(5000)) { process.Kill(); return false; }
                    return process.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }
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
