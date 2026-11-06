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

        // 系统盘根目录 / 系统文件夹一律不能当同步来源（界面上已经选不到，这里再兜一层）。
        ProtectedPathKind blocked = ProtectedPaths.Classify(localRoot);
        if (blocked is not ProtectedPathKind.None)
        {
            SetBanner(
                () => blocked is ProtectedPathKind.SystemDriveRoot
                    ? Text.ProtectedSystemDriveRoot
                    : Text.ProtectedSystemFolder,
                InfoBarSeverity.Error);
            return false;
        }

        string local = localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string folder = MappingViewModel.Normalize(deviceFolder);

        // 界面上的列表可能还停留在上一台设备（例如刚切换过设备）：先让它变成「这台设备」的列表，
        // 否则新加的对应关系会被连同旧列表一起写进错误的那台设备。
        EnsureMappingsOwnedBy(device);

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

    /// <summary>
    /// 界面上的这一批对应关系属于哪台设备（<see cref="DeviceInfo.StorageKey"/>）。
    /// <para>
    /// 多设备时必须有这个标记：否则切换设备后旧列表还在，既会被当成新设备的对应关系显示出来，
    /// 又可能被原样写进另一台设备的配置文件（对应关系「串台」，扫描时提示路径不存在）。
    /// </para>
    /// </summary>
    private string? _mappingsOwnerKey;

    /// <summary>区分「哪一台设备」的键（同一个卷才算同一台设备）。</summary>
    private static string VolumeKey(DeviceInfo device) => device.StorageKey;

    /// <summary>
    /// 确保界面上的对应关系属于 <paramref name="device"/>：不是这台设备的就重新读回来。
    /// </summary>
    private void EnsureMappingsOwnedBy(DeviceInfo device)
    {
        if (string.Equals(VolumeKey(device), _mappingsOwnerKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RestoreMappings(device);
    }

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

    /// <summary>
    /// 保存当前对应关系：本机设置里按设备分开存一段，同时写回<strong>这台设备</strong>根目录的
    /// <c>.serva-mappings.json</c>。别的设备的记录完全不受影响。
    /// </summary>
    private void SaveMappings()
    {
        if (_selectedDevice is not { } device)
        {
            // 没选中设备时界面被锁死，正常不会走到这里；真走到了也不要写坏任何设备的记录。
            return;
        }

        if (!string.Equals(VolumeKey(device), _mappingsOwnerKey, StringComparison.OrdinalIgnoreCase))
        {
            // 界面上的列表不是这台设备的（理应不会发生）：宁可不保存，也不要把一台设备的
            // 对应关系写进另一台设备的配置文件 —— 那正是「对应关系串台」的来源。
            return;
        }

        List<MappingSetting> mappings =
        [
            .. Mappings.Select(static m => new MappingSetting { LocalRoot = m.LocalRoot, DeviceFolder = m.DeviceFolder }),
        ];

        _settings.SetMappingsForDevice(device, mappings);
        _mappingsOwnerKey = VolumeKey(device);
        SaveSettings();

        SaveMappingsToDevice(device);
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
    /// 清空界面上的对应关系（<strong>不</strong>写回设置与设备文件）：
    /// 设备被拔出、切换设备、或用户点了「重新扫描」时用 —— 配置本身仍保留在设备文件与本机设置里。
    /// <para>同时忘掉「这批列表属于哪台设备」：下次读回时一定会重新判断。</para>
    /// </summary>
    private void ClearMappingsInMemory()
    {
        _mappingsOwnerKey = null;

        if (Mappings.Count == 0)
        {
            return;
        }

        Mappings.Clear();
        OnPropertyChanged(nameof(MappingSummary));
        RaiseMappingPanelState();
        OnPropertyChanged(nameof(CanScan));
    }

    /// <summary>
    /// 右侧「对应关系」面板的状态一起通知：是否可用、显示列表还是显示「选择一个可移动设备」。
    /// <para>
    /// 这几项都取决于「有没有选中设备」与「是否正在忙」，只通知 <see cref="CanAddMapping"/>
    /// 会让列表可见性 / 整块禁用停在旧状态。
    /// </para>
    /// </summary>
    private void RaiseMappingPanelState()
    {
        OnPropertyChanged(nameof(CanAddMapping));
        OnPropertyChanged(nameof(IsDeviceSelected));
        OnPropertyChanged(nameof(MappingListVisibility));
        OnPropertyChanged(nameof(DeviceHintVisibility));
        OnPropertyChanged(nameof(IsMappingPanelEnabled));
    }

    /// <summary>
    /// 接入 / 切换设备时恢复对应关系：只读<strong>这台设备自己</strong>的配置
    /// （本机按设备保存的记录优先，其次设备根目录里的文件）。
    /// </summary>
    private void RestoreMappings(DeviceInfo device)
    {
        string key = VolumeKey(device);

        if (!string.Equals(key, _mappingsOwnerKey, StringComparison.OrdinalIgnoreCase))
        {
            // 换了一台设备（或同一块硬盘上的另一个分区）：旧列表必须先扔掉，
            // 否则它会被显示成这台设备的对应关系，还会被原样写进这台设备的配置文件。
            ClearMappingsInMemory();
        }
        else if (Mappings.Count > 0)
        {
            // 同一台设备重新接入 / 只是设备列表重建：刷新设备上的标识文件即可。
            SaveMappingsToDevice(device);
            return;
        }

        _mappingsOwnerKey = key;

        IReadOnlyList<MappingSetting> saved = LoadSavedMappings(device);
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

        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanScan));
        RaiseMappingPanelState();

        if (Mappings.Count == 0)
        {
            return;
        }

        // 读到了就写回本机记录与设备文件，两边始终保持一致。
        SaveMappings();
    }

    /// <summary>
    /// 取这台设备保存过的对应关系。
    /// <para>
    /// 本机按设备保存的记录优先 —— 它记的是「用户在这台设备上的最后一次选择」，
    /// 因此删除过的对应关系不会因为设备文件写不进去（只读卡 / 空间不足）又冒出来；
    /// 本机还没有这台设备的记录时，才去读设备根目录里的 <c>.serva-mappings.json</c>（对应关系跟着设备走）。
    /// </para>
    /// </summary>
    private IReadOnlyList<MappingSetting> LoadSavedMappings(DeviceInfo device)
    {
        if (_settings.TryGetMappingsForDevice(device, out IReadOnlyList<MappingSetting> cached))
        {
            return cached;
        }

        DeviceMappingFile? onDevice = DeviceMappingStore.Load(device.RootPath);
        if (onDevice is null || !device.MatchesIdentity(onDevice.DeviceIdentity))
        {
            // 文件不存在 / 损坏 / 是别的设备写的：这台设备就是没有对应关系。
            return [];
        }

        return DeviceMappingStore.MappingsForCurrentComputer(onDevice);
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
        RaiseMappingPanelState();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(LocalDriveText));
        OnPropertyChanged(nameof(DeviceDriveText));
        OnPropertyChanged(nameof(LocalSideName));
        OnPropertyChanged(nameof(DeviceSideName));
        OnPropertyChanged(nameof(LocalCards));
        OnPropertyChanged(nameof(DeviceCard));
        OnPropertyChanged(nameof(MergeOptionText));
        OnPropertyChanged(nameof(OverwriteDeviceOptionText));
        OnPropertyChanged(nameof(OverwriteLocalOptionText));
        OnPropertyChanged(nameof(FilterLocalNewerText));
        OnPropertyChanged(nameof(FilterDeviceNewerText));
        OnPropertyChanged(nameof(FilterDeviceOnlyText));
        OnPropertyChanged(nameof(FilterLocalOnlyText));
        OnPropertyChanged(nameof(MergeHintText));
        OnPropertyChanged(nameof(OverwriteLocalHintText));
        OnPropertyChanged(nameof(OverwriteDeviceHintText));
        RaiseCommands();
    }

    private void OnRowSelectionChanged(object? sender, EventArgs e) => UpdateDerivedSummaries();
}
