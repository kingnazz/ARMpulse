using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ARMpulse.Core;
using Microsoft.Win32;

namespace ARMpulse.Windows;

public partial class MainWindow : Window
{
    private readonly MonitorViewModel _vm = new();
    private readonly SystemSampler _sampler = new();
    private readonly DispatcherTimer _timer = new();
    private bool _sampling, _closed;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARMpulse", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        DataContext = _vm;
        CollectionViewSource.GetDefaultView(_vm.Processes).SortDescriptions.Add(new(nameof(ProcessSample.Cpu), ListSortDirection.Descending));
        ProcessGrid.Columns[2].SortDirection = ListSortDirection.Descending;
        _timer.Tick += async (_, _) => await SampleAsync();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MonitorViewModel.Interval) or nameof(MonitorViewModel.AlwaysOnTop)) SaveSettings();
            if (e.PropertyName is nameof(MonitorViewModel.Interval) or nameof(MonitorViewModel.Paused)) UpdateTimer();
        };
        StateChanged += (_, _) => UpdateTimer();
        Loaded += async (_, _) => { UpdateTimer(); await SampleAsync(); };
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            { _vm.PageIndex = 1; SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Space)
            { _vm.Paused = !_vm.Paused; e.Handled = true; }
            else if (e.Key == Key.F5) { await SampleAsync(); e.Handled = true; }
        };
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void UpdateTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(WindowState == WindowState.Minimized ? Math.Max(5, _vm.Interval) : _vm.Interval);
        if (!_vm.Paused && !_closed) _timer.Start();
    }

    private async Task SampleAsync()
    {
        if (_sampling || _vm.Paused || _closed) return;
        _sampling = true;
        try
        {
            var sample = await Task.Run(_sampler.Sample);
            if (!_closed && !_vm.Paused)
            {
                _vm.Apply(sample);
            }
        }
        catch (Exception error) { if (!_closed) _vm.Message = "Sampling failed: " + error.Message + " · will retry on the next interval"; }
        finally { _sampling = false; }
    }

    private void ProcessGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in ProcessGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        var view = CollectionViewSource.GetDefaultView(_vm.Processes);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(e.Column.SortMemberPath, direction));
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _vm.Paused = !_vm.Paused;
    private void ViewProcesses_Click(object sender, RoutedEventArgs e) => _vm.PageIndex = 1;
    private void HighMemory_Click(object sender, RoutedEventArgs e) { _vm.Filter = "High memory"; _vm.PageIndex = 1; }

    private async void EndProcess_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } process) return;
        if (MessageBox.Show(this, $"Force end {process.Name} (PID {process.Pid})?\n\nUnsaved work in this process will be lost. Child processes are not ended automatically.", "End process?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { ProcessActions.End(process.Identity); _vm.Message = $"Ended {process.Name}."; await SampleAsync(); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { ShowActionError(error); }
    }

    private void LowerPriority_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } process) return;
        if (MessageBox.Show(this, $"Set {process.Name} to Below Normal CPU priority?\n\nIt may respond more slowly while other apps are busy. Windows resets this when the process exits.", "Lower CPU priority?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { ProcessActions.LowerPriority(process.Identity); _vm.Message = $"{process.Name}: CPU priority set to Below Normal."; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { ShowActionError(error); }
    }

    private void ShowActionError(Exception error) => MessageBox.Show(this, error.Message + "\n\nThe process may have exited or Windows may restrict access. ARMpulse does not elevate itself.", "Action unavailable", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.Selected?.Path;
        if (path is null) { _vm.Message = "Windows did not provide an executable path for this process."; return; }
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is null || !Directory.Exists(folder)) throw new IOException("Executable folder no longer exists.");
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { folder } });
        }
        catch (Exception error) when (error is Win32Exception or IOException or ArgumentException) { ShowActionError(error); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = $"ARMpulse-processes-{DateTime.Now:yyyyMMdd-HHmmss}.csv", Filter = "CSV files (*.csv)|*.csv", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        // Freeze this sample and retain the visible sort order.
        var rows = CollectionViewSource.GetDefaultView(_vm.Processes).Cast<ProcessSample>().ToArray();
        try
        {
            var lines = new List<string> { "Name,PID,CPU percent,Working set bytes,IO bytes per second,Architecture,Path,Access" };
            foreach (var p in rows) lines.Add(string.Join(",", Csv.Field(p.Name), p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                p.Cpu?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "", p.Memory?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                p.IoBytesPerSecond?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "", Csv.Field(p.Architecture), Csv.Field(p.Path), Csv.Field(p.Status)));
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            _vm.Message = $"Exported {rows.Length} processes. Executable paths are included.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _vm.Message = "Export failed: " + error.Message; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath));
            if (settings is not null) { _vm.Interval = settings.Interval; _vm.AlwaysOnTop = settings.AlwaysOnTop; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { _vm.Message = "Preferences could not be read; using defaults."; }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(_vm.Interval, _vm.AlwaysOnTop)));
            File.Move(temporary, SettingsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _vm.Message = "Preferences could not be saved: " + error.Message; }
    }

    private sealed record Preferences(int Interval, bool AlwaysOnTop);
}
