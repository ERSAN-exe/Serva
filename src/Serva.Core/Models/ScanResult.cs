namespace Serva.Core.Models;

/// <summary>一次扫描（本机来源或目标设备）的结果。</summary>
public sealed class ScanResult
{
    public ScanResult(
        string root,
        IReadOnlyDictionary<string, FileEntry> files,
        IReadOnlyList<string> directories,
        TimeSpan duration,
        DateTime capturedUtc,
        bool isFromSnapshot = false,
        IReadOnlyList<string>? collisions = null)
    {
        Root = root;
        Files = files;
        Directories = directories;
        Duration = duration;
        CapturedUtc = capturedUtc;
        IsFromSnapshot = isFromSnapshot;
        Collisions = collisions ?? [];
        TotalBytes = files.Values.Sum(static f => f.Length);
    }

    /// <summary>扫描根（文件或文件夹的绝对路径）。</summary>
    public string Root { get; }

    /// <summary>相对路径 → 文件条目。比较键为 <see cref="StringComparer.OrdinalIgnoreCase"/>。</summary>
    public IReadOnlyDictionary<string, FileEntry> Files { get; }

    /// <summary>扫描到的所有子目录的相对路径（用于空目录同步）。</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>扫描耗时。</summary>
    public TimeSpan Duration { get; }

    /// <summary>扫描完成时刻（UTC）。</summary>
    public DateTime CapturedUtc { get; }

    /// <summary>本次结果是否直接来自增量快照（命中即跳过读盘）。</summary>
    public bool IsFromSnapshot { get; }

    /// <summary>
    /// 多来源合并时出现的相对路径冲突（例如同时选了 <c>D:\a\x.txt</c> 与 <c>D:\b\x.txt</c>）。
    /// 冲突项保留第一次出现的来源。
    /// </summary>
    public IReadOnlyList<string> Collisions { get; }

    /// <summary>文件数量 —— 对应样稿里「文件数量」对比维度之一。</summary>
    public int FileCount => Files.Count;

    /// <summary>全部文件字节总和。</summary>
    public long TotalBytes { get; }
}
