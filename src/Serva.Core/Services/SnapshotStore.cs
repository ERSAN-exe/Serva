using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 增量快照：按「卷序列号」缓存每个目录的最后写入时间与文件清单。
/// <para>复扫时若所有目录时间戳与快照完全一致，则直接复用上次清单（跳过读盘 → 秒级复扫）。
/// 只要有任何目录时间戳变化就回退到完整扫描，因此不会漏掉新增文件。</para>
/// <para>文件位置：<c>%LocalAppData%\Serva\state\&lt;卷序列号&gt;.json</c></para>
/// </summary>
public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public SnapshotStore(string? stateDirectory = null)
    {
        StateDirectory = stateDirectory ?? DefaultStateDirectory;
    }

    public string StateDirectory { get; }
    /// <summary></summary>
    public static string DefaultStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serva",
        "state");

    public string GetSnapshotPath(string volumeSerial) =>
        Path.Combine(StateDirectory, Sanitize(volumeSerial) + ".json");

    /// <summary>目录时间戳与快照一致时返回缓存的扫描结果，否则返回 <c>null</c>。</summary>
    public ScanResult? TryReload(
        string root,
        string volumeSerial,
        IReadOnlyDictionary<string, long> directoryTimes,
        Stopwatch stopwatch)
    {
        string path = GetSnapshotPath(volumeSerial);
        if (!File.Exists(path))
        {
            return null;
        }

        SnapshotDocument? document;
        try
        {
            using FileStream stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<SnapshotDocument>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (document is null
            || !string.Equals(document.Root, root, StringComparison.OrdinalIgnoreCase)
            || document.Directories.Count != directoryTimes.Count)
        {
            return null;
        }

        foreach ((string directory, long ticks) in directoryTimes)
        {
            if (!document.Directories.TryGetValue(directory, out long cached) || cached != ticks)
            {
                return null;
            }
        }

        var files = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotFile file in document.Files)
        {
            string full = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            files[file.Path] = new FileEntry(
                full,
                file.Path,
                file.Length,
                new DateTime(file.Ticks, DateTimeKind.Utc),
                (FileAttributes)file.Attributes);
        }

        stopwatch.Stop();
        return new ScanResult(
            root,
            files,
            [.. document.Directories.Keys.Where(static d => d.Length > 0)],
            stopwatch.Elapsed,
            DateTime.UtcNow,
            isFromSnapshot: true);
    }

    /// <summary>写入 / 覆盖快照。</summary>
    public void Save(ScanResult scan, string volumeSerial, IReadOnlyDictionary<string, long> directoryTimes)
    {
        var document = new SnapshotDocument
        {
            Root = scan.Root,
            VolumeSerial = volumeSerial,
            CapturedUtc = scan.CapturedUtc.ToString("O"),
            Directories = new Dictionary<string, long>(directoryTimes, StringComparer.OrdinalIgnoreCase),
            Files = [.. scan.Files.Values.Select(static f => new SnapshotFile
            {
                Path = f.RelativePath,
                Length = f.Length,
                Ticks = f.LastWriteTimeUtc.Ticks,
                Attributes = (int)f.Attributes,
            })],
        };

        try
        {
            Directory.CreateDirectory(StateDirectory);
            using FileStream stream = File.Create(GetSnapshotPath(volumeSerial));
            JsonSerializer.Serialize(stream, document, JsonOptions);
        }
        catch (IOException)
        {
            // 快照只是加速缓存，写失败不影响正确性。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>删除某个卷的快照（例如恢复默认设置 / 强制完整扫描）。</summary>
    public void Invalidate(string volumeSerial)
    {
        try
        {
            string path = GetSnapshotPath(volumeSerial);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Sanitize(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                buffer.Append(c);
            }
        }

        return buffer.Length == 0 ? "unknown" : buffer.ToString();
    }

    private sealed class SnapshotDocument
    {
        public string Root { get; set; } = string.Empty;

        public string VolumeSerial { get; set; } = string.Empty;

        public string CapturedUtc { get; set; } = string.Empty;

        public Dictionary<string, long> Directories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<SnapshotFile> Files { get; set; } = [];
    }

    private sealed class SnapshotFile
    {
        public string Path { get; set; } = string.Empty;

        public long Length { get; set; }

        public long Ticks { get; set; }

        public int Attributes { get; set; }
    }
}
