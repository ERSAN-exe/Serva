using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 按对应关系扫描与比较（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>
    /// 按当前对应关系扫描两端并生成计划（扫描完成后由页面弹出结果对话框）。
    /// </summary>
    /// <returns><c>true</c> 表示扫描成功（结果可以查看）；设备被拔出 / 路径读不到时返回 <c>false</c>。</returns>
    public async Task<bool> ScanAndCompareAsync()
    {
        if (!CanScan || _selectedDevice is not { } device)
        {
            return false;
        }

        // 旧配置里可能还留着指向系统盘根目录 / 系统文件夹的对应关系：直接拒绝，
        // 绝不能拿它去扫描、更不能拿它执行（镜像删除会把系统当「多余文件」）。
        if (Mappings.FirstOrDefault(static m => ProtectedPaths.IsProtected(m.LocalRoot)) is { } blocked)
        {
            SetBanner(
                () => Text.F(
                    "This mapping points at a protected folder and can't be used: {0}",
                    "这条对应关系指向受保护的文件夹，无法使用：{0}",
                    blocked.LocalRoot),
                InfoBarSeverity.Error);
            return false;
        }

        IsBusy = true;
        ClearBanner();
        SetScanSummary(() => Text.T("Scanning…", "正在扫描…"));

        List<MappingViewModel> mappings = [.. Mappings];
        string volumeSerial = device.VolumeSerial;
        bool succeeded = false;

        try
        {
            SyncOptions options = _settings.Options;
            var matcher = new ExcludeMatcher(
                options.ExcludePatterns, options.IncludePatterns, options.AlwaysIncludePatterns);
            var progress = new Progress<ScanProgress>(p => SetScanSummary(() => Text.F(
                "Scanning: {0:N0} item(s) · {1}",
                "正在扫描：{0:N0} 项 · {1}",
                p.FilesFound,
                DeviceInfo.Format(p.BytesFound))));

            var results = await Task.Run(
                () =>
                {
                    var leftScanner = new FileScanner(matcher, options.SkipHiddenAndSystem, options.IncludeSubdirectories);
                    var rightScanner = new FileScanner(matcher, options.SkipHiddenAndSystem, options.IncludeSubdirectories);

                    var list = new List<(MappingViewModel Mapping, ScanResult Left, ScanResult Right)>();

                    foreach (MappingViewModel mapping in mappings)
                    {
                        ScanResult left = leftScanner.Scan(mapping.LocalRoot, null, progress);
                        ScanResult right = rightScanner.Scan(mapping.DeviceFolderAbsolute, volumeSerial, progress);
                        list.Add((mapping, left, right));
                    }

                    return list;
                },
                CancellationToken.None);

            ResetMappingStates();

            TimeSpan elapsed = TimeSpan.Zero;
            int leftFiles = 0;
            int rightFiles = 0;

            foreach ((MappingViewModel mapping, ScanResult left, ScanResult right) in results)
            {
                _states.Add(new MappingState { Mapping = mapping, Left = left, Right = right });
                elapsed += left.Duration + right.Duration;
                leftFiles += left.FileCount;
                rightFiles += right.FileCount;
            }

            SetScanSummary(() => Text.F(
                "Scan {0:0.0} s · local {1:N0} item(s) / device {2:N0} item(s) · {3} mapping(s)",
                "扫描 {0:0.0} s · 本机 {1:N0} 项 / 设备 {2:N0} 项 · {3} 条对应关系",
                elapsed.TotalSeconds,
                leftFiles,
                rightFiles,
                _states.Count));

            Replan();

            // 扫描完了却一条可比条目都没有：说清楚「两端都没有可同步的文件」，
            // 别让用户以为扫描没跑（结果页照样会打开，里面是同样的空态说明）。
            if (_allRows.Count == 0)
            {
                SetBanner(() => Text.NoComparableItemsHint, InfoBarSeverity.Warning);
            }

            succeeded = true;
        }
        catch (DirectoryNotFoundException ex)
        {
            DiscardStaleResults();
            SetBanner(() => Text.F("Path not found: {0}", "路径不存在：{0}", ex.Message), InfoBarSeverity.Error);
        }
        catch (IOException ex)
        {
            DiscardStaleResults();
            SetBanner(
                () => Text.F(
                    "Failed to read the device (it may have been unplugged): {0}",
                    "读取设备失败（设备可能已被拔出）：{0}",
                    ex.Message),
                InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            DiscardStaleResults();
            SetBanner(
                () => Text.F("No permission to read this path: {0}", "没有权限读取该路径：{0}", ex.Message),
                InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }

        return succeeded;
    }

    /// <summary>
    /// 扫描失败（设备被拔出 / 路径读不到）时把上一轮的结果作废。
    /// <para>
    /// 否则结果页还会拿着<strong>旧的计划</strong>照常打开 —— 表现就是「设备一断开，反而进了结果页」。
    /// </para>
    /// </summary>
    private void DiscardStaleResults()
    {
        ResetMappingStates();
        SetScanSummary(() => Text.T(
            "Scan failed. Reconnect the device and scan again.",
            "扫描失败，请重新接入设备后再扫描。"));
        UpdateDerivedSummaries();
    }
}
