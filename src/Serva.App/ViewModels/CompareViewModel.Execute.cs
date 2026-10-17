using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 执行与取消（partial）。</summary>
public sealed partial class CompareViewModel
{
    private async Task StartExecutionAsync()
    {
        SyncPlan? plan = _plan;
        if (!CanStart || plan is null || _selectedDevice is not { } device || !PrecheckSpace(plan, device))
        {
            return;
        }

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        IsRunning = true;
        ClearBanner();

        int grandTotal = plan.SelectedCount;
        long grandBytes = plan.SelectedWriteBytes;
        ResetRunningState(grandTotal, grandBytes);

        // 一次运行共用同一个日志文件，便于整体撤销。
        var journal = new Journal();
        RecordRunHeader(journal);
        int doneItems = 0;
        long doneBytes = 0;
        int completed = 0, failed = 0, skipped = 0;
        bool canceled = false;
        var failures = new List<SyncFailure>();

        try
        {
            foreach (MappingState state in _states)
            {
                if (_runCts.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                List<PlanItem> items = state.Plan is null
                    ? []
                    : [.. state.Plan.Items.Where(static i => i.IsSelected && i.IsActionable)];

                if (items.Count == 0)
                {
                    continue;
                }

                var pairPlan = new SyncPlan(EffectivePlan.Mode, items);
                int baseItems = doneItems;
                long baseBytes = doneBytes;

                var pairProgress = new Progress<SyncProgress>(
                    p => OnSyncProgress(p, baseItems, baseBytes, grandTotal, grandBytes));

                SyncRunResult result = await _syncEngine.ExecuteAsync(
                    pairPlan,
                    _settings.Options,
                    state.Mapping.LocalRoot,
                    state.Mapping.DeviceFolderAbsolute,
                    pairProgress,
                    journalDirectory: null,
                    cancellationToken: _runCts.Token,
                    journal: journal);

                doneItems += result.Completed;
                doneBytes += result.BytesCopied;
                completed += result.Completed;
                failed += result.Failed;
                skipped += result.Skipped;
                failures.AddRange(result.Failures);

                if (result.Canceled)
                {
                    canceled = true;
                    break;
                }
            }

            MarkRunCompleted(completed, skipped, failed);

            if (failed > 0)
            {
                SyncFailure first = failures[0];
                SetBanner(
                    () => Text.F(
                        "Run finished: {0:N0} succeeded / {1:N0} failed. Example: {2} — {3}",
                        "执行结束：{0:N0} 项成功 / {1:N0} 项失败。示例：{2} —— {3}",
                        completed,
                        failed,
                        first.RelativePath,
                        first.Message),
                    InfoBarSeverity.Error);
            }
            else if (canceled)
            {
                SetBanner(
                    () => Text.T(
                        "The run was canceled. Completed files are kept; no partial files remain.",
                        "执行已取消。已完成的文件保留，未完成的不会留下半截文件。"),
                    InfoBarSeverity.Warning);
            }
            else
            {
                // 只保留完成提示（不再提示日志文件名）：
                // 执行成功：合并了 硬盘C: 和 硬盘E: 的内容，100 个文件被更改。
                string direction = Action switch
                {
                    ResultAction.OverwriteLocalToDevice => Text.F(
                        "overwrote disk {1} with the contents of disk {0}",
                        "用 硬盘{0} 的内容覆盖了 硬盘{1}",
                        LocalDriveText,
                        DeviceDriveText),
                    ResultAction.OverwriteDeviceToLocal => Text.F(
                        "overwrote disk {0} with the contents of disk {1}",
                        "用 硬盘{1} 的内容覆盖了 硬盘{0}",
                        LocalDriveText,
                        DeviceDriveText),
                    _ => Text.F(
                        "merged the contents of disk {0} and disk {1}",
                        "合并了 硬盘{0} 和 硬盘{1} 的内容",
                        LocalDriveText,
                        DeviceDriveText),
                };

                SetBanner(
                    () => Text.F("Run finished: {0}, {1:N0} files changed.", "执行成功：{0}，{1:N0} 个文件被更改。", direction, completed),
                    InfoBarSeverity.Success);
            }

            await RescanDeviceAsync();
            Log.Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            SetBanner(
                () => Text.F(
                    "Run failed (the device may have been unplugged): {0}",
                    "执行失败（设备可能已被拔出）：{0}",
                    ex.Message),
                InfoBarSeverity.Error);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void CancelExecution() => _runCts?.Cancel();

    /// <summary>
    /// 写一条运行抬头日志，例如「由 C: 移动到 E:（合并）」/「由 C: 覆盖到 E:（覆盖 C: 内容）」，
    /// 这样在日志里能一眼看出这次运行的方向与处理方式。
    /// </summary>
    private void RecordRunHeader(Journal journal)
    {
        if (_states.Count == 0)
        {
            return;
        }

        MappingState first = _states[0];

        string description = Action switch
        {
            ResultAction.OverwriteLocalToDevice => Text.F(
                "Overwrote {1} with the contents of {0} (source {0})",
                "由 {0} 覆盖到 {1}（覆盖 {0} 内容）",
                LocalDriveText,
                DeviceDriveText),
            ResultAction.OverwriteDeviceToLocal => Text.F(
                "Overwrote {0} with the contents of {1} (source {1})",
                "由 {1} 覆盖到 {0}（覆盖 {1} 内容）",
                LocalDriveText,
                DeviceDriveText),
            _ => Text.F("Moved from {0} to {1} (merge)", "由 {0} 移动到 {1}（合并）", LocalDriveText, DeviceDriveText),
        };

        // Message 里放简短的处理方式，供日志页顶部的「上次同步」信息直接取用：
        // 覆盖本机硬盘时特别标注，提醒用户本机文件已被设备内容覆盖。
        string mode = Action switch
        {
            ResultAction.OverwriteDeviceToLocal => Text.T("Overwrite local disk", "覆盖本机硬盘"),
            ResultAction.OverwriteLocalToDevice => Text.T("Overwrite", "覆盖"),
            _ => Text.T("Merge", "合并"),
        };

        journal.Append(
            JournalOperationKind.Move,
            description,
            first.Mapping.LocalRoot,
            first.Mapping.DeviceFolderAbsolute,
            bytes: 0,
            success: true,
            message: mode,
            deviceLabel: _selectedDevice?.VolumeLabel);
    }

    /// <summary>执行前预检：目标设备与各本机文件夹的剩余空间。</summary>
    private bool PrecheckSpace(SyncPlan plan, DeviceInfo device)
    {
        long needOnDevice = plan.Items
            .Where(static i => i.IsSelected && i.Action == PlanAction.CopyToDevice)
            .Sum(static i => i.Size);
        long deviceFree = FreeSpaceOf(device.RootPath);

        if (deviceFree > 0 && needOnDevice > deviceFree)
        {
            SetBanner(
                () => Text.F(
                    "Not enough free space on the target device: {0} more needed. Free up space and try again.",
                    "目标设备剩余空间不足：还需要 {0}。请清理设备后重试。",
                    DeviceInfo.Format(needOnDevice - deviceFree)),
                InfoBarSeverity.Error);
            return false;
        }

        foreach (MappingState state in _states)
        {
            if (state.Plan is null)
            {
                continue;
            }

            long need = state.Plan.Items
                .Where(static i => i.IsSelected && i.Action == PlanAction.CopyToLocal)
                .Sum(static i => i.Size);

            if (need == 0)
            {
                continue;
            }

            long free = FreeSpaceOf(state.Mapping.LocalRoot);
            if (free > 0 && need > free)
            {
                SetBanner(
                    () => Text.F(
                        "Not enough free space on {0}: {1} more needed.",
                        "{0} 剩余空间不足：还需要 {1}。",
                        state.Mapping.LocalRoot,
                        DeviceInfo.Format(need - free)),
                    InfoBarSeverity.Error);
                return false;
            }
        }

        return true;
    }

    private static long FreeSpaceOf(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
