using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VPB
{
    internal static class VpbNetVoiceWizardProbe
    {
        const int InsufficientBuffer = 122;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern int GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, int reserved);

        internal static string Check(int selectedPid, int port)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("TTSVoiceWizard");
                try
                {
                    int pid = 0;
                    for (int i = 0; i < processes.Length; i++)
                        if (selectedPid == 0 || processes[i].Id == selectedPid) pid = processes[i].Id;
                    if (pid == 0) return "Voice Wizard not found; start TTSVoiceWizard";
                    if (selectedPid == 0 && processes.Length > 1) return "Multiple Voice Wizard instances; select its PID in Advanced settings";
                    return IsListening(pid, port)
                        ? "Voice Wizard listener found on 127.0.0.1:" + port
                        : "Voice Wizard running; enable its OSC listener on port " + port;
                }
                finally { for (int i = 0; i < processes.Length; i++) processes[i].Dispose(); }
            }
            catch (Win32Exception e) { return "Voice Wizard listener check failed (Windows error " + e.NativeErrorCode + ")"; }
            catch (InvalidOperationException) { return "Voice Wizard process changed; checking again"; }
        }

        internal static bool IsListening(int pid, int port)
        {
            int size = 0;
            int error = GetExtendedUdpTable(IntPtr.Zero, ref size, false, 2, 1, 0);
            if (error != 0 && error != InsufficientBuffer) throw new Win32Exception(error);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int capacity = Math.Max(size, 4);
                IntPtr table = Marshal.AllocHGlobal(capacity);
                try
                {
                    size = capacity;
                    error = GetExtendedUdpTable(table, ref size, false, 2, 1, 0);
                    if (error == InsufficientBuffer) continue;
                    if (error != 0) throw new Win32Exception(error);
                    int count = Marshal.ReadInt32(table);
                    if (count < 0 || count > (capacity - 4) / 12) throw new InvalidOperationException("Invalid UDP table size");
                    for (int i = 0; i < count; i++)
                    {
                        int row = 4 + i * 12;
                        int address = Marshal.ReadInt32(table, row);
                        int localPort = (Marshal.ReadByte(table, row + 4) << 8) | Marshal.ReadByte(table, row + 5);
                        if ((address == 0 || address == 0x0100007f) && localPort == port && Marshal.ReadInt32(table, row + 8) == pid)
                            return true;
                    }
                    return false;
                }
                finally { Marshal.FreeHGlobal(table); }
            }
            throw new Win32Exception(InsufficientBuffer);
        }
    }
}
