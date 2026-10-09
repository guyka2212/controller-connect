// The Controller Connect window: headset status, start the game, pick the controller, link status.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ControllerConnect
{
    // ── The window ───────────────────────────────────────────────────────────────────────

    class MainForm : Form
    {
        static readonly Color Dark = Color.FromArgb(0x14, 0x16, 0x1A);
        static readonly Color Accent = Color.FromArgb(0xFF, 0xB4, 0x00);
        static readonly Color Good = Color.FromArgb(0x2E, 0x8B, 0x3E);
        static readonly Color Warn = Color.FromArgb(0xB3, 0x6B, 0x00);
        const string GameReleases = "https://github.com/guyka2212/biobuzz-vr-sim/releases/latest";

        readonly Label headsetStatus, gameStatus, bridgeStatus, padLive;
        readonly Button getAdb, start, refreshPads;
        readonly LinkLabel getGame;
        readonly ComboBox pads;
        readonly Bridge bridge = new Bridge();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        volatile bool busy, polling;
        string serial, headsetName, installedVersion;
        volatile string notReady = "Looking for the headset...";
        bool versionChecked;

        public MainForm()
        {
            Text = "VrFsim Controller Connect " + BuildInfo.Version;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(600, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Panel { BackColor = Dark, Dock = DockStyle.Top, Height = 120 };
            header.Controls.Add(new PictureBox { Image = Program.LoadImage("logo.png"), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(100, 14, 400, 92) });
            Controls.Add(header);

            Controls.Add(new Label
            {
                Text = "For laptops that can't run Quest Link: the game runs on the headset, and the controller "
                     + "plugged into this PC is sent to it over the headset's USB cable. Keep this window open while you play.",
                Bounds = new Rectangle(16, 128, 568, 40),
            });

            Controls.Add(Title("1.  Headset (USB-C cable to this PC, developer mode on)", 176));
            headsetStatus = Status(200);
            getAdb = new Button { Text = "Download Android tools from Google (one time, ~7 MB)", Bounds = new Rectangle(16, 226, 360, 30), Visible = false };
            getAdb.Click += delegate { DownloadAdb(); };
            Controls.Add(getAdb);

            Controls.Add(Title("2.  VrFsim on the headset", 266));
            gameStatus = Status(290);
            start = new Button { Text = "Start VrFsim on the headset", Bounds = new Rectangle(16, 316, 250, 30) };
            start.Click += delegate { StartGame(); };
            Controls.Add(start);
            getGame = new LinkLabel { Text = "Get the game: VrFsim Quest Setup", Bounds = new Rectangle(280, 322, 300, 22) };
            getGame.LinkClicked += delegate { Process.Start(GameReleases); };
            Controls.Add(getGame);

            Controls.Add(Title("3.  Controller (plug it into this PC)", 358));
            pads = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(16, 384, 470, 26) };
            pads.SelectedIndexChanged += delegate { bridge.Source = pads.SelectedItem as Controller; };
            refreshPads = new Button { Text = "Refresh", Bounds = new Rectangle(494, 383, 90, 28) };
            refreshPads.Click += delegate { FindControllers(); };
            Controls.Add(pads);
            Controls.Add(refreshPads);
            padLive = new Label { Bounds = new Rectangle(16, 416, 568, 20), Font = new Font("Consolas", 9f), ForeColor = Color.DimGray };
            Controls.Add(padLive);

            Controls.Add(Title("Link", 446));
            bridgeStatus = Status(470);
            Controls.Add(new Label
            {
                Text = "Tip: the cable also charges the headset. Your PC's controller and gamepads paired to the headset both work.",
                Bounds = new Rectangle(16, 506, 568, 40), ForeColor = Color.DimGray,
            });

            FindControllers();
            ActiveControl = refreshPads;
            bridge.Start();
            timer.Interval = 250;
            timer.Tick += delegate { Tick(); };
            timer.Start();
            FormClosing += delegate { bridge.Stop(); };
        }

        Label Title(string text, int y)
        {
            return new Label { Text = text, Bounds = new Rectangle(16, y, 568, 22), Font = new Font("Segoe UI Semibold", 10f) };
        }

        Label Status(int y)
        {
            var l = new Label { Bounds = new Rectangle(16, y, 568, 22) };
            Controls.Add(l);
            return l;
        }

        static void SetStatus(Label l, string text, Color c) { l.Text = text; l.ForeColor = c; }

        void FindControllers()
        {
            var keep = pads.SelectedItem as Controller;
            pads.Items.Clear();
            for (int i = 0; i < 4; i++) if (XInputController.IsConnected(i)) pads.Items.Add(new XInputController(i));
            bool anyXInput = pads.Items.Count > 0;
            foreach (var j in JoystickController.Find())
                if (!(anyXInput && j.LooksLikeXInput)) pads.Items.Add(j);
            if (pads.Items.Count == 0)
            {
                pads.Items.Add(new NoController());
            }
            int index = 0;
            if (keep != null)
                for (int i = 0; i < pads.Items.Count; i++) if (pads.Items[i].ToString() == keep.ToString()) index = i;
            pads.SelectedIndex = index;
        }

        class NoController : Controller
        {
            public NoController() { Name = "No controller found - plug one in and press Refresh"; }
            public override bool Read(out PadState s) { s = new PadState(); return false; }
        }

        void Tick()
        {
            var src = pads.SelectedItem as Controller;
            PadState s;
            padLive.Text = src != null && src.Read(out s) ? s.Describe() : "";
            SetStatus(bridgeStatus, bridge.Status, bridge.Connected ? Good : Warn);
            if (!polling && !busy) { polling = true; ThreadPool.QueueUserWorkItem(delegate { PollHeadset(); }); }
        }

        void PollHeadset()
        {
            try
            {
                if (Program.Direct) { Ui(delegate { SetStatus(headsetStatus, "Direct mode (testing): no headset needed.", Good); }); return; }
                if (Adb.Path == null && !Adb.Locate())
                {
                    notReady = "First click \"Download Android tools from Google\" (step 1). Controller Connect needs them to talk to the headset.";
                    Ui(delegate
                    {
                        SetStatus(headsetStatus, "Android tools (adb) are needed to talk to the headset.", Warn);
                        getAdb.Visible = true;
                    });
                    return;
                }
                var devices = Adb.Headsets();
                string ready = null, state = null;
                foreach (var d in devices) { state = d.Value; if (d.Value == "device") { ready = d.Key; break; } }
                if (ready == null)
                {
                    serial = null; bridge.Serial = null; versionChecked = false;
                    string text = state == "unauthorized"
                        ? "Put the headset on and press Allow on \"Allow USB debugging\" (tick Always allow)."
                        : devices.Count > 0 ? "The headset is connecting..." : "No Quest found. Connect it with a USB-C cable and turn on developer mode.";
                    notReady = text;
                    Ui(delegate { SetStatus(headsetStatus, text, Warn); getAdb.Visible = false; SetStatus(gameStatus, "", Color.Black); });
                    return;
                }
                if (ready != serial)
                {
                    serial = ready;
                    headsetName = Adb.Shell(ready, "getprop ro.product.model");
                    versionChecked = false;
                }
                if (!versionChecked)
                {
                    installedVersion = Adb.InstalledVersion(ready);
                    versionChecked = true;
                }
                bridge.Serial = ready;
                notReady = null;
                string name = string.IsNullOrEmpty(headsetName) ? "Quest" : headsetName;
                Ui(delegate
                {
                    SetStatus(headsetStatus, "Connected: " + name + ".", Good);
                    getAdb.Visible = false;
                    start.Enabled = !busy;
                    if (installedVersion == null)
                        SetStatus(gameStatus, "VrFsim is not on the headset yet: install it with VrFsim Quest Setup (link on the right).", Warn);
                    else
                        SetStatus(gameStatus, "VrFsim " + installedVersion + " is installed on the headset.", Good);
                });
            }
            catch (Exception e) { Ui(delegate { SetStatus(headsetStatus, "Headset check failed: " + e.Message, Warn); }); }
            finally { polling = false; }
        }

        void Ui(MethodInvoker a)
        {
            try { if (!IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void DownloadAdb()
        {
            var answer = MessageBox.Show(this,
                "Download Android SDK Platform-Tools (adb) from Google?\n\nThey are installed for this app only, in "
                + "%LOCALAPPDATA%\\VrFsim. By downloading you accept Google's Android SDK terms:\n"
                + "https://developer.android.com/studio/terms",
                "VrFsim Controller Connect", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            RunBusy(delegate
            {
                Adb.Download(delegate (string t) { Ui(delegate { SetStatus(headsetStatus, t, Warn); }); });
            }, "Android tools ready.");
        }

        /// <summary>True when the headset is ready; otherwise tells the user what is missing.</summary>
        bool HeadsetReady()
        {
            string why = notReady;
            if (serial != null && why == null) return true;
            MessageBox.Show(this, why ?? "Looking for the headset... connect it with a USB-C cable.", "VrFsim Controller Connect",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        void StartGame()
        {
            if (!HeadsetReady()) return;
            if (installedVersion == null)
            {
                MessageBox.Show(this, "VrFsim is not on the headset yet. Install it first with VrFsim Quest Setup (the link next to this button).",
                    "VrFsim Controller Connect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string s = serial;
            RunBusy(delegate { Adb.Shell(s, "am start -n " + Adb.Package + "/com.unity3d.player.UnityPlayerActivity"); },
                "VrFsim started. Put the headset on.");
        }

        void RunBusy(MethodInvoker work, string done)
        {
            busy = true;
            start.Enabled = getAdb.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { work(); } catch (Exception e) { error = e.Message; }
                busy = false;
                Ui(delegate
                {
                    getAdb.Enabled = start.Enabled = true;
                    if (error != null) MessageBox.Show(this, error, "VrFsim Controller Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else SetStatus(gameStatus, done, Good);
                });
            });
        }
    }
}
