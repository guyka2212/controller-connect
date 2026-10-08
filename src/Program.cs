// VrFsim Controller Connect: entry point. See README.md.
// Command line: /direct  connect to 127.0.0.1:47812 without adb (testing against a local listener).
using System;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace ControllerConnect
{
    static class Program
    {
        public static bool Direct;

        [STAThread]
        static void Main(string[] args)
        {
            foreach (var a in args) if (a.Equals("/direct", StringComparison.OrdinalIgnoreCase)) Direct = true;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 for the download
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static Image LoadImage(string resource)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return s == null ? null : Image.FromStream(s);
        }
    }
}
