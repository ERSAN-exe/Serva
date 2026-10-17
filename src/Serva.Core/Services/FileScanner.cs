using System.Diagnostics;
using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>扫描进度回调。</summary>
public sealed record ScanProgress(int FilesFound, long BytesFound, string CurrentDirectory);

/// <summary>
/// 文件系统扫描器。
/// <para>目录遍历与文件枚举分两阶段：先用「增量快照」比对目录时间戳，命中即跳过读盘（秒级复扫）；
/// 未命中才完整枚举文件。</para>
/// </summary>
public sealed partial class FileScanner
{
    private readonly ExcludeMatcher _matcher;
    private readonly bool _skipHiddenAndSystem;
    private readonly bool _includeSubdirectories;
    private readonly SnapshotStore? _snapshotStore;

    public FileScanner(
        ExcludeMatcher matcher,
        bool skipHiddenAndSystem = false,
        bool includeSubdirectories = true,
        SnapshotStore? snapshotStore = null)
    {
        _matcher = matcher;
        _skipHiddenAndSystem = skipHiddenAndSystem;
        _includeSubdirectories = includeSubdirectories;
        _snapshotStore = snapshotStore;
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
        Dictionary<string, long> directoryTimes = CaptureDirectoryTimes(root, directories);

        if (_snapshotStore is not null && !string.IsNullOrEmpty(volumeSerial))
        {
            ScanResult? cached = _snapshotStore.TryReload(root, volumeSerial, directoryTimes, stopwatch);
            if (cached is not null)
            {
                return cached;
            }
        }

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

        ScanResult result = new(
            root,
            found,
            [.. directories.Select(static d => d.Rel)],
            stopwatch.Elapsed,
            DateTime.UtcNow);

        if (_snapshotStore is not null && !string.IsNullOrEmpty(volumeSerial))
        {
            _snapshotStore.Save(result, volumeSerial, directoryTimes);
        }

        return result;
    }
}
