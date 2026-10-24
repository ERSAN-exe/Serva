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

                // 在写入前记录目标端是否已有同名文件 —— 撤销时据此决定能否安全删除。
                bool overwritten = File.Exists(target);

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
                    createdDirectories: createdDirectories);
                break;
            }

            case PlanAction.CopyToLocal:
            {
                string source = Combine(state.RightRoot, item.RelativePath);
                string target = Combine(state.LeftRoot, item.TargetFileName ?? item.RelativePath);

                PreserveExistingTarget(state, item, target, state.LeftRoot);
                bool overwritten = File.Exists(target);

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
                    createdDirectories: createdDirectories);
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
}
