// Reads the battery level of G-Wolves mice through the 2.4 GHz receiver or the cable, using Win32 HID.
// Own implementation of the protocols the official web configurator uses. Protocol descriptions were read
// (not copied) in github.com/quenTiago/windows-taskbar-battery and github.com/maximelonguepe/gwolves-battery-overlay.
//
// Three protocol families, one device speaks exactly one:
//
// 1) "Feature" (e.g. HSK Pro V2): vendor interface with 65-byte feature report (report ID + 64).
//    Request [3]=2 [4]=2 [6]=0x83. Reply via GetFeature:
//    [1] 0xA1 = mouse answered, 0xA0 = asleep/off; [6]=0x83, [7] 1 = charging, [8] percent.
//
// 2) "Legacy" (older models, e.g. HSK Pro, HTX): same interface, request [2]=2 [3]=0x8F [4]=1 wireless / 0 wired.
//    Reply: [1] 0xA1, [2]=2, [3]=0x8F, [5] 1 = charging, [6] percent.
//
// 3) "Compx" (e.g. Fenrir Pro with "Receiver RS"): vendor interface with 17-byte input/output reports, report ID 8.
//    Request [0]=8 [1]=cmd ... [16]=checksum, reply comes as input report with [1]=cmd.
//    0x04 = battery: [6] percent, [7] charging, [8..9] millivolts.  0x01 = model ID at [11] (shared receiver).
//    No reply within the timeout = mouse is asleep or off.
//
// Only read commands are ever sent. Nothing that changes settings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MouseBattery
{
    public enum MouseState { Awake, Asleep, NotFound, Error }

    public class BatteryReading
    {
        public MouseState State;
        public int Percent;
        public bool Charging;
        public string Name = "";
        public string Product = "";
        public string Info = "";
        public override string ToString() => $"{Name}: {State} {Percent}% charging={Charging} [{Product}] {Info}";
    }

    public class HidInterface
    {
        public string Path;
        public ushort Pid;
        public string Product;
        public bool IsCompx; // otherwise 65-byte feature interface (Feature or Legacy)
    }

    public static class GWolvesHid
    {
        // ---------- Win32 ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct HIDP_CAPS
        {
            public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            public fixed ushort Reserved[17];
            public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices; public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices; public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder s, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int needed, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_RW = 0xC0000000, SHARE_RW = 0x3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

        static List<string> HidPaths()
        {
            var paths = new List<string>();
            HidD_GetHidGuid(out Guid g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return paths;
            try
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(needed);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, needed, out needed, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(buf + 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return paths;
        }

        static SafeFileHandle Open(string path, bool overlapped = false)
        {
            uint flags = overlapped ? FILE_FLAG_OVERLAPPED : 0;
            var h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
            if (h.IsInvalid && !overlapped) { h.Dispose(); h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero); }
            return h;
        }

        static string ProductName(SafeFileHandle h)
        {
            var s = new StringBuilder(128);
            return HidD_GetProductString(h, s, 256) ? s.ToString() : "";
        }

        // Vendor-specific interfaces of all connected G-Wolves devices.
        public static List<HidInterface> FindInterfaces()
        {
            var found = new List<HidInterface>();
            string id = "vid_" + Devices.VendorId.ToString("x4");
            foreach (string path in HidPaths())
            {
                if (path.ToLowerInvariant().IndexOf(id) < 0) continue;
                using var h = Open(path);
                if (h.IsInvalid) continue;
                var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf<HIDD_ATTRIBUTES>();
                if (!HidD_GetAttributes(h, ref a) || a.VendorID != Devices.VendorId) continue;
                if (!HidD_GetPreparsedData(h, out IntPtr pp)) continue;
                var caps = new HIDP_CAPS();
                try { HidP_GetCaps(pp, ref caps); } finally { HidD_FreePreparsedData(pp); }
                if (caps.UsagePage < 0xFF00) continue; // vendor-defined interfaces only

                bool? compx = null;
                if (caps.FeatureReportByteLength >= 65) compx = false;
                else if (caps.InputReportByteLength == 17 && caps.OutputReportByteLength == 17) compx = true;
                if (compx == null) continue;

                found.Add(new HidInterface { Path = path, Pid = a.ProductID, Product = ProductName(h), IsCompx = compx.Value });
            }
            return found;
        }

        // Model ID per shared-receiver path (asked once, while the mouse is awake)
        static readonly Dictionary<string, int> modelIdCache = new(StringComparer.OrdinalIgnoreCase);

        public static string DisplayName(HidInterface s, IDictionary<string, string> customNames)
        {
            string pid = s.Pid.ToString("X4");
            if (customNames != null && customNames.TryGetValue(pid, out string n1) && !string.IsNullOrWhiteSpace(n1)) return n1;
            if (s.Pid == Devices.SharedReceiverPid && modelIdCache.TryGetValue(s.Path, out int mid)
                && Devices.SharedReceiverModels.TryGetValue(mid, out string n2)) return n2;
            if (Devices.ByPid.TryGetValue(s.Pid, out var m) && m.Name != null) return m.Name;

            // Unknown device: clean up the USB product name
            string name = s.Product ?? "";
            foreach (string cut in new[] { "G-Wolves", "Wireless Mouse", "Receiver", "Dongle", "Wireless", "Mouse", "8K", "4K" })
            {
                int i = name.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) name = name.Remove(i, cut.Length);
            }
            name = name.Trim(' ', '-', '_');
            if (name.EndsWith("-N", StringComparison.OrdinalIgnoreCase)) name = name[..^2].Trim();
            if (name.Length < 2 || name.Equals("RS", StringComparison.OrdinalIgnoreCase)) name = "G-Wolves";
            return name;
        }

        // Query all mice. One result per mouse (receiver and cable of the same mouse are merged).
        public static List<BatteryReading> ReadAll(IDictionary<string, string> customNames = null)
        {
            var perMouse = new Dictionary<string, BatteryReading>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in FindInterfaces())
            {
                BatteryReading r;
                try { r = s.IsCompx ? ReadCompx(s) : ReadFeatureOrLegacy(s); }
                catch (Exception e) { r = new BatteryReading { State = MouseState.Error, Info = e.Message }; }
                r.Name = DisplayName(s, customNames);
                r.Product = s.Product;
                if (!perMouse.TryGetValue(r.Name, out var before) || Rank(r) > Rank(before)) perMouse[r.Name] = r;
            }
            return perMouse.Values.ToList();
        }

        static int Rank(BatteryReading s) => s.State switch { MouseState.Awake => 3, MouseState.Asleep => 2, MouseState.Error => 1, _ => 0 };

        // ---------- Feature (0x83) and Legacy (0x8F) ----------
        static BatteryReading ReadFeatureOrLegacy(HidInterface s)
        {
            using var h = Open(s.Path);
            if (h.IsInvalid) return new BatteryReading { State = MouseState.Error, Info = "cannot open" };

            Devices.ByPid.TryGetValue(s.Pid, out var model);
            if (model != null && model.Protocol == Protocol.Legacy) return ReadLegacy(h, model.Wired);
            if (model != null) return ReadFeature(h);

            // Unknown model: try the newer protocol first, then the older one (wireless, then wired)
            var r = ReadFeature(h);
            if (r.State != MouseState.Error) return r;
            var l = ReadLegacy(h, false);
            if (l.State != MouseState.Error) return l;
            l = ReadLegacy(h, true);
            return l.State != MouseState.Error ? l : r;
        }

        static BatteryReading ReadFeature(SafeFileHandle h)
        {
            var r = new BatteryReading { State = MouseState.Error };
            var req = new byte[65];
            req[3] = 2; req[4] = 2; req[6] = 0x83;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!HidD_SetFeature(h, req, req.Length)) { r.Info = "send failed (" + Marshal.GetLastWin32Error() + ")"; return r; }
                Thread.Sleep(80); // receiver fetches the answer from the mouse over the air
                var ans = new byte[65];
                if (!HidD_GetFeature(h, ans, ans.Length)) { r.Info = "read failed"; continue; }
                if (ans[6] != 0x83) { r.Info = "unexpected: " + BitConverter.ToString(ans, 0, 10); continue; }
                if (ans[1] == 0xA0) { r.State = MouseState.Asleep; r.Info = ""; return r; }
                if (ans[1] == 0xA1 && ans[8] >= 1 && ans[8] <= 100)
                {
                    r.State = MouseState.Awake; r.Percent = ans[8]; r.Charging = ans[7] == 1; r.Info = "";
                    return r;
                }
                r.Info = "no value: " + BitConverter.ToString(ans, 0, 10);
            }
            return r;
        }

        static BatteryReading ReadLegacy(SafeFileHandle h, bool wired)
        {
            var r = new BatteryReading { State = MouseState.Error };
            var req = new byte[65];
            req[2] = 2; req[3] = 0x8F; req[4] = (byte)(wired ? 0 : 1);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!HidD_SetFeature(h, req, req.Length)) { r.Info = "send failed (" + Marshal.GetLastWin32Error() + ")"; return r; }
                for (int wait = 0; wait < 6; wait++)
                {
                    Thread.Sleep(50);
                    var ans = new byte[65];
                    if (!HidD_GetFeature(h, ans, ans.Length)) { r.Info = "read failed"; break; }
                    if (ans[3] != 0x8F) { r.Info = "unexpected: " + BitConverter.ToString(ans, 0, 10); continue; }
                    if (ans[1] == 0xA1 && ans[6] >= 1 && ans[6] <= 100)
                    {
                        r.State = MouseState.Awake; r.Percent = ans[6]; r.Charging = ans[5] == 1; r.Info = "";
                        return r;
                    }
                    if (ans[1] == 0xA0) { r.State = MouseState.Asleep; r.Info = ""; }
                    else r.Info = "no value: " + BitConverter.ToString(ans, 0, 10);
                    if (ans[1] > 0xA1) break; // busy: send again
                }
                if (r.State == MouseState.Asleep) return r;
            }
            return r;
        }

        // ---------- Compx ----------
        static byte[] CompxRequest(byte cmd, params (int index, byte value)[] extra)
        {
            var b = new byte[17];
            b[0] = 8; b[1] = cmd;
            foreach (var (i, v) in extra) b[i] = v;
            int sum = 0;
            for (int i = 1; i < 16; i++) sum += b[i];
            b[16] = (byte)((85 - sum - 8) & 0xFF); // checksum as used by the configurator
            return b;
        }

        // Sends one Compx request and waits for the matching input report. null = no answer (asleep/off).
        static byte[] CompxAsk(FileStream fs, byte[] req, int timeoutMs)
        {
            var buf = new byte[17];
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            // Start reading before writing: the answer can arrive before the write returns.
            var read = fs.ReadAsync(buf, 0, 17, cancel.Token);
            fs.Write(req, 0, req.Length);
            fs.Flush();
            try
            {
                while (true)
                {
                    int n = read.GetAwaiter().GetResult();
                    if (n >= 12 && buf[0] == 8 && buf[1] == req[1]) return buf;
                    read = fs.ReadAsync(buf, 0, 17, cancel.Token); // other input report: keep reading
                }
            }
            catch (OperationCanceledException) { return null; }
        }

        static BatteryReading ReadCompx(HidInterface s)
        {
            var r = new BatteryReading { State = MouseState.Error };
            using var h = Open(s.Path, overlapped: true);
            if (h.IsInvalid) { r.Info = "cannot open (" + Marshal.GetLastWin32Error() + ")"; return r; }
            using var fs = new FileStream(h, FileAccess.ReadWrite, 17, isAsync: true);

            var ans = CompxAsk(fs, CompxRequest(0x04), 600);
            if (ans == null) { r.State = MouseState.Asleep; r.Info = "no answer (asleep or off)"; return r; }
            int p = ans[6];
            if (p < 1 || p > 100) { r.Info = "no value: " + BitConverter.ToString(ans, 0, 10); return r; }
            r.State = MouseState.Awake; r.Percent = p; r.Charging = ans[7] == 1;
            r.Info = "voltage " + ((ans[8] << 8) + ans[9]) + " mV";

            // Shared receiver: ask once which model is paired (read-only)
            if (s.Pid == Devices.SharedReceiverPid && !modelIdCache.ContainsKey(s.Path))
            {
                var rnd = new byte[4]; Random.Shared.NextBytes(rnd);
                var id = CompxAsk(fs, CompxRequest(0x01, (5, 8), (6, rnd[0]), (7, rnd[1]), (8, rnd[2]), (9, rnd[3])), 600);
                if (id != null && id[11] != 0) { modelIdCache[s.Path] = id[11]; r.Info += ", model ID " + id[11]; }
            }
            return r;
        }
    }
}
