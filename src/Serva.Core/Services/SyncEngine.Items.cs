using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary><see cref="SyncEngine"/> 的逐项执行（partial）。</summary>
public sealed partial class SyncEngine
{
    private static async Task ProcessItemAsync(
        RunState state,
        PlanItem item,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        try
        {
            Volatile.Write(ref state.CurrentFile, item.RelativePath);
            await ExecuteItemAsync(state, item, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref state.Completed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Interlocked.Increment(ref state.Failed);
            state.Failures.Add(new SyncFailure(item.RelativePath, ex.Message));
            state.Journal.Append(
                JournalOperationKind.Error, item.RelativePath, string.Empty, string.Empty, item.Size, false, ex.Message);
        }
        finally
        {
            gate.Release();
            Report(state, force: false);
        }
    }

    private static async Task ExecuteItemAsync(RunState state, PlanItem item, CancellationToken cancellationToken)
    {
        switch (item.Action)
        {
            case PlanAction.CopyToDevice:
            {
                string source = Combine(state.LeftRoot, item.RelativePath);
                string target = Combine(state.RightRoot, item.TargetFileName ?? item.RelativePath);

                PreserveExistingTarget(state, item, target, state.RightRoot);

                // 写入前目标端是否已有同名文件 —— 撤销时据此决定能否安全删除。
                bool overwritten = File.Exists(target);

                // 覆盖前把原件挪进同目录的隐藏目录 .ServaUndoTemp，撤销时据此还原（执行策略可关闭）。
                string? backupPath = BackupExistingTarget(state, item, target, overwritten);

                // 复制前先记下目标端缺少的目录：撤销时据此删掉「本次运行才出现」的空目录。
                IReadOnlyList<string> createdDirectories = CollectMissingDirectories(target);

                await CopyFileAsync(
                        source,
                        target,
                        state.Options,
                        bytes => Interlocked.Add(ref state.CopiedBytes, bytes),
                        cancellationToken)
                    .ConfigureAwait(false);

                state.Journal.Append(
                    JournalOperationKind.Copy, item.RelativePath, source, target, item.Size, true, null, overwritten,
                    createdDirectories: createdDirectories, backupPath: backupPath);
                break;
            }

            case PlanAction.CopyToLocal:
            {
                string source = Combine(state.RightRoot, item.RelativePath);
                string target = Combine(state.LeftRoot, item.TargetFileName ?? item.RelativePath);

                PreserveExistingTarget(state, item, target, state.LeftRoot);
                bool overwritten = File.Exists(target);
                string? backupPath = BackupExistingTarget(state, item, target, overwritten);

                // 复制前先记下目标端缺少的目录：撤销时据此删掉「本次运行才出现」的空目录。
                IReadOnlyList<string> createdDirectories = CollectMissingDirectories(target);

                await CopyFileAsync(
                        source,
                        target,
                        state.Options,
                        bytes => Interlocked.Add(ref state.CopiedBytes, bytes),
                        cancellationToken)
                    .ConfigureAwait(false);

                state.Journal.Append(
                    JournalOperationKind.Copy, item.RelativePath, source, target, item.Size, true, null, overwritten,
                    createdDirectories: createdDirectories, backupPath: backupPath);
                break;
            }

            case PlanAction.DeleteOnDevice:
                DeleteItem(
                    Combine(state.RightRoot, item.RelativePath),
                    state.Options,
                    item.RelativePath,
                    state.Journal);
                Interlocked.Increment(ref state.Skipped);
                break;

            case PlanAction.DeleteOnLocal:
                DeleteItem(
                    Combine(state.LeftRoot, item.RelativePath),
                    state.Options,
                    item.RelativePath,
                    state.Journal);
                Interlocked.Increment(ref state.Skipped);
                break;

            default:
                Interlocked.Increment(ref state.Skipped);
                break;
        }
    }

    /// <summary>
    /// 列出 <paramref name="target"/> 所在目录链里<strong>当前还不存在</strong>的目录（从最外层到最内层）：
    /// 复制时这些目录会被创建出来，撤销时连同空目录一起删掉。
    /// </summary>
    private static IReadOnlyList<string> CollectMissingDirectories(string target)
    {
        string? directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return [];
        }

        var missing = new List<string>();

        for (string? current = directory;
             !string.IsNullOrEmpty(current) && !Directory.Exists(current);
             current = Path.GetDirectoryName(current))
        {
            missing.Add(current);
        }

        missing.Reverse();   // 最外层在前，撤销时反着删。
        return missing;
    }
    /// <summary>「两者都保留」：写入前先把目标端已有文件改名，两个版本都不丢。</summary>
    private static void PreserveExistingTarget(RunState state, PlanItem item, string target, string targetRoot)
    {
        if (item.PreserveTargetAs is null || !File.Exists(target))
        {
            return;
        }

        string preserved = Combine(targetRoot, item.PreserveTargetAs);
        string? directory = Path.GetDirectoryName(preserved);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(preserved))
        {
            File.Delete(preserved);
        }

        File.Move(target, preserved);
        state.Journal.Append(
            JournalOperationKind.Rename, item.RelativePath, target, preserved, 0, true, "冲突：两者都保留");
    }

    /// <summary>
    /// 覆盖前把目标端已有的文件挪进同目录的隐藏目录 <c>.ServaUndoTemp</c>，返回副本路径（没有备份时为 <c>null</c>）。
    /// <para>
    /// 执行策略关掉「保留临时副本」时不备份。备份本身失败（被占用 / 无权限 / 空间不足）也不影响本次复制 ——
    /// 只是这一个文件覆盖后无法撤销；让复制失败反而更容易让用户以为文件没同步。
    /// </para>
    /// </summary>
    private static string? BackupExistingTarget(RunState state, PlanItem item, string target, bool targetExists)
    {
        if (!targetExists || !state.Options.KeepOverwriteBackup)
        {
            return null;
        }

        try
        {
            string backupPath = OverwriteBackup.Move(target);
            state.Journal.Append(
                JournalOperationKind.Backup, item.RelativePath, backupPath, target, item.Size, true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            state.Journal.Append(
                JournalOperationKind.Error, item.RelativePath, target, string.Empty, item.Size, false,
                CoreText.Format(
                    "Could not keep a copy of the original file for undo: {0}",
                    "无法保留原件副本（覆盖后不能撤销）：{0}",
                    ex.Message));
            return null;
        }
    }
}
