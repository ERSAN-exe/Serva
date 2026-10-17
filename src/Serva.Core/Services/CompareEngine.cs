using System.Diagnostics;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 两端比较引擎：按相对路径（不区分大小写）配对，判定 8 种状态并统计各状态数量。
/// </summary>
public sealed class CompareEngine
{
    /// <summary>修改时间容差 —— FAT / exFAT 的时间戳精度为 2 秒。</summary>
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    public CompareResult Compare(ScanResult left, ScanResult right, ConflictBaseline? baseline = null)
    {
        var stopwatch = Stopwatch.StartNew();

        var keys = new HashSet<string>(left.Files.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(right.Files.Keys);

        var items = new List<CompareItem>(keys.Count);
        foreach (string key in keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))
        {
            left.Files.TryGetValue(key, out FileEntry? leftEntry);
            right.Files.TryGetValue(key, out FileEntry? rightEntry);
            items.Add(new CompareItem(key, leftEntry, rightEntry, DetermineStatus(key, leftEntry, rightEntry, baseline)));
        }

        stopwatch.Stop();

        // 扫描耗时取两侧较大值（一先一后，用户关心的是总耗时）。
        return new CompareResult(left, right, items, stopwatch.Elapsed);
    }

    /// <summary>
    /// 判定单个相对路径的状态。
    /// <list type="number">
    /// <item>只在单侧存在 → 仅本机 / 仅设备。</item>
    /// <item>长度相同且时间戳在容差内 → 相同。</item>
    /// <item>有基线且两端都相对基线变化 → 冲突（两端都改过）。</item>
    /// <item>长度不同但时间戳落在同一容差窗口 → 冲突（无法自动判定）。</item>
    /// <item>否则 → 时间戳较新的一侧。</item>
    /// </list>
    /// </summary>
    public static CompareStatus DetermineStatus(
        string relativePath,
        FileEntry? left,
        FileEntry? right,
        ConflictBaseline? baseline = null)
    {
        if (right is null && left is null)
        {
            return CompareStatus.Same;
        }

        if (right is null)
        {
            return CompareStatus.LeftOnly;
        }

        if (left is null)
        {
            return CompareStatus.RightOnly;
        }

        long delta = Math.Abs(left.LastWriteTimeUtc.Ticks - right.LastWriteTimeUtc.Ticks);
        bool withinTolerance = delta <= TimestampTolerance.Ticks;

        if (left.Length == right.Length && withinTolerance)
        {
            return CompareStatus.Same;
        }

        if (baseline is not null && baseline.BothChanged(relativePath, left, right))
        {
            return CompareStatus.Conflict;
        }

        if (withinTolerance)
        {
            return CompareStatus.Conflict;
        }

        return left.LastWriteTimeUtc > right.LastWriteTimeUtc
            ? CompareStatus.LeftNewer
            : CompareStatus.RightNewer;
    }
}
