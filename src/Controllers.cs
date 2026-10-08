// The controller on the PC (XInput, or any Windows game controller through the joystick API)
// and the state that is sent to the headset.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ControllerConnect
{
    // ── Controller state (what goes over the wire) ──────────────────────────────────────────

    struct PadState
    {
        // Bit numbers = Unity's GamepadButton enum.
        public const int DpadUp = 0, DpadDown = 1, DpadLeft = 2, DpadRight = 3, North = 4, East = 5, South = 6,
            West = 7, LeftStick = 8, RightStick = 9, LeftShoulder = 10, RightShoulder = 11, Start = 12, Select = 13;

        public ushort Buttons;
        public float LX, LY, RX, RY, LT, RT;

        public void Set(int bit, bool on) { if (on) Buttons |= (ushort)(1 << bit); }

        public byte[] Encode()
        {
            var b = new byte[16];
            b[0] = (byte)'S';
            BitConverter.GetBytes(Buttons).CopyTo(b, 2);
            Axis(b, 4, LX); Axis(b, 6, LY); Axis(b, 8, RX); Axis(b, 10, RY);
            b[12] = (byte)Math.Round(Clamp01(LT) * 255f);
            b[13] = (byte)Math.Round(Clamp01(RT) * 255f);
            return b;
        }

        static void Axis(byte[] b, int i, float v)
        {
            v = Math.Max(-1f, Math.Min(1f, v));
            BitConverter.GetBytes((short)Math.Round(v * 32767f)).CopyTo(b, i);
        }

        static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }

        public string Describe()
        {
            var sb = new StringBuilder();
            string[] names = { "Up", "Down", "Left", "Right", "Y", "B", "A", "X", "LS", "RS", "LB", "RB", "Start", "Back" };
            for (int i = 0; i < names.Length; i++) if ((Buttons & (1 << i)) != 0) sb.Append(names[i]).Append(' ');
            if (LT > 0.1f) sb.Append("LT ");
            if (RT > 0.1f) sb.Append("RT ");
            return string.Format("Left stick {0,5:0.00} {1,5:0.00}   Right stick {2,5:0.00} {3,5:0.00}   {4}",
                LX, LY, RX, RY, sb.Length > 0 ? sb.ToString() : "(no buttons)");
        }
    }

    // ── Controllers on the PC: XInput (Xbox and compatible) and the Windows joystick API (everything else)

    abstract class Controller
    {
        public string Name;
        public abstract bool Read(out PadState s);
        public override string ToString() { return Name; }
    }

    class XInputController : Controller
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE { public uint dwPacketNumber; public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int GetState14(int index, out XINPUT_STATE state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int GetState910(int index, out XINPUT_STATE state);

        readonly int index;
        public XInputController(int index) { this.index = index; Name = "Xbox / XInput controller " + (index + 1); }

        static int Get(int index, out XINPUT_STATE st)
        {
            try { return GetState14(index, out st); }
            catch (DllNotFoundException) { return GetState910(index, out st); }
        }

        public static bool IsConnected(int index)
        {
            XINPUT_STATE st;
            try { return Get(index, out st) == 0; } catch { return false; }
        }

        public override bool Read(out PadState s)
        {
            s = new PadState();
            XINPUT_STATE st;
            if (Get(index, out st) != 0) return false;
            int w = st.wButtons;
            s.Set(PadState.DpadUp, (w & 0x0001) != 0); s.Set(PadState.DpadDown, (w & 0x0002) != 0);
            s.Set(PadState.DpadLeft, (w & 0x0004) != 0); s.Set(PadState.DpadRight, (w & 0x0008) != 0);
            s.Set(PadState.Start, (w & 0x0010) != 0); s.Set(PadState.Select, (w & 0x0020) != 0);
            s.Set(PadState.LeftStick, (w & 0x0040) != 0); s.Set(PadState.RightStick, (w & 0x0080) != 0);
            s.Set(PadState.LeftShoulder, (w & 0x0100) != 0); s.Set(PadState.RightShoulder, (w & 0x0200) != 0);
            s.Set(PadState.South, (w & 0x1000) != 0); s.Set(PadState.East, (w & 0x2000) != 0);
            s.Set(PadState.West, (w & 0x4000) != 0); s.Set(PadState.North, (w & 0x8000) != 0);
            s.LX = st.sThumbLX / 32767f; s.LY = st.sThumbLY / 32767f;
            s.RX = st.sThumbRX / 32767f; s.RY = st.sThumbRY / 32767f;
            s.LT = st.bLeftTrigger / 255f; s.RT = st.bRightTrigger / 255f;
            return true;
        }
    }

    /// <summary>
    /// Any controller Windows lists under "Game controllers" (PlayStation, Logitech in D mode, Switch
    /// Pro, generic USB pads). Buttons use the common DirectInput order: 1 X/Square, 2 A/Cross,
    /// 3 B/Circle, 4 Y/Triangle, 5 LB, 6 RB, 7 LT, 8 RT, 9 Back/Share, 10 Start/Options, 11 LS, 12 RS;
    /// left stick X/Y, right stick Z/R, D-pad on the POV hat.
    /// </summary>
    class JoystickController : Controller
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
                wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOYINFOEX
        {
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint joyGetDevCapsW(UIntPtr id, ref JOYCAPS caps, uint size);
        [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JOYINFOEX info);

        const uint JOY_RETURNALL = 0xFF;
        readonly uint id;
        JOYCAPS caps;

        JoystickController(uint id, JOYCAPS caps)
        {
            this.id = id;
            this.caps = caps;
            Name = (string.IsNullOrEmpty(caps.szPname) ? "Game controller" : caps.szPname.Trim()) + " (joystick " + (id + 1) + ")";
        }

        public static List<JoystickController> Find()
        {
            var list = new List<JoystickController>();
            uint n = 0;
            try { n = joyGetNumDevs(); } catch { return list; }
            for (uint i = 0; i < n && i < 16; i++)
            {
                var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX)), dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(i, ref info) != 0) continue;   // not plugged in
                var caps = new JOYCAPS();
                if (joyGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf(typeof(JOYCAPS))) != 0) continue;
                list.Add(new JoystickController(i, caps));
            }
            return list;
        }

        /// <summary>Xbox pads also show up here; they are better read through XInput.</summary>
        public bool LooksLikeXInput
        {
            get
            {
                string n = (caps.szPname ?? "").ToLowerInvariant();
                return n.Contains("xbox") || n.Contains("xinput");
            }
        }

        static float Norm(uint v, uint min, uint max)
        {
            if (max <= min) return 0f;
            return (v - (float)min) / (max - min) * 2f - 1f;
        }

        public override bool Read(out PadState s)
        {
            s = new PadState();
            var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX)), dwFlags = JOY_RETURNALL };
            if (joyGetPosEx(id, ref info) != 0) return false;
            uint b = info.dwButtons;
            s.Set(PadState.West, (b & 1) != 0); s.Set(PadState.South, (b & 2) != 0);
            s.Set(PadState.East, (b & 4) != 0); s.Set(PadState.North, (b & 8) != 0);
            s.Set(PadState.LeftShoulder, (b & 16) != 0); s.Set(PadState.RightShoulder, (b & 32) != 0);
            s.Set(PadState.Select, (b & 256) != 0); s.Set(PadState.Start, (b & 512) != 0);
            s.Set(PadState.LeftStick, (b & 1024) != 0); s.Set(PadState.RightStick, (b & 2048) != 0);
            s.LT = (b & 64) != 0 ? 1f : 0f;
            s.RT = (b & 128) != 0 ? 1f : 0f;
            s.LX = Norm(info.dwXpos, caps.wXmin, caps.wXmax);
            s.LY = -Norm(info.dwYpos, caps.wYmin, caps.wYmax);
            s.RX = Norm(info.dwZpos, caps.wZmin, caps.wZmax);
            s.RY = -Norm(info.dwRpos, caps.wRmin, caps.wRmax);
            uint pov = info.dwPOV;
            if (pov != 0xFFFF && pov <= 36000)
            {
                float deg = pov / 100f;
                s.Set(PadState.DpadUp, deg >= 315f || deg <= 45f);
                s.Set(PadState.DpadRight, deg >= 45f && deg <= 135f);
                s.Set(PadState.DpadDown, deg >= 135f && deg <= 225f);
                s.Set(PadState.DpadLeft, deg >= 225f && deg <= 315f);
            }
            return true;
        }
    }
}
