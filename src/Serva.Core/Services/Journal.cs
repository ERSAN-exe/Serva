using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serva.Core.Localization;

namespace Serva.Core.Services;

/// <summary>日志中的操作类型（列表里的「操作」列）。</summary>
public enum JournalOperationKind
{
    Copy,
    Delete,
    MoveToTrash,
    Rename,
    Skip,
    Error,

    /// <summary>由撤销流程写入的记录。</summary>
    Undo,

    /// <summary>
    /// 一次运行的起始记录：记录本次运行的方向与处理方式
    /// （如「由 C: 移动到 E:（合并）」）。追加在末尾，避免改动旧日志里的枚举数值。
    /// </summary>
    Move,

    /// <summary>
    /// 覆盖前把原文件挪进同目录的隐藏目录 <c>.ServaUndoTemp</c>（撤销时据此还原）。
    /// 追加在末尾，避免改动旧日志里的枚举数值。
    /// </summary>
    Backup,
}

/// <summary>
/// 运行抬头（<see cref="JournalOperationKind.Move"/>）在 <c>Message</c> 里保存的<strong>处理方式代码</strong>：
/// 存代码而不是翻译好的文字，切换语言后日志里的说明还能跟着变。
/// </summary>
public static class RunHeaderCode
{
    public const string Merge = "merge";

    public const string OverwriteLocalToDevice = "overwrite-local-to-device";

    public const string OverwriteDeviceToLocal = "overwrite-device-to-local";

    /// <summary>
    /// 把新旧两种写法都归一到代码：新日志存的就是代码；旧日志里存的是当时翻译好的文字
    /// （中文或英文），认出来之后这些旧日志也能跟着语言切换。
    /// </summary>
    public static string? Normalize(string? message) => message switch
    {
        Merge or "Merge" or "合并" => Merge,
        OverwriteLocalToDevice or "Overwrite" or "覆盖" => OverwriteLocalToDevice,
        OverwriteDeviceToLocal or "Overwrite local disk" or "覆盖本机硬盘" => OverwriteDeviceToLocal,
        _ => null,
    };
}
/// <summary>一条操作日志。</summary>
public sealed record JournalEntry(
    DateTime TimestampUtc,
    JournalOperationKind Kind,
    string RelativePath,
    string SourcePath,
    string TargetPath,
    long Bytes,
    bool Success,
    string? Message)
{
    /// <summary>
    /// 复制目标端在写入前是否已存在同名文件。
    /// <para><c>null</c> 表示未知（旧日志）—— 撤销时按「不可安全撤销」处理，避免误删用户数据。</para>
    /// </summary>
    public bool? Overwritten { get; init; }

    /// <summary>
    /// 运行抬头（<see cref="JournalOperationKind.Move"/>）记录的外置设备卷标。
    /// 记在日志里，设备拔掉后「上次同步」信息仍能显示设备名称。
    /// </summary>
    public string? DeviceLabel { get; init; }

    /// <summary>
    /// 这次复制为此文件<strong>新建</strong>的目录（绝对路径，从最外层到最内层）。
    /// 撤销时用来删掉「本次运行才出现」的空目录；旧日志没有这一项（<c>null</c>）。
    /// </summary>
    public IReadOnlyList<string>? CreatedDirectories { get; init; }

    /// <summary>
    /// 覆盖前原文件被挪到的临时副本路径（<c>.ServaUndoTemp</c>）；<c>null</c> 表示没有保留副本。
    /// 有副本时，撤销可以安全地删除本次写入的文件，再由备份步骤把原件还原回去。
    /// </summary>
    public string? BackupPath { get; init; }

    public string KindText => Kind switch
    {
        JournalOperationKind.Copy => CoreText.Get("Copy", "复制"),
        JournalOperationKind.Delete => CoreText.Get("Delete", "删除"),
        JournalOperationKind.MoveToTrash => CoreText.Get("Recycle", "移入回收"),
        JournalOperationKind.Rename => CoreText.Get("Rename", "重命名"),
        JournalOperationKind.Skip => CoreText.Get("Skip", "跳过"),
        JournalOperationKind.Undo => CoreText.Get("Undo", "撤销"),
        JournalOperationKind.Move => CoreText.Get("Move", "移动"),
        JournalOperationKind.Backup => CoreText.Get("Backup", "备份"),
        _ => CoreText.Get("Error", "错误"),
    };

    /// <summary>日志列表里的结果列文案。</summary>
    public string ResultText => Success
        ? CoreText.Get("OK", "成功")
        : CoreText.Get("Failed", "失败");

    /// <summary>日志列表里的时间列文案：只显示时间。</summary>
    public string TimeText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

    public string DetailText => Kind == JournalOperationKind.Move
        ? RunHeaderText()
        : string.IsNullOrWhiteSpace(Message)
            ? RelativePath
            : $"{RelativePath} — {Message}";

    /// <summary>
    /// 运行抬头的说明：由「处理方式代码 + 两端盘符」按<strong>当前语言</strong>现算；
    /// 旧日志里存的是当时翻译好的句子（<c>Message</c> 不是代码），直接显示原文。
    /// </summary>
    private string RunHeaderText()
    {
        string source = DriveOf(SourcePath);
        string target = DriveOf(TargetPath);

        return RunHeaderCode.Normalize(Message) switch
        {
            RunHeaderCode.Merge => CoreText.Format(
                "Moved from {0} to {1} (merge)", "由 {0} 移动到 {1}（合并）", source, target),
            RunHeaderCode.OverwriteLocalToDevice => CoreText.Format(
                "Overwrote {1} with the contents of {0} (source {0})",
                "由 {0} 覆盖到 {1}（覆盖 {0} 内容）",
                source,
                target),
            RunHeaderCode.OverwriteDeviceToLocal => CoreText.Format(
                "Overwrote {0} with the contents of {1} (source {1})",
                "由 {1} 覆盖到 {0}（覆盖 {1} 内容）",
                source,
                target),
            _ => RelativePath,
        };
    }

    /// <summary>取路径的盘符（<c>G:\\x</c> → <c>G:</c>）；取不到时返回原路径。</summary>
    private static string DriveOf(string path)
    {
        string? root = Path.GetPathRoot(path);

        return string.IsNullOrEmpty(root)
            ? path
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

/// <summary>一个日志文件的概要（对应样稿日志页顶部横幅）。</summary>
public sealed record JournalSummary(
    string FilePath,
    string FileName,
    DateTime Timestamp,
    int OperationCount,
    int FailureCount,
    int UndoableCount)
{
    public string Caption =>
        CoreText.Format("{0} · {1} operation(s)", "{0} · {1} 次操作", FileName, OperationCount)
        + (FailureCount > 0
            ? CoreText.Format(" · {0} failed", " · {0} 项失败", FailureCount)
            : string.Empty);
}

/// <summary>
/// 操作日志：每次执行写入一个 JSONL 文件（每行一条操作），可用于「撤销本次运行」。
/// <para>文件位置：<c>%LocalAppData%\Serva\journal\&lt;时间戳&gt;.jsonl</c></para>
/// </summary>
public sealed class Journal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();

    public Journal(string? journalDirectory = null, DateTime? timestamp = null)
    {
        DirectoryPath = journalDirectory ?? DefaultJournalDirectory;
        DateTime stamp = timestamp ?? DateTime.Now;
        FileName = stamp.ToString("yyyy-MM-ddTHH-mm-ss") + ".jsonl";
        FilePath = Path.Combine(DirectoryPath, FileName);

        try
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string DefaultJournalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serva",
        "journal");

    public string DirectoryPath { get; }

    public string FileName { get; }

    public string FilePath { get; }

    /// <summary>已记录的操作条数。</summary>
    public int OperationCount { get; private set; }

    /// <summary>其中失败的条数。</summary>
    public int FailureCount { get; private set; }

    /// <summary>追加一条操作记录（线程安全）。</summary>
    public void Append(JournalEntry entry)
    {
        lock (_gate)
        {
            try
            {
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, Encoding.UTF8);
                OperationCount++;
                if (!entry.Success)
                {
                    FailureCount++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Append(
        JournalOperationKind kind,
        string relativePath,
        string sourcePath,
        string targetPath,
        long bytes,
        bool success,
        string? message = null,
        bool? overwritten = null,
        string? deviceLabel = null,
        IReadOnlyList<string>? createdDirectories = null,
        string? backupPath = null) =>
        Append(new JournalEntry(DateTime.UtcNow, kind, relativePath, sourcePath, targetPath, bytes, success, message)
        {
            Overwritten = overwritten,
            DeviceLabel = deviceLabel,
            CreatedDirectories = createdDirectories,
            BackupPath = backupPath,
        });

    /// <summary>读取日志文件概要（次数 / 失败数 / 可撤销数）。</summary>
    public static JournalSummary Summarize(string path)
    {
        IReadOnlyList<JournalEntry> entries = Read(path);
        int undoable = JournalUndoer.BuildPlan(entries).ExecutableCount;

        string name = Path.GetFileName(path);
        DateTime stamp = DateTime.MinValue;
        if (DateTime.TryParseExact(
                Path.GetFileNameWithoutExtension(name),
                "yyyy-MM-ddTHH-mm-ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            stamp = parsed;
        }
        else
        {
            try
            {
                stamp = File.GetLastWriteTime(path);
            }
            catch (IOException)
            {
            }
        }

        return new JournalSummary(
            path,
            name,
            stamp,
            entries.Count,
            entries.Count(static e => !e.Success),
            undoable);
    }

    /// <summary>列出全部日志概要（新的在前）。</summary>
    public static IReadOnlyList<JournalSummary> ListSummaries(string? directory = null) =>
        [.. ListJournals(directory).Select(Summarize).OrderByDescending(static s => s.Timestamp)];

    /// <summary>列出全部日志文件（新的在前）。</summary>
    public static IReadOnlyList<string> ListJournals(string? directory = null)
    {
        string dir = directory ?? DefaultJournalDirectory;
        try
        {
            return [.. System.IO.Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(static f => f)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 只保留最新的一份日志文件，其余旧日志直接删除（日志页只显示最新一次运行）。
    /// 文件被占用 / 无权限时忽略。
    /// </summary>
    /// <returns>删除的文件数。</returns>
    public static int DeleteOldJournals(string? directory = null)
    {
        IReadOnlyList<string> files = ListJournals(directory);   // 文件名含时间戳，新的在前
        int deleted = 0;

        for (int i = 1; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    /// <summary>
    /// 删除全部日志文件（比较页「日志」卡片里的「清除日志」）。
    /// 清除后没有任何日志可用于「撤销本次运行」。文件被占用 / 无权限时忽略。
    /// </summary>
    /// <returns>删除的文件数。</returns>
    public static int DeleteAllJournals(string? directory = null)
    {
        int deleted = 0;

        foreach (string file in ListJournals(directory))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    /// <summary>读取一个日志文件中的全部条目。</summary>
    public static IReadOnlyList<JournalEntry> Read(string path)
    {
        var entries = new List<JournalEntry>();

        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return entries;
        }
        catch (UnauthorizedAccessException)
        {
            return entries;
        }

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                JournalEntry? entry = JsonSerializer.Deserialize<JournalEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // 崩溃导致的半截行：忽略。
            }
        }

        return entries;
    }
}
