namespace Serva.Core.Models;

/// <summary>单个文件在一次扫描中的元数据快照。</summary>
/// <param name="FullPath">磁盘上的绝对路径。</param>
/// <param name="RelativePath">相对于扫描根的路径，使用 <c>/</c> 分隔（比较键）。</param>
/// <param name="Length">字节数。</param>
/// <param name="LastWriteTimeUtc">最后写入时间（UTC）。</param>
/// <param name="Attributes">文件属性。</param>
public sealed record FileEntry(
    string FullPath,
    string RelativePath,
    long Length,
    DateTime LastWriteTimeUtc,
    FileAttributes Attributes)
{
    /// <summary>隐藏或系统文件（对应「跳过隐藏 / 系统文件」策略）。</summary>
    public bool IsHiddenOrSystem =>
        (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>
    /// 轻量指纹（大小 + 修改时间 tick）。用于增量快照比对，
    /// 不读取文件内容，因此对 U 盘（exFAT）足够快。
    /// </summary>
    public string Fingerprint => string.Concat(Length, ":", LastWriteTimeUtc.Ticks);
}
