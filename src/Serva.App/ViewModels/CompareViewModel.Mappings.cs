using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 对应关系与通用状态（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>添加一条「本机文件夹 ↔ 设备文件夹」对应关系。</summary>
    public bool TryAddMapping(string? localRoot, string? deviceFolder)
    {
        DeviceInfo? device = _selectedDevice;
        if (device is null)
        {
            SetBanner(() => Text.T("Select a removable device first.", "请先选择移动设备。"), InfoBarSeverity.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(localRoot) || !Directory.Exists(localRoot))
        {
            SetBanner(
                () => Text.F("Local folder not found: {0}", "本机文件夹不存在：{0}", localRoot),
                InfoBarSeverity.Error);
            return false;
        }

        string local = localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string folder = MappingViewModel.Normalize(deviceFolder);

        if (Mappings.Any(m => string.Equals(m.LocalRoot, local, StringComparison.OrdinalIgnoreCase)))
        {
            SetBanner(
                () => Text.F("This local folder is already mapped: {0}", "这个本机文件夹已经在对应关系里了：{0}", local),
                InfoBarSeverity.Warning);
            return false;
        }

        if (Mappings.Any(m => string.Equals(m.DeviceFolder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            SetBanner(
                () => Text.F(
                    "This device folder is already mapped: {0}",
                    "这个设备文件夹已经在对应关系里了：{0}",
                    device.RootPath + folder),
                InfoBarSeverity.Warning);
            return false;
        }

        Mappings.Add(new MappingViewModel(local, folder, device.RootPath));
        SaveMappings();

        ClearBanner();
        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanScan));
        ResetResults(MappingsUpdatedPrompt);
        return true;
    }

    /// <summary>「对应关系已更新，请点击『扫描并比较』。」（按当前语言生成）。</summary>
    private static Func<string> MappingsUpdatedPrompt => () => Text.T(
        "Mappings updated. Click \"Scan and compare\".",
        "对应关系已更新，请点击「扫描并比较」。");

    private void RemoveMapping(MappingViewModel? mapping)
    {
        if (mapping is null || IsRunning || !Mappings.Remove(mapping))
        {
            return;
        }

        SaveMappings();
        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanScan));
        ResetResults(MappingsUpdatedPrompt);
    }

    private void SaveMappings()
    {
        _settings.Mappings =
        [
            .. Mappings.Select(static m => new MappingSetting { LocalRoot = m.LocalRoot, DeviceFolder = m.DeviceFolder }),
        ];
        SaveSettings();

        if (_selectedDevice is { } device)
        {
            SaveMappingsToDevice(device);
        }
    }

    /// <summary>
    /// 把当前对应关系写到<strong>该设备</strong>根目录（<c>.serva-mappings.json</c>，隐藏文件）：
    /// 对应关系跟着设备走，换机器 / 设置文件丢失后插上设备就能找回。
    /// <para>
    /// 每次「用户改了对应关系」或「接入设备」都会立刻刷新这个文件，
    /// 因此设备根目录上的标识文件始终与界面上的选择一致。
    /// </para>
    /// </summary>
    private void SaveMappingsToDevice(DeviceInfo device)
    {
        DeviceMappingStore.Save(device.RootPath, new DeviceMappingFile
        {
            DeviceIdentity = device.Identity,
            DeviceName = device.DisplayName,
            SavedAt = DateTime.Now.ToString("s"),
            Mappings =
            [
                .. Mappings.Select(static m => new MappingSetting { LocalRoot = m.LocalRoot, DeviceFolder = m.DeviceFolder }),
            ],
        });
    }

    /// <summary>
    /// 接入设备时恢复对应关系：优先读设备根目录里的文件（对应关系跟着设备走），
    /// 其次沿用本机设置里「上次使用的那台设备」的记录。
    /// </summary>
    /// <summary>
    /// 清空界面上的对应关系（<strong>不</strong>写回设置与设备文件）：
    /// 设备被拔出、或用户点了「重新扫描」时用 —— 配置本身仍保留在设备文件与本机设置里。
    /// </summary>
    private void ClearMappingsInMemory()
    {
        if (Mappings.Count == 0)
        {
            return;
        }

        Mappings.Clear();
        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanAddMapping));
        OnPropertyChanged(nameof(CanScan));
    }
    private void RestoreMappings(DeviceInfo device, string? knownIdentity, string? knownRoot)
    {
        if (Mappings.Count > 0)
        {
            // 内存里已经有对应关系（例如同一台设备重新接入）：不重复从文件恢复，
            // 但把设备上的标识文件刷新一次，保证它反映界面上的当前选择。
            SaveMappingsToDevice(device);
            return;
        }

        IReadOnlyList<MappingSetting> saved = LoadSavedMappings(device, knownIdentity, knownRoot);
        foreach (MappingSetting setting in saved)
        {
            if (string.IsNullOrWhiteSpace(setting.LocalRoot) || !Directory.Exists(setting.LocalRoot))
            {
                continue;
            }

            if (Mappings.Any(m => string.Equals(m.LocalRoot, setting.LocalRoot, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Mappings.Add(new MappingViewModel(setting.LocalRoot, setting.DeviceFolder, device.RootPath));
        }

        if (Mappings.Count == 0)
        {
            return;
        }

        // 对应关系来自设备文件时顺带写回本机设置，两边保持一致（SaveMappings 也会刷新设备文件）；
        // 来自本机设置时必须补一次设备文件 —— 否则设备上的标识文件永远停在旧内容，
        // 用户在选择设备后看不到对应关系被实时写回设备根目录。
        if (!ReferenceEquals(saved, _settings.Mappings))
        {
            SaveMappings();
        }
        else
        {
            SaveMappingsToDevice(device);
        }

        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanScan));
    }

    /// <summary>
    /// 取该设备保存过的对应关系：<c>&lt;设备根&gt;\.serva-mappings.json</c> 优先
    /// （没有本机记录也能恢复）；没有设备文件时，只有「上次使用的那台设备」才沿用
    /// 本机设置里的记录 —— 否则别的设备占用同一盘符时会误用旧配置。
    /// </summary>
    private IReadOnlyList<MappingSetting> LoadSavedMappings(
        DeviceInfo device,
        string? knownIdentity,
        string? knownRoot)
    {
        DeviceMappingFile? onDevice = DeviceMappingStore.Load(device.RootPath);

        if (onDevice?.Mappings is { Count: > 0 } mappings
            && (string.IsNullOrEmpty(onDevice.DeviceIdentity)
                || string.Equals(onDevice.DeviceIdentity, device.Identity, StringComparison.OrdinalIgnoreCase)))
        {
            return mappings;
        }

        return IsLastKnownDevice(device, knownIdentity, knownRoot) ? _settings.Mappings : [];
    }

    /// <summary>显示提示条：句子由生成器产出，切换语言后可以重新生成。</summary>
    private void SetBanner(Func<string> message, InfoBarSeverity severity)
    {
        _bannerFactory = message;
        BannerSeverity = severity;
        BannerMessage = message();
    }

    /// <summary>
    /// 清掉提示条：连「句子生成器」一起清空。
    /// <para>
    /// 只清 <see cref="BannerMessage"/> 是不够的：切换语言时会用生成器重新生成句子，
    /// 已经关掉 / 已经消失的提示条就会又冒出来。
    /// </para>
    /// </summary>
    private void ClearBanner()
    {
        _bannerFactory = null;
        BannerMessage = null;
    }

    /// <summary>页面上的「×」：关闭提示条，切换页面 / 切换语言后都不再出现。</summary>
    public void DismissBanner() => ClearBanner();

    private void UpdateDerivedSummaries()
    {
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanAddMapping));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(LocalDriveText));
        OnPropertyChanged(nameof(DeviceDriveText));
        OnPropertyChanged(nameof(LocalCard));
        OnPropertyChanged(nameof(DeviceCard));
        OnPropertyChanged(nameof(MergeHintText));
        OnPropertyChanged(nameof(OverwriteLocalHintText));
        OnPropertyChanged(nameof(OverwriteDeviceHintText));
        RaiseCommands();
    }

    private void OnRowSelectionChanged(object? sender, EventArgs e) => UpdateDerivedSummaries();
}
