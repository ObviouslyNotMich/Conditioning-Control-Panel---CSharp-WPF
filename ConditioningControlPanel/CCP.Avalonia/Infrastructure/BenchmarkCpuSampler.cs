using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Infrastructure;

/// <summary>
/// Samples process CPU usage over an interval by comparing
/// <see cref="Process.TotalProcessorTime"/> snapshots.
/// </summary>
public static class BenchmarkCpuSampler
{
    public sealed class Result
    {
        public double AverageCpuPercent { get; set; }
        public double PeakCpuPercent { get; set; }
    }

    public static async Task<Result> SampleAsync(TimeSpan duration, TimeSpan sampleInterval, CancellationToken cancellationToken = default)
    {
        using var proc = Process.GetCurrentProcess();
        var processorCount = Environment.ProcessorCount;
        var sw = Stopwatch.StartNew();

        proc.Refresh();
        var lastCpu = proc.TotalProcessorTime;
        var lastWall = sw.Elapsed;

        var sum = 0.0;
        var peak = 0.0;
        var samples = 0;

        while (sw.Elapsed < duration && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(sampleInterval, cancellationToken).ConfigureAwait(false);

            proc.Refresh();
            var nowCpu = proc.TotalProcessorTime;
            var nowWall = sw.Elapsed;

            var cpuDelta = (nowCpu - lastCpu).TotalSeconds;
            var wallDelta = (nowWall - lastWall).TotalSeconds;
            if (wallDelta > 0)
            {
                // TotalProcessorTime is summed across all cores; normalize to 0-100%.
                var percent = cpuDelta / (wallDelta * processorCount) * 100.0;
                sum += percent;
                samples++;
                if (percent > peak) peak = percent;
            }

            lastCpu = nowCpu;
            lastWall = nowWall;
        }

        return new Result
        {
            AverageCpuPercent = samples > 0 ? sum / samples : 0,
            PeakCpuPercent = peak
        };
    }
}
