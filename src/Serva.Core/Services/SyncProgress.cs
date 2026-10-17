using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>执行过程中的进度快照（对应样稿「执行中」面板）。</summary>
public sealed record SyncProgress(
    int Total,
    int Completed,
    int Failed,
    int Skipped,
    long TotalBytes,
    long CopiedBytes,
    string CurrentRelativePath,
    TimeSpan Elapsed)
{
    public int Percent => Total <= 0 ? 0 : (int)Math.Round(Completed * 100.0 / Total);

    public double BytesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : CopiedBytes / Elapsed.TotalSeconds;

    /// <summary>预计剩余时间；无法估计时返回 <c>null</c>。</summary>
    public TimeSpan? Remaining
    {
        get
        {
            double speed = BytesPerSecond;
            long remainingBytes = TotalBytes - CopiedBytes;
            if (speed <= 1 || remainingBytes <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(remainingBytes / speed);
        }
    }
}

/// <summary>单项失败记录。</summary>
public sealed record SyncFailure(string RelativePath, string Message);

/// <summary>一次执行的结果汇总。</summary>
public sealed class SyncRunResult
{
    public int Completed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public long BytesCopied { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string JournalPath { get; init; } = string.Empty;

    public IReadOnlyList<SyncFailure> Failures { get; init; } = [];

    /// <summary>因取消 / 设备拔出而提前结束。</summary>
    public bool Canceled { get; init; }
}

/// <summary>预检结果：目标端剩余空间是否足够。</summary>
public sealed record SpaceCheck(long RequiredBytes, long FreeBytes)
{
    public bool Sufficient => FreeBytes >= RequiredBytes;

    public long Shortfall => Math.Max(0, RequiredBytes - FreeBytes);
}
