namespace Serva.Core.Models;

/// <summary>比较结果中的一行（一个相对路径）。</summary>
public sealed class CompareItem
{
    public CompareItem(string relativePath, FileEntry? left, FileEntry? right, CompareStatus status)
    {
        RelativePath = relativePath;
        Left = left;
        Right = right;
        Status = status;
    }

    /// <summary>相对路径（/ 分隔）。</summary>
    public string RelativePath { get; }

    /// <summary>本机侧条目；仅设备存在时为 <c>null</c>。</summary>
    public FileEntry? Left { get; }

    /// <summary>设备侧条目；仅本机存在时为 <c>null</c>。</summary>
    public FileEntry? Right { get; }

    public CompareStatus Status { get; }

    /// <summary>表格中显示的字节数（优先取本机侧）。</summary>
    public long Size => Left?.Length ?? Right?.Length ?? 0;

    /// <summary>表格中显示的修改时间（优先取本机侧）。</summary>
    public DateTime? LastWriteTimeUtc => Left?.LastWriteTimeUtc ?? Right?.LastWriteTimeUtc;

    /// <summary>是否需要用户介入（冲突）。</summary>
    public bool NeedsAttention => Status == CompareStatus.Conflict;
}

/// <summary>
/// 两端比较的完整结果：逐项状态 + 各状态数量（样稿里的统计与筛选计数）。
/// </summary>
public sealed class CompareResult
{
    public CompareResult(
        ScanResult left,
        ScanResult right,
        IReadOnlyList<CompareItem> items,
        TimeSpan compareDuration)
    {
        Left = left;
        Right = right;
        Items = items;
        CompareDuration = compareDuration;

        var counts = new Dictionary<CompareStatus, int>();
        foreach (CompareStatus status in Enum.GetValues<CompareStatus>())
        {
            counts[status] = 0;
        }

        foreach (CompareItem item in items)
        {
            counts[item.Status]++;
        }

        Counts = counts;
        TotalCount = items.Count;
    }

    public ScanResult Left { get; }

    public ScanResult Right { get; }

    /// <summary>按相对路径排序后的比较项。</summary>
    public IReadOnlyList<CompareItem> Items { get; }

    public IReadOnlyDictionary<CompareStatus, int> Counts { get; }

    public int TotalCount { get; }

    public TimeSpan CompareDuration { get; }

    public int CountOf(CompareStatus status) => Counts.TryGetValue(status, out int value) ? value : 0;
}
