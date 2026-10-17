using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary><see cref="FileScanner"/> 的「多来源统一基准」部分（partial）。</summary>
public sealed partial class FileScanner
{
    /// <summary>
    /// 扫描多个来源，并把所有相对路径统一映射为「相对于 <paramref name="baseRoot"/>」。
    /// <para>这样无论选了 1 个还是多个来源，<c>目标路径 = 目标根 + 相对路径</c> 都成立。</para>
    /// <example>
    /// baseRoot = <c>D:\</c>，来源 = <c>D:\工作</c> → 相对路径 <c>工作\docs\a.txt</c>。
    /// </example>
    /// </summary>
    public ScanResult ScanSources(
        string baseRoot,
        IReadOnlyList<string> sources,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var merged = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();
        var collisions = new List<string>();

        foreach (string source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScanResult partial = Scan(source, null, progress, cancellationToken);

            // 文件来源要先退到它所在的目录，再把文件名拼回去。
            string sourceDirectory = Directory.Exists(source) ? source : Path.GetDirectoryName(source)!;
            string prefix = Path.GetRelativePath(baseRoot, sourceDirectory);
            prefix = prefix == "." ? string.Empty : prefix.Replace('\\', '/');

            foreach (string directory in partial.Directories)
            {
                directories.Add(MergeRelative(prefix, directory));
            }

            foreach ((string relativePath, FileEntry entry) in partial.Files)
            {
                string mapped = MergeRelative(prefix, relativePath);
                FileEntry adjusted = ReferenceEquals(mapped, relativePath) || mapped == relativePath
                    ? entry
                    : entry with { RelativePath = mapped };

                if (!merged.TryAdd(mapped, adjusted))
                {
                    collisions.Add(mapped);
                }
            }
        }

        stopwatch.Stop();
        return new ScanResult(
            baseRoot,
            merged,
            directories,
            stopwatch.Elapsed,
            DateTime.UtcNow,
            collisions: collisions);
    }

    private static string MergeRelative(string prefix, string relativePath)
    {
        if (prefix.Length == 0)
        {
            return relativePath;
        }

        return relativePath.Length == 0 ? prefix : prefix + "/" + relativePath;
    }
}
