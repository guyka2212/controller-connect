// Google's adb (Android Debug Bridge) talks to the Quest over USB. It is not bundled (Google's SDK
// license): one already on the PC is used, or platform-tools are downloaded from Google on request.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ControllerConnect
{
    // ── adb: the headset over USB ──────────────────────────────────────────────────────────

    static class Adb
    {
        public const string Package = "com.vrfsim.biobuzz";
        public const int Port = 47812;
        public static string Path;

        /// <summary>Shared with VrFsim Quest Setup, so adb is downloaded once per PC.</summary>
        static string OwnToolsDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrFsim"); }
        }

        /// <summary>An adb already on this PC: ours, the Android SDK's, a Unity editor's, or on PATH.</summary>
        public static bool Locate()
        {
            var candidates = new List<string>
            {
                System.IO.Path.Combine(OwnToolsDir, @"platform-tools\adb.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe"),
            };
            try
            {
                string hub = @"C:\Program Files\Unity\Hub\Editor";
                if (Directory.Exists(hub))
                    foreach (var d in Directory.GetDirectories(hub))
                        candidates.Add(System.IO.Path.Combine(d, @"Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"));
            }
            catch { }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Trim().Length > 0) candidates.Add(System.IO.Path.Combine(dir.Trim(), "adb.exe")); } catch { }
            }
            foreach (var c in candidates)
                if (File.Exists(c)) { Path = c; return true; }
            Path = null;
            return false;
        }

        /// <summary>Downloads Android platform-tools from Google into %LOCALAPPDATA%\VrFsim.</summary>
        public static void Download(Action<string> status)
        {
            string zip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrfsim-platform-tools.zip");
            status("Downloading Android platform-tools from Google...");
            using (var web = new WebClient())
                web.DownloadFile("https://dl.google.com/android/repository/platform-tools-latest-windows.zip", zip);
            status("Unpacking...");
            string target = System.IO.Path.Combine(OwnToolsDir, "platform-tools");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(OwnToolsDir);
            ZipFile.ExtractToDirectory(zip, OwnToolsDir);
            File.Delete(zip);
            Locate();
        }

        public static string Run(string args, int timeoutMs)
        {
            if (Path == null) return "";
            var psi = new ProcessStartInfo(Path, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                var output = new StringBuilder();
                p.OutputDataReceived += delegate (object o, DataReceivedEventArgs e) { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e) { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                else p.WaitForExit();
                lock (output) return output.ToString();
            }
        }

        /// <summary>(serial, state) of each connected device: state is "device", "unauthorized" or "offline".</summary>
        public static List<KeyValuePair<string, string>> Devices()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var line in Run("devices", 8000).Split('\n'))
            {
                var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] != "List" && !parts[0].StartsWith("*"))
                    list.Add(new KeyValuePair<string, string>(parts[0], parts[1]));
            }
            return list;
        }

        static readonly Dictionary<string, bool> questCache = new Dictionary<string, bool>();

        /// <summary>
        /// Connected Meta Quest headsets only. Emulators and phones are skipped, so the game is never
        /// started on the wrong device. An unauthorized device cannot be asked what it is yet, so it
        /// is kept (it is almost always the headset waiting for "Allow USB debugging").
        /// </summary>
        public static List<KeyValuePair<string, string>> Headsets()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var d in Devices())
            {
                if (d.Key.StartsWith("emulator-")) continue;
                if (d.Value == "device")
                {
                    bool quest;
                    if (!questCache.TryGetValue(d.Key, out quest))
                    {
                        string who = (Shell(d.Key, "getprop ro.product.manufacturer") + " " + Shell(d.Key, "getprop ro.product.model")).ToLowerInvariant();
                        quest = who.Contains("oculus") || who.Contains("meta") || who.Contains("quest");
                        questCache[d.Key] = quest;
                    }
                    if (!quest) continue;
                }
                list.Add(d);
            }
            return list;
        }

        public static string Shell(string serial, string cmd) { return Run("-s " + serial + " shell " + cmd, 15000).Trim(); }

        public static string InstalledVersion(string serial)
        {
            var m = Regex.Match(Shell(serial, "dumpsys package " + Package), @"versionName=(\S+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static bool Forward(string serial)
        {
            string r = Run("-s " + serial + " forward tcp:" + Port + " tcp:" + Port, 8000);
            return !r.ToLowerInvariant().Contains("error");
        }
    }
}
