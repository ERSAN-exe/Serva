using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 按对应关系扫描与比较（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>按当前对应关系扫描两端并生成计划（扫描完成后由页面弹出结果对话框）。</summary>
    public async Task ScanAndCompareAsync()
    {
        if (!CanScan || _selectedDevice is not { } device)
        {
            return;
        }

        IsBusy = true;
        ClearBanner();
        SetScanSummary(() => Text.T("Scanning…", "正在扫描…"));

        List<MappingViewModel> mappings = [.. Mappings];
        string volumeSerial = device.VolumeSerial;

        try
        {
            SyncOptions options = _settings.Options;
            var matcher = new ExcludeMatcher(options.ExcludePatterns, options.IncludePatterns);
            var progress = new Progress<ScanProgress>(p => SetScanSummary(() => Text.F(
                "Scanning: {0:N0} item(s) · {1}",
                "正在扫描：{0:N0} 项 · {1}",
                p.FilesFound,
                DeviceInfo.Format(p.BytesFound))));

            var results = await Task.Run(
                () =>
                {
                    var leftScanner = new FileScanner(matcher, options.SkipHiddenAndSystem, options.IncludeSubdirectories);
                    var rightScanner = new FileScanner(
                        matcher, options.SkipHiddenAndSystem, options.IncludeSubdirectories, _snapshotStore);

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
        }
        catch (DirectoryNotFoundException ex)
        {
            SetBanner(() => Text.F("Path not found: {0}", "路径不存在：{0}", ex.Message), InfoBarSeverity.Error);
        }
        catch (IOException ex)
        {
            SetBanner(
                () => Text.F(
                    "Failed to read the device (it may have been unplugged): {0}",
                    "读取设备失败（设备可能已被拔出）：{0}",
                    ex.Message),
                InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            SetBanner(
                () => Text.F("No permission to read this path: {0}", "没有权限读取该路径：{0}", ex.Message),
                InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
