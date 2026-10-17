using System.Collections.Concurrent;
using System.Diagnostics;
using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 同步执行引擎。
/// <para>并发复制（<see cref="SyncOptions.Concurrency"/>）、<c>.serva-part</c> + 原子改名、
/// 复制后校验、保留时间戳、镜像删除移入系统回收站、全程写 JSONL 日志、可随时取消。</para>
/// </summary>
public sealed partial class SyncEngine
{
    private const int BufferSize = 1024 * 1024;
    private const string PartSuffix = ".serva-part";

    /// <summary>执行前预检：需要写入设备的字节数 vs 设备剩余空间。</summary>
    public static SpaceCheck CheckSpace(SyncPlan plan, string rightRoot)
    {
        long required = plan.Items
            .Where(static i => i.IsSelected && i.Action == PlanAction.CopyToDevice)
            .Sum(static i => i.Size);

        long free = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rightRoot)) ?? rightRoot);
            if (drive.IsReady)
            {
                free = drive.AvailableFreeSpace;
            }
        }
        catch (IOException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new SpaceCheck(required, free);
    }

    /// <summary>
    /// 执行计划。<paramref name="cancellationToken"/> 取消后立即停止派发新任务；
    /// 已完成的文件保留，不会留下半截文件。
    /// </summary>
    /// <param name="journal">
    /// 可选的共享日志。多条「本机文件夹 ↔ 设备文件夹」对应关系连续执行时传入同一个实例，
    /// 就能把一次运行记录进同一个 JSONL 文件，便于整体撤销。
    /// </param>
    public async Task<SyncRunResult> ExecuteAsync(
        SyncPlan plan,
        SyncOptions options,
        string leftRoot,
        string rightRoot,
        IProgress<SyncProgress>? progress = null,
        string? journalDirectory = null,
        CancellationToken cancellationToken = default,
        Journal? journal = null)
    {
        List<PlanItem> work = [.. plan.Items.Where(static i => i.IsSelected && i.IsActionable)];

        var state = new RunState
        {
            Options = options,
            LeftRoot = leftRoot,
            RightRoot = rightRoot,
            Journal = journal ?? new Journal(journalDirectory),
            Failures = new ConcurrentBag<SyncFailure>(),
            Progress = progress,
            Total = work.Count,
            TotalBytes = work.Where(static i => SyncPlan.IsWrite(i.Action)).Sum(static i => i.Size),
            Stopwatch = Stopwatch.StartNew(),
        };

        using var gate = new SemaphoreSlim(Math.Max(1, options.Concurrency), Math.Max(1, options.Concurrency));
        var tasks = new List<Task>(work.Count);
        bool canceled = false;

        Report(state, force: true);

        try
        {
            foreach (PlanItem item in work)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                // 设备被拔出：立即中止执行，已完成的部分保留（可在设置中关闭该行为）。
                if (options.AbortOnDeviceDisconnect
                    && item.Action is PlanAction.CopyToDevice or PlanAction.DeleteOnDevice
                    && !Directory.Exists(rightRoot))
                {
                    canceled = true;
                    state.Journal.Append(
                        JournalOperationKind.Error, item.RelativePath, string.Empty, rightRoot, 0, false,
                        CoreText.Get("Target device disconnected", "目标设备已断开"));
                    break;
                }

                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(ProcessItemAsync(state, item, gate, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        state.Stopwatch.Stop();
        if (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }

        Report(state, force: true);

        return new SyncRunResult
        {
            Completed = Volatile.Read(ref state.Completed),
            Failed = Volatile.Read(ref state.Failed),
            Skipped = Volatile.Read(ref state.Skipped),
            BytesCopied = Interlocked.Read(ref state.CopiedBytes),
            Elapsed = state.Stopwatch.Elapsed,
            JournalPath = state.Journal.FilePath,
            Failures = [.. state.Failures],
            Canceled = canceled,
        };
    }
}
