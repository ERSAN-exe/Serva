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
                        deleted++;
                        break;

                    case UndoActionKind.RestoreFromBackup:
                        if (!File.Exists(step.SourcePath))
                        {
                            skipped++;
                            messages.Add(CoreText.Format(
                                "{0}: the temporary copy of the overwritten file no longer exists, skipped",
                                "{0}：覆盖前的临时副本已不存在，已跳过",
                                step.RelativePath));
                            OverwriteBackup.TryDeleteFolder(Path.GetDirectoryName(step.SourcePath));
                            break;
                        }

                        if (File.Exists(step.TargetPath))
                        {
                            // 目标端已被再次改动：不覆盖现场，跳过还原。
                            long backupSize = new FileInfo(step.SourcePath).Length;
                            long actualSize = new FileInfo(step.TargetPath).Length;
                            if (backupSize != actualSize)
                            {
                                skipped++;
                                messages.Add(CoreText.Format(
                                    "{0}: the target file changed again, the original was not restored",
                                    "{0}：目标端文件已被再次改动，未还原原文件",
                                    step.RelativePath));
                                break;
                            }
                        }

                        // 移回原位（同时删掉已经空的 .ServaUndoTemp 目录）。
                        OverwriteBackup.Restore(step.SourcePath, step.TargetPath);
                        journal?.Append(
                            JournalOperationKind.Undo, step.RelativePath, step.SourcePath, step.TargetPath, step.Bytes, true,
                            CoreText.Get("Undo: restore the overwritten original", "撤销：还原被覆盖的原文件"));
                        restored++;
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

        // 最后统一清理「本次运行新建、现在已空」的目录（含被跳过步骤的目录）。
        DeleteEmptyCreatedDirectories(plan);

        // 还原过的原文件已经离开临时目录：顺手把空的 .ServaUndoTemp 一起删掉。
        DeleteEmptyBackupFolders(plan);

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
    /// 删除本次运行<strong>新建</strong>、且现在是空目录的文件夹（由深到浅）。
    /// <para>
    /// 收集整个撤销计划里记录的目录（包括被跳过的步骤）之后再删：
    /// 这样即使某个文件因为「已被再次改动」被跳过，只要它已经不在了，空目录也能一并清掉。
    /// </para>
    /// </summary>
    private static void DeleteEmptyCreatedDirectories(UndoPlan plan)
    {
        var directories = new List<string>();

        foreach (UndoStep step in plan.Steps)
        {
            if (step.CreatedDirectories is { } created)
            {
                directories.AddRange(created);
            }
        }

        if (directories.Count == 0)
        {
            return;
        }

        // 长的（更深的）先删：内层删掉后外层才会变空。
        directories.Sort(static (left, right) => right.Length.CompareTo(left.Length));

        foreach (string directory in directories)
        {
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

    /// <summary>撤销后删掉已经变空的 <c>.ServaUndoTemp</c> 目录（还有副本的目录保留）。</summary>
    private static void DeleteEmptyBackupFolders(UndoPlan plan)
    {
        foreach (UndoStep step in plan.Steps)
        {
            if (step.Action == UndoActionKind.RestoreFromBackup)
            {
                OverwriteBackup.TryDeleteFolder(Path.GetDirectoryName(step.SourcePath));
            }
        }
    }
}
