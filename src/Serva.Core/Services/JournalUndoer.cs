using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 把一次运行的日志转换为「撤销计划」并按相反顺序执行。
/// <para>
/// 安全策略：只删除<strong>本次确实新建</strong>的文件（目标端原本不存在同名文件）。
/// 已被覆盖的内容、被直接删除的文件都<strong>不</strong>自动还原，只标记为「无法撤销」，
/// 避免撤销过程造成二次数据损坏。
/// </para>
/// </summary>
public sealed partial class JournalUndoer
{
    /// <summary>构建撤销计划（按原始操作的相反顺序，保证「先改名再写入」这类组合能正确回退）。</summary>
    public static UndoPlan BuildPlan(IReadOnlyList<JournalEntry> entries)
    {
        var steps = new List<UndoStep>(entries.Count);

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            JournalEntry entry = entries[i];
            if (!entry.Success)
            {
                continue;   // 失败的操作没有产生变更
            }

            switch (entry.Kind)
            {
                case JournalOperationKind.Copy:
                    steps.Add(BuildCopyUndo(entry));
                    break;

                case JournalOperationKind.MoveToTrash:
                    // 回收目录里的 TargetPath → 原位 SourcePath
                    steps.Add(new UndoStep(
                        UndoActionKind.RestoreFromTrash,
                        entry.RelativePath,
                        entry.SourcePath,
                        entry.TargetPath,
                        entry.Bytes,
                        null));
                    break;

                case JournalOperationKind.Rename:
                    // 现名字 TargetPath → 原名字 SourcePath
                    steps.Add(new UndoStep(
                        UndoActionKind.RenameBack,
                        entry.RelativePath,
                        entry.SourcePath,
                        entry.TargetPath,
                        entry.Bytes,
                        null));
                    break;

                case JournalOperationKind.Delete:
                    steps.Add(new UndoStep(
                        UndoActionKind.NotPossible,
                        entry.RelativePath,
                        entry.SourcePath,
                        entry.TargetPath,
                        entry.Bytes,
                        string.IsNullOrWhiteSpace(entry.Message)
                            ? CoreText.Get("The file was permanently deleted and cannot be restored", "文件已被直接删除，无法还原")
                            : CoreText.Format("{0}; cannot be restored automatically", "文件{0}，无法自动还原", entry.Message)));
                    break;

                case JournalOperationKind.Backup:
                    // 副本 SourcePath（.ServaUndoTemp 里的原件）→ 原位 TargetPath
                    steps.Add(new UndoStep(
                        UndoActionKind.RestoreFromBackup,
                        entry.RelativePath,
                        entry.SourcePath,
                        entry.TargetPath,
                        entry.Bytes,
                        null));
                    break;
            }
        }

        return new UndoPlan(steps);
    }

    private static UndoStep BuildCopyUndo(JournalEntry entry)
    {
        if (entry.Overwritten is null)
        {
            return new UndoStep(
                UndoActionKind.NotPossible,
                entry.RelativePath,
                entry.SourcePath,
                entry.TargetPath,
                entry.Bytes,
                CoreText.Get(
                    "Cannot tell whether the target already had a file with this name; not deleting automatically",
                    "无法确认目标端原本是否已有同名文件，为安全起见不自动删除"));
        }

        if (entry.Overwritten == true)
        {
            // 覆盖前保留了原件副本：先删掉本次写入的文件是安全的，原件随后由备份步骤还原回去。
            if (entry.BackupPath is null)
            {
                return new UndoStep(
                    UndoActionKind.NotPossible,
                    entry.RelativePath,
                    entry.SourcePath,
                    entry.TargetPath,
                    entry.Bytes,
                    CoreText.Get("The original file on the target was overwritten and cannot be restored", "目标端原有文件已被覆盖，无法还原"));
            }

            return new UndoStep(
                UndoActionKind.DeleteCreatedFile,
                entry.RelativePath,
                entry.SourcePath,
                entry.TargetPath,
                entry.Bytes,
                null,
                entry.CreatedDirectories);
        }

        return new UndoStep(
            UndoActionKind.DeleteCreatedFile,
            entry.RelativePath,
            entry.SourcePath,
            entry.TargetPath,
            entry.Bytes,
            null,
            entry.CreatedDirectories);
    }
}
