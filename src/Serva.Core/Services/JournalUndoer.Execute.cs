using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary><see cref="JournalUndoer"/> 的执行部分（partial）。</summary>
public sealed partial class JournalUndoer
{
    /// <summary>执行撤销计划；<paramref name="journal"/> 非空时把撤销动作也记入新日志。</summary>
    public static UndoResult Execute(UndoPlan plan, Journal? journal = null)
    {
        int deleted = 0, restored = 0, renamed = 0, skipped = 0, failed = 0;
        var messages = new List<string>();

        foreach (UndoStep step in plan.Steps)
        {
            if (!step.IsExecutable)
            {
                skipped++;
                if (step.Note is not null)
                {
                    messages.Add($"{step.RelativePath}：{step.Note}");
                }

                continue;
            }

            try
            {
                switch (step.Action)
                {
                    case UndoActionKind.DeleteCreatedFile:
                        if (!File.Exists(step.TargetPath))
                        {
                            DeleteEmptyCreatedDirectories(step);
                            skipped++;
                            break;
                        }

                        // 安全校验：大小与日志记录不一致说明该文件已被再次改动，不删。
                        long actual = new FileInfo(step.TargetPath).Length;
                        if (step.Bytes > 0 && actual != step.Bytes)
                        {
                            skipped++;
                            messages.Add(CoreText.Format(
                                "{0}: file changed again ({1} bytes ≠ logged {2} bytes), skipped",
                                "{0}：文件已被再次改动（{1} 字节 ≠ 日志 {2} 字节），已跳过",
                                step.RelativePath,
                                actual,
                                step.Bytes));
                            break;
                        }

                        File.Delete(step.TargetPath);
                        journal?.Append(
                            JournalOperationKind.Undo, step.RelativePath, step.TargetPath, string.Empty, step.Bytes, true,
                            CoreText.Get("Undo: delete files copied in this run", "撤销：删除本次复制的文件"));
                        DeleteEmptyCreatedDirectories(step);
                        deleted++;
                        break;

                    case UndoActionKind.RestoreFromTrash:
                        if (!File.Exists(step.TargetPath))
                        {
                            skipped++;
                            messages.Add(CoreText.Format(
                                "{0}: the file in the recycle folder no longer exists, skipped",
                                "{0}：回收目录中的文件已不存在，已跳过",
                                step.RelativePath));
                            break;
                        }

                        EnsureParent(step.SourcePath);
                        if (File.Exists(step.SourcePath))
                        {
                            File.Delete(step.SourcePath);
                        }

                        File.Move(step.TargetPath, step.SourcePath);
                        journal?.Append(
                            JournalOperationKind.Undo, step.RelativePath, step.TargetPath, step.SourcePath, step.Bytes, true,
                            CoreText.Get("Undo: move back from the recycle folder", "撤销：从回收目录移回"));
                        restored++;
                        break;

                    case UndoActionKind.RenameBack:
                        if (!File.Exists(step.TargetPath))
                        {
                            skipped++;
                            messages.Add(CoreText.Format(
                                "{0}: the file to rename no longer exists, skipped",
                                "{0}：待改回的文件已不存在，已跳过",
                                step.RelativePath));
                            break;
                        }

                        EnsureParent(step.SourcePath);
                        if (File.Exists(step.SourcePath))
                        {
                            File.Delete(step.SourcePath);
                        }

                        File.Move(step.TargetPath, step.SourcePath);
                        journal?.Append(
                            JournalOperationKind.Undo, step.RelativePath, step.TargetPath, step.SourcePath, step.Bytes, true,
                            CoreText.Get("Undo: rename back", "撤销：改回原名"));
                        renamed++;
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                failed++;
                messages.Add($"{step.RelativePath}：{ex.Message}");
                journal?.Append(
                    JournalOperationKind.Error, step.RelativePath, step.SourcePath, step.TargetPath, step.Bytes, false, ex.Message);
            }
        }

        return new UndoResult
        {
            Deleted = deleted,
            Restored = restored,
            Renamed = renamed,
            Skipped = skipped,
            Failed = failed,
            Messages = messages,
        };
    }

    /// <summary>
    /// 删除本次运行<strong>新建</strong>、且现在是空目录的文件夹（从最内层开始）。
    /// <para>文件删掉之后，这些目录本来就不该由这次运行留在目标端。</para>
    /// </summary>
    private static void DeleteEmptyCreatedDirectories(UndoStep step)
    {
        IReadOnlyList<string>? directories = step.CreatedDirectories;
        if (directories is null || directories.Count == 0)
        {
            return;
        }

        for (int index = directories.Count - 1; index >= 0; index--)
        {
            string directory = directories[index];

            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
                // 目录非空 / 正被占用：保留。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
    private static void EnsureParent(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
