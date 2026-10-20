using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using ARMpulse.Core;

namespace ARMpulse.Windows;

internal sealed record Snapshot(DateTimeOffset Time, double? Cpu, ulong TotalMemory, ulong AvailableMemory,
    double? Download, double? Upload, string Battery, string Power, ProcessSample[] Processes, string? Warning)
{
    public double MemoryPercent => TotalMemory == 0 ? 0 : 100.0 * (TotalMemory - AvailableMemory) / TotalMemory;
}

internal sealed class SystemSampler
{
    private (ulong Idle, ulong Kernel, ulong User)? _cpu;
    private Dictionary<ProcessIdentity, (TimeSpan Cpu, long? Io, long Stamp)> _previous = [];
    private Dictionary<string, (long Down, long Up, long Stamp)> _network = [];
    private readonly Dictionary<ProcessIdentity, (string? Path, string Architecture)> _metadata = [];

    public Snapshot Sample()
    {
        double? cpu = null;
        var warnings = new List<string>();
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_cpu is { } old) cpu = Metrics.Cpu(idle, kernel, user, old.Idle, old.Kernel, old.User);
            _cpu = (idle, kernel, user);
        }
        else warnings.Add("CPU counters unavailable");
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref memory)) warnings.Add("Memory counters unavailable");
        var battery = "Unavailable";
        var power = "Power status unavailable";
        if (Native.GetSystemPowerStatus(out var status))
        {
            battery = status.BatteryFlag == 255 ? "Unavailable" : (status.BatteryFlag & 128) != 0 ? "No battery" : status.BatteryPercent <= 100 ? $"{status.BatteryPercent}%" : "Unavailable";
            power = status.AcLine switch { 1 => "Plugged in", 0 => "On battery", _ => "Power source unknown" };
            if ((status.BatteryFlag & 8) != 0) power += " · charging";
            if (status.SystemStatus == 1) power += " · battery saver";
        }
        var (down, up) = ReadNetwork();
        return new(DateTimeOffset.Now, cpu, memory.TotalPhysical, memory.AvailablePhysical, down, up,
            battery, power, ReadProcesses(), warnings.Count == 0 ? null : string.Join(" · ", warnings));
    }

    private (double? Down, double? Up) ReadNetwork()
    {
        double down = 0, up = 0;
        var comparable = false;
        Dictionary<string, (long Down, long Up, long Stamp)> next = [];
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var s = adapter.GetIPStatistics();
                    var stamp = Stopwatch.GetTimestamp();
                    next[adapter.Id] = (s.BytesReceived, s.BytesSent, stamp);
                    if (!_network.TryGetValue(adapter.Id, out var old)) continue;
                    var seconds = Stopwatch.GetElapsedTime(old.Stamp, stamp).TotalSeconds;
                    var d = Metrics.Rate(s.BytesReceived, old.Down, seconds);
                    var u = Metrics.Rate(s.BytesSent, old.Up, seconds);
                    if (d is null || u is null) continue;
                    down += d.Value; up += u.Value; comparable = true;
                }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { }
        _network = next;
        return comparable ? (down, up) : (null, null);
    }

    private ProcessSample[] ReadProcesses()
    {
        List<ProcessSample> result = [];
        Dictionary<ProcessIdentity, (TimeSpan Cpu, long? Io, long Stamp)> next = [];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var pid = process.Id;
                    var name = process.ProcessName;
                    using var handle = Native.OpenProcess(Native.Query, false, pid);
                    if (handle.IsInvalid || !Native.GetProcessTimes(handle, out var created, out _, out var k, out var u))
                    {
                        result.Add(new(new(pid, 0), name, null, "Unknown", null, null, null, null, "Restricted"));
                        continue;
                    }
                    var identity = new ProcessIdentity(pid, created);
                    var stamp = Stopwatch.GetTimestamp();
                    var time = TimeSpan.FromTicks(k + u);
                    long? io = Native.GetProcessIoCounters(handle, out var counters) ? checked((long)(counters.ReadBytes + counters.WriteBytes)) : null;
                    double? cpu = null, ioRate = null;
                    if (_previous.TryGetValue(identity, out var old))
                    {
                        var elapsed = Stopwatch.GetElapsedTime(old.Stamp, stamp).TotalSeconds;
                        cpu = Metrics.ProcessCpu(time, old.Cpu, elapsed, Environment.ProcessorCount);
                        if (io is { } now && old.Io is { } previous) ioRate = Metrics.Rate(now, previous, elapsed);
                    }
                    next[identity] = (time, io, stamp);
                    if (!_metadata.TryGetValue(identity, out var meta))
                    {
                        var buffer = new StringBuilder(32768);
                        var length = buffer.Capacity;
                        var path = Native.QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null;
                        var architecture = "Unknown";
                        if (Native.IsWow64Process2(handle, out var machine, out var native))
                        {
                            architecture = (machine == 0 ? native : machine) switch
                            {
                                0xAA64 => "ARM64", 0xA641 => "ARM64EC",
                                // ARM64EC can be reported as AMD64. Do not claim all its code is emulated.
                                0x8664 => native == 0xAA64 ? "x64 on ARM" : "x64",
                                0x014C => native == 0xAA64 ? "x86 on ARM" : "x86",
                                _ => "Unknown"
                            };
                        }
                        meta = (path, architecture);
                        _metadata[identity] = meta;
                    }
                    var mem = new Native.MemoryCounters { Size = (uint)Marshal.SizeOf<Native.MemoryCounters>() };
                    long? workingSet = Native.GetProcessMemoryInfo(handle, ref mem, mem.Size) ? checked((long)mem.WorkingSet) : null;
                    int? threads = null;
                    try { threads = process.Threads.Count; } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
                    result.Add(new(identity, name, meta.Path, meta.Architecture, cpu, workingSet, ioRate, threads, "Running"));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or OverflowException) { }
            }
        }
        _previous = next;
        foreach (var id in _metadata.Keys.Where(id => !next.ContainsKey(id)).ToArray()) _metadata.Remove(id);
        return result.ToArray();
    }
}
