using ARMpulse.Core;
using Xunit;

namespace ARMpulse.Tests;

public sealed class ProcessQueryTests
{
    private static readonly ProcessSample[] Processes =
    [
        new(new(123, 10), "Editor", "C:\\Apps\\Editor.exe", "ARM64", 8, 1024L * 1024 * 1024, 1, 2, "Running"),
        new(new(456, 20), "Browser", "C:\\Browser.exe", "x64 on ARM", 1, 128 * 1024 * 1024, null, 4, "Running"),
        new(new(789, 0), "System", null, "Unknown", null, null, null, null, "Restricted")
    ];
    [Theory] [InlineData("eDiToR", 123)] [InlineData("456", 456)] [InlineData("apps", 123)]
    public void SearchMatchesNamePidAndPath(string query, int pid) => Assert.Equal(pid, Assert.Single(ProcessQuery.Apply(Processes, query, "All processes")).Pid);
    [Fact] public void CompatibilityFilterExcludesUnknownAndNative() => Assert.Equal(456, Assert.Single(ProcessQuery.Apply(Processes, "", "Compatibility")).Pid);
    [Fact] public void HighCpuExcludesUnavailable() => Assert.Equal(123, Assert.Single(ProcessQuery.Apply(Processes, "", "High CPU")).Pid);
    [Fact] public void HighMemoryUsesByteThreshold() => Assert.Equal(123, Assert.Single(ProcessQuery.Apply(Processes, "", "High memory")).Pid);
    [Fact] public void QueryAndFilterIntersect() => Assert.Empty(ProcessQuery.Apply(Processes, "Editor", "Compatibility"));
    [Fact] public void AllIncludesRestrictedProcesses() => Assert.Equal(3, ProcessQuery.Apply(Processes, "", "All processes").Count());
    [Fact] public void ProcessIdentityIncludesStartTime() => Assert.NotEqual(new ProcessIdentity(123, 1), new ProcessIdentity(123, 2));
    [Fact] public void Arm64EcIsNotLabeledEntirelyEmulated() => Assert.False((Processes[0] with { Architecture = "ARM64EC" }).IsCompatibilityProcess);
}
