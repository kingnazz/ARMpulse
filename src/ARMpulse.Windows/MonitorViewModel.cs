using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ARMpulse.Core;

namespace ARMpulse.Windows;

internal sealed class MonitorViewModel : INotifyPropertyChanged
{
    private readonly History<Snapshot> _history = new(120);
    private Snapshot? _snapshot;
    private string _query = "";
    private string _filter = "All processes";
    private int _page;
    private bool _paused;
    private bool _topmost;
    private int _interval = 2;
    private ProcessSample? _selected;
    private string _message = "Collecting the first sample…";

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Query { get => _query; set { _query = value; RefreshProcesses(); Notify(); } }
    public string Filter { get => _filter; set { _filter = value; RefreshProcesses(); Notify(); } }
    public int PageIndex { get => _page; set { _page = value; Notify(); Notify(nameof(PageTitle)); Notify(nameof(PageSubtitle)); } }
    public bool Paused { get => _paused; set { _paused = value; Notify(); Notify(nameof(PauseText)); Notify(nameof(LiveText)); } }
    public bool AlwaysOnTop { get => _topmost; set { _topmost = value; Notify(); } }
    public int Interval { get => _interval; set { _interval = value is 1 or 2 or 5 ? value : 2; Notify(); } }
    public string Message { get => _message; set { _message = value; Notify(); } }
    public ProcessSample? Selected { get => _selected; set { _selected = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => Selected is not null;
    public ProcessCollection Processes { get; } = [];
    public IEnumerable<ProcessSample> AllProcesses => _snapshot?.Processes ?? [];
    public ProcessSample[] TopProcesses => AllProcesses.OrderByDescending(p => p.Cpu ?? -1).Take(5).ToArray();
    public string[] Filters { get; } = ["All processes", "Compatibility", "High CPU", "High memory"];
    public int[] Intervals { get; } = [1, 2, 5];
    public string PageTitle => PageIndex switch { 1 => "Processes", 2 => "Memory", 3 => "Session history", 4 => "Settings & about", _ => "Your Surface, at a glance." };
    public string PageSubtitle => PageIndex switch
    {
        1 => "Find what’s using your resources. Take back control.",
        2 => "Understand your memory before you close an app.",
        3 => "A little context for every spike. Kept only in this session.",
        4 => "Native Windows. Local data. Your machine.",
        _ => "Less noise. More insight into what’s happening right now."
    };
    public string PauseText => Paused ? "Resume" : "Pause";
    public string LiveText => Paused ? "PAUSED" : "LIVE";
    public string Cpu => Metrics.Percent(_snapshot?.Cpu);
    public string Memory => _snapshot is null || _snapshot.TotalMemory == 0 ? "—" : Metrics.Bytes(_snapshot.TotalMemory - _snapshot.AvailableMemory);
    public string MemoryDetail => _snapshot is null || _snapshot.TotalMemory == 0 ? "Waiting for memory counters" : $"of {Metrics.Bytes(_snapshot.TotalMemory)} · {_snapshot.MemoryPercent:0}% used";
    public string MemoryPercent => _snapshot?.TotalMemory > 0 ? $"{_snapshot.MemoryPercent:0.0}%" : "—";
    public double MemoryValue => _snapshot?.MemoryPercent ?? 0;
    public string TotalMemory => Metrics.Bytes(_snapshot?.TotalMemory);
    public string AvailableMemory => Metrics.Bytes(_snapshot?.AvailableMemory);
    public string Download => _snapshot?.Download is { } d ? Metrics.Bytes(d) + "/s" : "—";
    public string Upload => _snapshot?.Upload is { } u ? Metrics.Bytes(u) + "/s" : "—";
    public string Battery => _snapshot?.Battery ?? "—";
    public string Power => _snapshot?.Power ?? "Waiting for power status";
    public string Processor => $"{Environment.ProcessorCount} logical processors · {RuntimeInformation.OSArchitecture}";
    public string Machine => Environment.MachineName;
    public string Runtime => $"{RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture} app";
    public string ProcessCount => $"{AllProcesses.Count()} processes";
    public string FilterCount => $"{Processes.Count} shown · {AllProcesses.Count()} total";
    public string EmulatedCount => $"{AllProcesses.Count(p => p.IsCompatibilityProcess)} x64 / x86 on ARM";
    public string ArmCount => $"{AllProcesses.Count(p => p.Architecture is "ARM64" or "ARM64EC")} ARM native / hybrid";
    public string LastUpdate => _snapshot is null ? "Waiting for sample" : $"Updated {_snapshot.Time:HH:mm:ss}";
    public double[] CpuHistory => _history.Items.Select(s => s.Cpu ?? double.NaN).ToArray();
    public double[] MemoryHistory => _history.Items.Select(s => s.TotalMemory > 0 ? s.MemoryPercent : double.NaN).ToArray();
    public string HistoryRange => _history.Items is { Length: > 1 } items
        ? $"{items[0].Time:HH:mm:ss} — {items[^1].Time:HH:mm:ss} · {items.Length} samples"
        : "History starts after the first complete interval";
    public string CpuAverage => Summary(s => s.Cpu, false);
    public string CpuPeak => Summary(s => s.Cpu, true);
    public string MemoryPeak => Summary(s => s.TotalMemory > 0 ? s.MemoryPercent : null, true);
    public string InsightTitle => _snapshot is null ? "Getting to know your system" :
        _snapshot.MemoryPercent >= 85 ? "Memory is getting busy" :
        _snapshot.Cpu >= 80 ? "Your CPU is working hard" : "Room to breathe";
    public string InsightBody => _snapshot is null ? "Live counters will appear in a moment." :
        _snapshot.MemoryPercent >= 85 ? "Sort Processes by memory to find large working sets. Save your work before ending an app." :
        _snapshot.Cpu >= 80 ? "Check the busiest processes below. Sustained load matters more than a single spike." :
        "Use the Compatibility filter to find x64 and x86 processes on ARM. Native versions can improve efficiency; a compatibility process alone does not imply a problem.";

    private string Summary(Func<Snapshot, double?> select, bool peak)
    {
        var values = _history.Items.Select(select).OfType<double>().ToArray();
        return Metrics.Percent(values.Length == 0 ? null : peak ? values.Max() : values.Average());
    }

    public void Apply(Snapshot sample)
    {
        _snapshot = sample;
        _history.Add(sample);
        RefreshProcesses();
        Message = sample.Warning ?? "Local monitoring · no telemetry";
        Notify("");
    }

    private void RefreshProcesses()
    {
        var selection = Selected?.Identity;
        Processes.ReplaceAll(ProcessQuery.Apply(AllProcesses, Query, Filter));
        Selected = selection is { } id ? Processes.FirstOrDefault(p => p.Identity == id) : null;
        Notify(nameof(FilterCount));
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
