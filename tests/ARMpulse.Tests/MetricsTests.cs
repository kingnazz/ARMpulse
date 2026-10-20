using ARMpulse.Core;
using Xunit;

namespace ARMpulse.Tests;

public sealed class MetricsTests
{
    [Fact] public void CpuSubtractsIdleFromKernelInclusiveTime() => Assert.Equal(75, Metrics.Cpu(200, 400, 300, 100, 100, 200));
    [Fact] public void FullyIdleCpuIsZero() => Assert.Equal(0, Metrics.Cpu(100, 100, 0, 0, 0, 0));
    [Fact] public void BusyCpuIsOneHundred() => Assert.Equal(100, Metrics.Cpu(0, 100, 100, 0, 0, 0));
    [Fact] public void ZeroElapsedCpuTicksAreUnavailable() => Assert.Null(Metrics.Cpu(1, 2, 3, 1, 2, 3));
    [Theory]
    [InlineData(0, 5, 5, 1, 4, 4)]
    [InlineData(1, 3, 5, 0, 4, 4)]
    [InlineData(1, 5, 3, 0, 4, 4)]
    public void ResetCpuCountersAreUnavailable(ulong idle, ulong kernel, ulong user, ulong oldIdle, ulong oldKernel, ulong oldUser) =>
        Assert.Null(Metrics.Cpu(idle, kernel, user, oldIdle, oldKernel, oldUser));
    [Fact] public void ProcessCpuUsesTotalMachineCapacity() => Assert.Equal(100.0 / 12, Metrics.ProcessCpu(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 2, 12)!.Value, 6);
    [Fact] public void ProcessCpuIsClamped() => Assert.Equal(100, Metrics.ProcessCpu(TimeSpan.FromSeconds(50), TimeSpan.Zero, 1, 1));
    [Fact] public void ExitedOrResetProcessCpuIsUnavailable() => Assert.Null(Metrics.ProcessCpu(TimeSpan.Zero, TimeSpan.FromSeconds(1), 1, 12));
    [Theory] [InlineData(0, 4)] [InlineData(-1, 4)] [InlineData(1, 0)]
    public void InvalidCpuWindowIsUnavailable(double seconds, int cores) => Assert.Null(Metrics.ProcessCpu(TimeSpan.FromSeconds(1), TimeSpan.Zero, seconds, cores));
    [Fact] public void ByteRateUsesActualElapsedSeconds() => Assert.Equal(500, Metrics.Rate(1600, 100, 3));
    [Fact] public void CounterResetIsNotNegativeTraffic() => Assert.Null(Metrics.Rate(1, 100, 1));
    [Theory] [InlineData(0)] [InlineData(-1)] public void InvalidRateIntervalIsUnavailable(double seconds) => Assert.Null(Metrics.Rate(2, 1, seconds));
    [Fact] public void IdleTrafficIsZero() => Assert.Equal(0, Metrics.Rate(100, 100, 2));
    [Fact] public void MissingAndInvalidBytesStayUnavailable()
    {
        Assert.Equal("—", Metrics.Bytes(null)); Assert.Equal("—", Metrics.Bytes(double.NaN)); Assert.Equal("—", Metrics.Bytes(-1));
    }
    [Fact] public void ByteFormattingScalesToGigabytes() => Assert.EndsWith(" GB", Metrics.Bytes(32L * 1024 * 1024 * 1024));
    [Fact] public void HistoryRetainsOnlyLatestSamples()
    {
        var history = new History<int>(3);
        foreach (var item in Enumerable.Range(1, 1000)) history.Add(item);
        Assert.Equal([998, 999, 1000], history.Items);
    }
    [Fact] public void HistorySnapshotsDoNotMutateInternalState()
    {
        var history = new History<int>(1); history.Add(5); history.Items[0] = 99; Assert.Equal(5, history.Items[0]);
    }
    [Fact] public void HistoryRejectsInvalidCapacity() => Assert.Throws<ArgumentOutOfRangeException>(() => new History<int>(0));
    [Theory] [InlineData("=CMD()")] [InlineData(" +1")] [InlineData("-2")] [InlineData("@formula")] [InlineData("\tcmd")]
    public void CsvNeutralizesSpreadsheetFormulas(string value) => Assert.StartsWith("\"'", Csv.Field(value));
    [Fact] public void CsvEscapesQuotesAndPreservesCommasAndNewlines() => Assert.Equal("\"a,\"\"b\"\"\nc\"", Csv.Field("a,\"b\"\nc"));
    [Fact] public void CsvMissingValueIsEmptyField() => Assert.Equal("\"\"", Csv.Field(null));
}
