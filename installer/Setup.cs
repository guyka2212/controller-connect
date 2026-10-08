// Generic one-file Windows installer (used by VrFsim and VrFsim Controller Connect). The program
// is embedded as payload.zip; names and texts come from BuildInfo, which the build script
// generates. Built with the C# compiler that ships with Windows (.NET Framework 4), so it needs
// nothing extra to build or run. C# 5 only (that compiler's language version).
//
// Installs per user (no admin prompt) to %LOCALAPPDATA%\Programs\<AppName>, adds desktop and
// Start menu shortcuts and an Apps & features entry (uninstall).
//
// Command line (for scripted installs and tests):
//   /S            silent, no window          /D=<folder>    install folder
//   /noshortcuts  skip shortcuts             /noregister    skip the Apps & features entry
//   /nolaunch     do not start the program after a silent install
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppSetup
{
    static class Program
    {
        public const string AppName = BuildInfo.AppName;
        public const string ExeName = BuildInfo.ExeName;
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + BuildInfo.UninstallId;

        [STAThread]
        static int Main(string[] args)
        {
            bool silent = false, shortcuts = true, register = true, launch = true;
            string dir = DefaultDir();
            foreach (var a in args)
            {
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) dir = a.Substring(3).Trim('"');
                else if (a.Equals("/noshortcuts", StringComparison.OrdinalIgnoreCase)) shortcuts = false;
                else if (a.Equals("/noregister", StringComparison.OrdinalIgnoreCase)) register = false;
                else if (a.Equals("/nolaunch", StringComparison.OrdinalIgnoreCase)) launch = false;
            }

            if (silent)
            {
                try
                {
                    string exe = Install(dir, shortcuts, shortcuts, register, null);
                    if (launch) Launch(exe);
                    return 0;
                }
                catch (Exception e)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), BuildInfo.UninstallId + "-Setup.log"), e.ToString());
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(dir));
            return 0;
        }

        public static string DefaultDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        /// <summary>The program always gets its own folder named AppName, so uninstalling can remove it whole.</summary>
        public static string NormalizeDir(string dir)
        {
            dir = Path.GetFullPath(dir.Trim()).TrimEnd('\\');
            if (!string.Equals(Path.GetFileName(dir), AppName, StringComparison.OrdinalIgnoreCase))
                dir = Path.Combine(dir, AppName);
            return dir;
        }

        public static string DesktopShortcut()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        }

        public static string StartMenuShortcut()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
        }

        /// <summary>Installs the embedded program. Returns its exe path. Progress: 0..1.</summary>
        public static string Install(string dir, bool desktop, bool startMenu, bool register, Action<float, string> progress)
        {
            dir = NormalizeDir(dir);
            string exe = Path.Combine(dir, ExeName);

            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                string path = null;
                try { path = p.MainModule.FileName; } catch { }
                if (path == null || string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(AppName + " is running. Close it, then install again.");
            }

            if (Directory.Exists(dir))
            {
                bool empty = Directory.GetFileSystemEntries(dir).Length == 0;
                if (!empty && !File.Exists(exe))
                    throw new InvalidOperationException("The folder " + dir + " already contains other files. Choose another folder.");
                if (!empty)
                {
                    Report(progress, 0f, "Removing the previous version...");
                    Directory.Delete(dir, true);   // an earlier install (it has our exe)
                }
            }
            Directory.CreateDirectory(dir);

            long bytes = 0;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (stream == null) throw new InvalidOperationException("This installer is damaged (no program data inside).");
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    int n = zip.Entries.Count, i = 0;
                    string root = dir + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Bad path in installer data: " + entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            entry.ExtractToFile(target, true);
                            bytes += entry.Length;
                        }
                        i++;
                        if (i % 8 == 0 || i == n) Report(progress, 0.95f * i / n, "Copying files... " + (100 * i / n) + "%");
                    }
                }
            }
            if (!File.Exists(exe)) throw new InvalidOperationException("The installer data does not contain " + ExeName + ".");

            Report(progress, 0.97f, "Creating shortcuts...");
            if (desktop) MakeShortcut(DesktopShortcut(), exe, dir);
            if (startMenu) MakeShortcut(StartMenuShortcut(), exe, dir);
            if (register) Register(dir, exe, bytes);
            Report(progress, 1f, AppName + " is installed.");
            return exe;
        }

        static void Report(Action<float, string> progress, float f, string text)
        {
            if (progress != null) progress(f, text);
        }

        /// <summary>A .lnk through the Windows Script Host (late-bound, so no interop assembly is needed).</summary>
        static void MakeShortcut(string lnkPath, string exe, string dir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { dir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { BuildInfo.Description });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        /// <summary>Apps & features entry. Uninstall removes the game folder, the shortcuts and this entry.</summary>
        static void Register(string dir, string exe, long bytes)
        {
            string uninstall = "cmd.exe /c \"rmdir /s /q \"" + dir + "\" & del /f /q \"" + DesktopShortcut() + "\" & del /f /q \""
                + StartMenuShortcut() + "\" & reg delete \"HKCU\\" + UninstallKey + "\" /f\"";
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", BuildInfo.Version);
                key.SetValue("Publisher", "VrFsim");
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", uninstall);
                key.SetValue("URLInfoAbout", BuildInfo.RepoUrl);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
        }

        public static void Launch(string exe)
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
        }

        public static Image LoadImage(string resource)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return s == null ? null : Image.FromStream(s);
        }
    }

    class SetupForm : Form
    {
        const string AppName = BuildInfo.AppName;
        static readonly Color Dark = Color.FromArgb(0x14, 0x16, 0x1A);
        static readonly Color Accent = Color.FromArgb(0xFF, 0xB4, 0x00);

        readonly TextBox folder;
        readonly CheckBox desktop, startMenu, launch;
        readonly ProgressBar bar;
        readonly Label status;
        readonly Button install, browse, close;
        string installedExe;

        public SetupForm(string dir)
        {
            Text = AppName + " " + BuildInfo.Version + " Setup";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(560, 430);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Panel { BackColor = Dark, Dock = DockStyle.Top, Height = 150 };
            var logo = new PictureBox { Image = Program.LoadImage("logo.png"), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(70, 18, 420, 114) };
            header.Controls.Add(logo);
            Controls.Add(header);

            var intro = new Label
            {
                Text = BuildInfo.Intro,
                Bounds = new Rectangle(20, 162, 520, 40),
            };
            Controls.Add(intro);

            Controls.Add(new Label { Text = "Install folder:", Bounds = new Rectangle(20, 210, 200, 20) });
            folder = new TextBox { Text = dir, Bounds = new Rectangle(20, 232, 420, 25) };
            browse = new Button { Text = "Browse...", Bounds = new Rectangle(450, 230, 90, 28) };
            browse.Click += delegate
            {
                using (var d = new FolderBrowserDialog { Description = "Choose where to install " + AppName + " (a folder named " + AppName + " is created inside)." })
                    if (d.ShowDialog(this) == DialogResult.OK) folder.Text = Program.NormalizeDir(d.SelectedPath);
            };
            Controls.Add(folder);
            Controls.Add(browse);

            desktop = new CheckBox { Text = "Create a desktop shortcut", Checked = true, Bounds = new Rectangle(20, 268, 260, 24) };
            startMenu = new CheckBox { Text = "Add to the Start menu", Checked = true, Bounds = new Rectangle(20, 292, 260, 24) };
            launch = new CheckBox { Text = "Start " + AppName + " when done", Checked = true, Bounds = new Rectangle(290, 268, 250, 24) };
            Controls.Add(desktop);
            Controls.Add(startMenu);
            Controls.Add(launch);

            bar = new ProgressBar { Bounds = new Rectangle(20, 328, 520, 18), Maximum = 1000 };
            status = new Label { Text = "Ready to install.", Bounds = new Rectangle(20, 350, 520, 22) };
            Controls.Add(bar);
            Controls.Add(status);

            install = new Button { Text = "Install", Bounds = new Rectangle(340, 384, 96, 32), BackColor = Accent, FlatStyle = FlatStyle.Flat };
            install.FlatAppearance.BorderSize = 0;
            close = new Button { Text = "Cancel", Bounds = new Rectangle(444, 384, 96, 32) };
            install.Click += delegate { if (installedExe != null) Finish(); else StartInstall(); };
            close.Click += delegate { Close(); };
            Controls.Add(install);
            Controls.Add(close);
            AcceptButton = install;
            ActiveControl = install;
        }

        void StartInstall()
        {
            string dir = folder.Text;
            bool d = desktop.Checked, s = startMenu.Checked;
            foreach (Control c in new Control[] { install, browse, close, folder, desktop, startMenu }) c.Enabled = false;
            var worker = new Thread(delegate ()
            {
                try
                {
                    string exe = Program.Install(dir, d, s, true, delegate (float f, string text)
                    {
                        BeginInvoke((Action)delegate { bar.Value = (int)(f * 1000); status.Text = text; });
                    });
                    BeginInvoke((Action)delegate { Done(exe); });
                }
                catch (Exception e)
                {
                    BeginInvoke((Action)delegate { Failed(e.Message); });
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Done(string exe)
        {
            installedExe = exe;
            status.Text = AppName + " is installed." + (desktop.Checked ? " Look for its icon on your desktop." : "");
            install.Text = "Finish";
            install.Enabled = true;
            close.Visible = false;
        }

        void Failed(string message)
        {
            status.Text = "Install failed.";
            MessageBox.Show(this, message, AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            foreach (Control c in new Control[] { install, browse, close, folder, desktop, startMenu }) c.Enabled = true;
        }

        void Finish()
        {
            if (launch.Checked)
            {
                try { Program.Launch(installedExe); }
                catch (Exception e) { MessageBox.Show(this, e.Message, AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            Close();
        }
    }
}
