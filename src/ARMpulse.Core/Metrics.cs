using System.Globalization;

namespace ARMpulse.Core;

public static class Metrics
{
    // Kernel time includes idle time in GetSystemTimes.
    public static double? Cpu(ulong idle, ulong kernel, ulong user, ulong oldIdle, ulong oldKernel, ulong oldUser)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        double total = (double)(kernel - oldKernel) + (user - oldUser);
        return total > 0 ? Math.Clamp(100 * (1 - (idle - oldIdle) / total), 0, 100) : null;
    }

    public static double? Rate(long current, long previous, double seconds) =>
        current >= previous && seconds > 0 ? (current - previous) / seconds : null;

    public static double? ProcessCpu(TimeSpan current, TimeSpan previous, double seconds, int cores) =>
        current >= previous && seconds > 0 && cores > 0
            ? Math.Clamp((current - previous).TotalSeconds / seconds / cores * 100, 0, 100) : null;

    public static string Bytes(double? bytes)
    {
        if (bytes is null || !double.IsFinite(bytes.Value) || bytes < 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = bytes.Value;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return value.ToString(index > 0 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + units[index];
    }

    public static string Percent(double? value) => value is null ? "—" : $"{value:0.0}%";
}

public sealed class History<T>(int capacity)
{
    private readonly Queue<T> _items = capacity > 0 ? new(capacity) : throw new ArgumentOutOfRangeException(nameof(capacity));
    public void Add(T item) { if (_items.Count == capacity) _items.Dequeue(); _items.Enqueue(item); }
    public T[] Items => _items.ToArray();
}

public readonly record struct ProcessIdentity(int Pid, long StartTicks);

public sealed record ProcessSample(ProcessIdentity Identity, string Name, string? Path, string Architecture,
    double? Cpu, long? Memory, double? IoBytesPerSecond, int? Threads, string Status)
{
    public int Pid => Identity.Pid;
    public bool IsCompatibilityProcess => Architecture is "x64 on ARM" or "x86 on ARM";
    public string CpuText => Metrics.Percent(Cpu);
    public string MemoryText => Metrics.Bytes(Memory);
    public string IoText => IoBytesPerSecond is null ? "—" : Metrics.Bytes(IoBytesPerSecond) + "/s";
}

public static class ProcessQuery
{
    public static IEnumerable<ProcessSample> Apply(IEnumerable<ProcessSample> processes, string query, string filter) =>
        processes.Where(p => (string.IsNullOrWhiteSpace(query) || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Pid.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (p.Path?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            && (filter switch
            {
                "Compatibility" => p.IsCompatibilityProcess,
                "High CPU" => p.Cpu >= 5,
                "High memory" => p.Memory >= 512L * 1024 * 1024,
                _ => true
            }));
}
