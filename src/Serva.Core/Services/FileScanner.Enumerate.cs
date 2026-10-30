using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// <see cref="FileScanner"/> 的目录遍历与辅助部分（partial）。
/// </summary>
public sealed partial class FileScanner
{
    /// <summary>
    /// 扫描多个来源并合并为一个比较基准。
    /// <para>文件夹来源以其自身为根（相对路径不含文件夹名）；单个文件来源以文件名为键。</para>
    /// </summary>
    public ScanResult ScanMany(
        IReadOnlyList<string> roots,
        string displayRoot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var merged = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();
        var collisions = new List<string>();

        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScanResult partial = Scan(root, null, progress, cancellationToken);
            directories.AddRange(partial.Directories);

            foreach ((string key, FileEntry entry) in partial.Files)
            {
                if (!merged.TryAdd(key, entry))
                {
                    collisions.Add(key);
                }
            }
        }

        stopwatch.Stop();
        return new ScanResult(
            displayRoot,
            merged,
            directories,
            stopwatch.Elapsed,
            DateTime.UtcNow,
            collisions: collisions);
    }

    /// <summary>只枚举目录（不读文件内容），返回相对于 <paramref name="root"/> 的路径。</summary>
    private List<(string Rel, string Full)> EnumerateDirectories(string root, CancellationToken cancellationToken)
    {
        var result = new List<(string Rel, string Full)> { (string.Empty, root) };
        var stack = new Stack<(string Rel, string Full)>();
        stack.Push((string.Empty, root));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string rel, string full) = stack.Pop();

            foreach (string child in SafeEnumerateDirectories(full))
            {
                string name = Path.GetFileName(child);
                string childRel = rel.Length == 0 ? name : rel + "/" + name;

                // 命中排除规则的目录整棵子树跳过；重解析点（junction / symlink）跳过以免成环。
                if (_matcher.IsExcluded(childRel) || IsReparsePoint(child))
                {
                    continue;
                }

                if (!_includeSubdirectories)
                {
                    continue;
                }

                result.Add((childRel, child));
                stack.Push((childRel, child));
            }
        }

        return result;
    }

    private FileEntry? CreateEntry(FileInfo info, string relativePath, string fullPath)
    {
        try
        {
            if (_skipHiddenAndSystem && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                return null;
            }

            return new FileEntry(fullPath, relativePath, info.Length, info.LastWriteTimeUtc, info.Attributes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
