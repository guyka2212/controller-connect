// Sends the selected controller to VrFsim on the headset. See PROTOCOL.md.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ControllerConnect
{
    // ── Sends the selected controller to the game, ~120 times a second ─────────────────────

    class Bridge
    {
        public volatile Controller Source;
        public volatile string Serial;           // headset to forward to (null = none)
        public volatile string Status = "Waiting for the headset.";
        public volatile bool Connected;
        public PadState Last;
        Thread thread;
        volatile bool running;

        public void Start()
        {
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "VrFsim bridge" };
            thread.Start();
        }

        public void Stop() { running = false; }

        void Loop()
        {
            string forwarded = null;
            while (running)
            {
                string serial = Serial;
                if (!Program.Direct && serial == null) { Connected = false; Status = "Waiting for the headset."; Thread.Sleep(500); continue; }
                if (!Program.Direct && forwarded != serial)
                {
                    if (!Adb.Forward(serial)) { Status = "Could not open the USB link to the headset."; Thread.Sleep(1500); continue; }
                    forwarded = serial;
                }
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.NoDelay = true;
                        client.Connect(IPAddress.Loopback, Adb.Port);
                        var stream = client.GetStream();
                        stream.ReadTimeout = 2000;
                        stream.Write(Encoding.ASCII.GetBytes("VFG1"), 0, 4);
                        var ack = new byte[2];
                        int got = 0;
                        while (got < 2) { int n = stream.Read(ack, got, 2 - got); if (n <= 0) break; got += n; }
                        if (got < 2 || ack[0] != 'O' || ack[1] != 'K')
                            throw new IOException("game not listening");
                        Connected = true;
                        Status = "Connected: your controller is driving VrFsim on the headset.";
                        var sw = Stopwatch.StartNew();
                        long lastSend = -1000;
                        byte[] previous = null;
                        while (running && Serial == serial)
                        {
                            PadState s;
                            var src = Source;
                            if (src == null || !src.Read(out s)) s = new PadState();
                            Last = s;
                            var packet = s.Encode();
                            long now = sw.ElapsedMilliseconds;
                            if (previous == null || !Same(packet, previous) || now - lastSend > 100)
                            {
                                stream.Write(packet, 0, packet.Length);
                                previous = packet;
                                lastSend = now;
                            }
                            Thread.Sleep(8);
                        }
                    }
                }
                catch (Exception)
                {
                    Connected = false;
                    Status = "Waiting for VrFsim to start on the headset...";
                    forwarded = null;   // re-open the USB link on the next try (the headset may have reconnected)
                    Thread.Sleep(1000);
                }
                Connected = false;
            }
        }

        static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
