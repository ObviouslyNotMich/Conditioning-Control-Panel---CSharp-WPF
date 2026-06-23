using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Infrastructure;

/// <summary>
/// Measures the UI/compositor frame rate of a <see cref="TopLevel"/> by counting
/// <see cref="TopLevel.RequestAnimationFrame(Action{TimeSpan})"/> callbacks.
/// </summary>
public static class BenchmarkFrameCounter
{
    public sealed class Result
    {
        public int TotalFrames { get; set; }
        public double AverageFps { get; set; }
        public double MinimumFps { get; set; }
        public double MaximumFps { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public static async Task<Result> MeasureAsync(TopLevel topLevel, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        var frames = 0;
        var buckets = new List<int>();
        var bucketStart = sw.Elapsed;
        const double BucketSeconds = 1.0;
        var bucketFrames = 0;

        var tcs = new TaskCompletionSource();

        void OnFrame(TimeSpan _)
        {
            frames++;
            bucketFrames++;

            var now = sw.Elapsed;
            if ((now - bucketStart).TotalSeconds >= BucketSeconds)
            {
                buckets.Add(bucketFrames);
                bucketFrames = 0;
                bucketStart = now;
            }

            if (sw.Elapsed < duration)
            {
                topLevel.RequestAnimationFrame(OnFrame);
            }
            else
            {
                buckets.Add(bucketFrames);
                tcs.TrySetResult();
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() => topLevel.RequestAnimationFrame(OnFrame));
        await tcs.Task;

        var effectiveDuration = sw.Elapsed;
        var avgFps = effectiveDuration.TotalSeconds > 0 ? frames / effectiveDuration.TotalSeconds : 0;
        var perSecond = buckets.Select(c => (double)c / BucketSeconds).ToList();
        return new Result
        {
            TotalFrames = frames,
            AverageFps = avgFps,
            MinimumFps = perSecond.Any() ? perSecond.Min() : 0,
            MaximumFps = perSecond.Any() ? perSecond.Max() : 0,
            Duration = effectiveDuration
        };
    }
}
