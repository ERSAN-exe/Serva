using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 进度聚合与执行后复扫（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>进度区里「当前文件」一行的状态。</summary>
    private enum RunTextState
    {
        /// <summary>还没开始 / 正在准备。</summary>
        Idle,

        /// <summary>正在处理某个文件。</summary>
        Running,

        /// <summary>本次执行已经结束。</summary>
        Completed,
    }

    private void ResetRunningState(int total, long totalBytes)
    {
        _progressCompleted = 0;
        _progressGrandTotal = total;
        _progressBytes = 0;
        _progressGrandBytes = totalBytes;
        _progressBytesPerSecond = 0;
        _progressRemaining = null;
        _runState = RunTextState.Idle;
        _runCurrentRelativePath = string.Empty;

        RunningPercent = 0;
        RunningCompletedText = "0";
        RunningQueuedText = total.ToString("N0");
        RunningSkippedText = "0";
        RunningFailedText = "0";
        RefreshRunningTexts();
    }

    /// <summary>把「当前对应关系」的进度换算成整体进度。</summary>
    private void OnSyncProgress(SyncProgress progress, int baseItems, long baseBytes, int grandTotal, long grandBytes)
    {
        int completed = baseItems + progress.Completed;
        long bytes = baseBytes + progress.CopiedBytes;

        RunningPercent = grandTotal <= 0 ? 0 : Math.Min(100, completed * 100.0 / grandTotal);

        _progressCompleted = completed;
        _progressGrandTotal = grandTotal;
        _progressBytes = bytes;
        _progressGrandBytes = grandBytes;
        _progressBytesPerSecond = progress.BytesPerSecond;
        _progressRemaining = progress.BytesPerSecond > 1 && grandBytes > bytes
            ? TimeSpan.FromSeconds((grandBytes - bytes) / progress.BytesPerSecond)
            : null;
        _runState = RunTextState.Running;
        _runCurrentRelativePath = progress.CurrentRelativePath;

        RunningCompletedText = completed.ToString("N0");
        RunningQueuedText = Math.Max(0, grandTotal - completed - progress.Failed).ToString("N0");
        RunningSkippedText = progress.Skipped.ToString("N0");
        RunningFailedText = progress.Failed.ToString("N0");
        RefreshRunningTexts();
    }

    /// <summary>本次执行结束：进度满格，「当前文件」一行显示「已完成」。</summary>
    private void MarkRunCompleted(int completed, int skipped, int failed)
    {
        RunningPercent = 100;
        _progressCompleted = completed;
        _runState = RunTextState.Completed;

        RunningCompletedText = completed.ToString("N0");
        RunningQueuedText = "0";
        RunningSkippedText = skipped.ToString("N0");
        RunningFailedText = failed.ToString("N0");
        RefreshRunningTexts();
    }

    /// <summary>进度区里带文案的几行重新取值（进度推进时、切换语言时都用它）。</summary>
    private void RefreshRunningTexts()
    {
        OnPropertyChanged(nameof(RunningCountText));
        OnPropertyChanged(nameof(RunningDetailText));
        OnPropertyChanged(nameof(RunningCurrentFile));
    }

    /// <summary>执行后重新扫描设备侧（命中快照则秒级返回），刷新结果表。</summary>
    private async Task RescanDeviceAsync()
    {
        if (_selectedDevice is not { } device || _states.Count == 0)
        {
            return;
        }

        SyncOptions options = _settings.Options;
        var matcher = new ExcludeMatcher(options.ExcludePatterns, options.IncludePatterns);
        string volumeSerial = device.VolumeSerial;

        try
        {
            await Task.Run(() =>
            {
                var scanner = new FileScanner(
                    matcher, options.SkipHiddenAndSystem, options.IncludeSubdirectories);

                foreach (MappingState state in _states)
                {
                    state.Right = scanner.Scan(state.Mapping.DeviceFolderAbsolute, volumeSerial);
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            SetBanner(
                () => Text.F("Failed to refresh the device state: {0}", "刷新设备状态失败：{0}", ex.Message),
                InfoBarSeverity.Warning);
            return;
        }

        SetScanSummary(() => Text.F(
            "Refreshed after the run · {0} mapping(s)",
            "执行后已刷新 · {0} 条对应关系",
            _states.Count));
        Replan();
    }
}
