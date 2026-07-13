using System.Runtime.InteropServices;

namespace AIClockBridge;

// System-wide CPU% and memory-used% for the net-speed page (device + mirror).
// CPU comes from GetSystemTimes deltas between calls; memory is
// GlobalMemoryStatusEx's dwMemoryLoad. Recomputes at most once per second.
static class SystemStatsMonitor
{
    static readonly object Lock = new();
    static ulong _lastBusy, _lastIdle;
    static bool _hasLast;
    static (int Cpu, int Mem) _cached;
    static DateTime _cachedAt = DateTime.MinValue;

    public static (int Cpu, int Mem) Snapshot()
    {
        lock (Lock)
        {
            if ((DateTime.UtcNow - _cachedAt).TotalSeconds < 1.0) return _cached;
            _cachedAt = DateTime.UtcNow;

            if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                var idle = ToUlong(idleFt);
                // kernel time includes idle time, so busy = (kernel - idle) + user
                var busy = ToUlong(kernelFt) - idle + ToUlong(userFt);
                if (_hasLast)
                {
                    var dBusy = busy - _lastBusy;
                    var dTotal = dBusy + (idle - _lastIdle);
                    if (dTotal > 0) _cached.Cpu = (int)Math.Round(dBusy * 100.0 / dTotal);
                }
                _lastBusy = busy;
                _lastIdle = idle;
                _hasLast = true;
            }

            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem)) _cached.Mem = (int)mem.dwMemoryLoad;
            return _cached;
        }
    }

    static ulong ToUlong(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad; // % of physical memory in use
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
