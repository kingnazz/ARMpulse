using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using ARMpulse.Core;

namespace ARMpulse.Windows;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 1 && e.Args[0] == "--smoke-child")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await Task.Delay(TimeSpan.FromSeconds(30));
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = 0;
            try
            {
                var evidence = await Task.Run(RunSmokeTest);
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error)
            {
                code = 1;
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
            }
            Shutdown(code);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--ui-test")
        {
            var listener = new TextWriterTraceListener(e.Args[1]);
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            Exit += (_, _) => { listener.Flush(); listener.Close(); };
        }
        else if (e.Args.Length > 0)
        {
            MessageBox.Show("Usage: ARMpulse.exe [--smoke-test output.json]", "ARMpulse");
            Shutdown(2);
            return;
        }
        new MainWindow().Show();
    }

    private static object RunSmokeTest()
    {
        var sampler = new SystemSampler();
        sampler.Sample();
        Thread.Sleep(450);
        var sample = sampler.Sample();
        if (sample.TotalMemory == 0 || sample.AvailableMemory > sample.TotalMemory || sample.Cpu is null ||
            !double.IsFinite(sample.Cpu.Value) || sample.Processes.Length == 0 || sample.Warning is not null)
            throw new InvalidOperationException("Live Windows telemetry validation failed.");
        var self = sample.Processes.Single(p => p.Pid == Environment.ProcessId);
        if (self.Memory is not > 0 || self.Cpu is null || self.Path is null || self.Architecture == "Unknown")
            throw new InvalidOperationException("Current process counters or architecture unavailable.");

        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            ArgumentList = { "--smoke-child" }, UseShellExecute = false, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start smoke-test child.");
        try
        {
            using var handle = Native.OpenProcess(Native.Query, false, child.Id);
            if (handle.IsInvalid || !Native.GetProcessTimes(handle, out var created, out _, out _, out _))
                throw new InvalidOperationException("Could not inspect child process.");
            var id = new ProcessIdentity(child.Id, created);
            var staleRejected = false;
            try { ProcessActions.LowerPriority(id with { StartTicks = created + 1 }); }
            catch (InvalidOperationException) { staleRejected = true; }
            if (!staleRejected) throw new InvalidOperationException("Stale process identity was accepted.");
            ProcessActions.LowerPriority(id);
            child.Refresh();
            if (child.PriorityClass != ProcessPriorityClass.BelowNormal) throw new InvalidOperationException("Priority did not change.");
            ProcessActions.End(id);
            if (!child.WaitForExit(5000)) throw new InvalidOperationException("Child did not exit.");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
        return new { passed = true, sample.Time, sample.Cpu, sample.TotalMemory, processCount = sample.Processes.Length,
            architecture = self.Architecture, processActions = "priority, termination, and stale identity rejection passed" };
    }
}
