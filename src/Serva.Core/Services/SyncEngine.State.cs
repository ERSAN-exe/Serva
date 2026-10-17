using System.Collections.Concurrent;
using System.Diagnostics;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary><see cref="SyncEngine"/> 的执行状态与进度上报（partial）。</summary>
public sealed partial class SyncEngine
{
    /// <summary>执行期间的共享状态（由并发 worker 读写）。</summary>
    private sealed class RunState
    {
        public required SyncOptions Options { get; init; }

        public required string LeftRoot { get; init; }

        public required string RightRoot { get; init; }

        public required Journal Journal { get; init; }

        public required ConcurrentBag<SyncFailure> Failures { get; init; }

        public required Stopwatch Stopwatch { get; init; }

        public required int Total { get; init; }

        public required long TotalBytes { get; init; }

        public IProgress<SyncProgress>? Progress { get; init; }

        public int Completed;
        public int Failed;
        public int Skipped;
        public long CopiedBytes;
        public long LastReport;
        public string CurrentFile = string.Empty;
    }

    /// <summary>上报进度；<paramref name="force"/> 为 false 时限流到每 250ms 一次。</summary>
    private static void Report(RunState state, bool force)
    {
        if (state.Progress is null)
        {
            return;
        }

        long now = state.Stopwatch.ElapsedTicks;
        long previous = Interlocked.Read(ref state.LastReport);

        if (force)
        {
            Interlocked.Exchange(ref state.LastReport, now);
        }
        else
        {
            if (now - previous < Stopwatch.Frequency / 4)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref state.LastReport, now, previous) != previous)
            {
                return;
            }
        }

        state.Progress.Report(new SyncProgress(
            state.Total,
            Volatile.Read(ref state.Completed),
            Volatile.Read(ref state.Failed),
            Volatile.Read(ref state.Skipped),
            state.TotalBytes,
            Interlocked.Read(ref state.CopiedBytes),
            state.CurrentFile,
            state.Stopwatch.Elapsed));
    }
}
