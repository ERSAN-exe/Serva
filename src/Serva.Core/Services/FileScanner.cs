using System.Diagnostics;
using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>扫描进度回调。</summary>
public sealed record ScanProgress(int FilesFound, long BytesFound, string CurrentDirectory);

/// <summary>
/// 文件系统扫描器。
/// <para>
/// 每次都读取真实的文件元数据（大小 / 修改时间 / 属性）。
/// </para>
/// <para>
/// 不能用「目录时间戳 + 增量快照」跳过读盘：修改文件<strong>内容</strong>只会更新文件的修改时间，
/// <strong>不会</strong>改变所在目录的时间戳 —— 命中缓存就会拿到旧的修改时间，
/// 表现为「设备端文件更新，却显示本机较新」（比较新旧完全依赖这个时间）。
/// </para>
/// </summary>
public sealed partial class FileScanner
{
    private readonly ExcludeMatcher _matcher;
    private readonly bool _skipHiddenAndSystem;
    private readonly bool _includeSubdirectories;

    public FileScanner(
        ExcludeMatcher matcher,
        bool skipHiddenAndSystem = false,
        bool includeSubdirectories = true)
    {
        _matcher = matcher;
        _skipHiddenAndSystem = skipHiddenAndSystem;
        _includeSubdirectories = includeSubdirectories;
    }

    /// <summary>扫描单个来源（可以是文件或文件夹）。</summary>
    public ScanResult Scan(
        string root,
        string? volumeSerial = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (File.Exists(root))
        {
            var info = new FileInfo(root);
            var single = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            FileEntry? entry = CreateEntry(info, info.Name, info.FullName);
            if (entry is not null)
            {
                single[entry.RelativePath] = entry;
            }

            stopwatch.Stop();
            return new ScanResult(root, single, [], stopwatch.Elapsed, DateTime.UtcNow);
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(CoreText.Format("Source path not found: {0}", "找不到来源路径：{0}", root));
        }

        List<(string Rel, string Full)> directories = EnumerateDirectories(root, cancellationToken);

        var found = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;

        foreach ((string rel, string full) in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string file in SafeEnumerateFiles(full))
            {
                string name = Path.GetFileName(file);
                string relativePath = rel.Length == 0 ? name : rel + "/" + name;

                if (_matcher.IsExcluded(relativePath))
                {
                    continue;
                }

                FileEntry? entry = CreateEntry(new FileInfo(file), relativePath, file);
                if (entry is null)
                {
                    continue;
                }

                found[entry.RelativePath] = entry;
                bytes += entry.Length;
            }

            progress?.Report(new ScanProgress(found.Count, bytes, full));
        }

        stopwatch.Stop();

        return new ScanResult(
            root,
            found,
            [.. directories.Select(static d => d.Rel)],
            stopwatch.Elapsed,
            DateTime.UtcNow);
    }
}
