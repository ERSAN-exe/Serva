using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.Core.Models;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 绑定属性（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>目标移动设备（只列出移动存储设备，按设备总线识别）。</summary>
    public DeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            // 刷新设备列表时 ListView 会先把自己清空并回写 <c>null</c>：这是中间态，直接忽略，
            // 否则「同一台设备重新扫描」会被误判成更换设备而清空配置。
            // （选中项已改为单向绑定，正常路径不会再回写；这里只是兜底。）
            if (value is null && _isRefreshingDevices)
            {
                return;
            }

            DeviceInfo? previous = _selectedDevice;

            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MappingSummary));
            RaiseMappingPanelState();
            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(DeviceCard));

            // 「重新扫描」会重建整份 DeviceInfo（可用空间等时刻在变）。设备身份（硬件 ID + 卷序列号）
            // 没变就还是同一台设备：保留对应关系与比较结果。
            if (value is not null && value.IsSameDevice(previous))
            {
                return;
            }

            // 设备被拔出 / 未检测到：对应关系属于设备，界面上一并清空；
            // 配置仍留在设备根目录的 .serva-mappings.json 与本机设置里，重新接入同一台设备会自动恢复。
            if (value is null)
            {
                ClearMappingsInMemory();
                ResetResults(() => Text.T("Select a removable device.", "请选择一个移动设备。"));
                return;
            }

            // 换了设备：清掉上一台设备的对应关系（它的配置留在它自己根目录的文件里，不受影响），
            // 然后尝试读取新设备根目录里的 .serva-mappings.json —— 对应关系跟着设备走。
            string? knownRoot = _settings.LastTargetRoot;
            string? knownIdentity = _settings.LastTargetIdentity;

            if (Mappings.Count > 0)
            {
                Mappings.Clear();
                _settings.Mappings = [];
                SaveSettings();
            }

            _settings.LastTargetRoot = value.RootPath;
            _settings.LastTargetIdentity = value.Identity;
            SaveSettings();

            // 换了设备：读新设备自己根目录里的对应关系（上一台设备的配置留在它自己的文件里，不受影响）。
            RestoreMappings(value, knownIdentity, knownRoot);

            ResetResults(() => Mappings.Count > 0
                ? Text.T(
                    "Mappings are ready. Click \"Scan and compare\".",
                    "对应关系已就绪，请点击「扫描并比较」。")
                : Text.T(
                    "Add a mapping, then click \"Scan and compare\".",
                    "请添加对应关系，然后点击「扫描并比较」。"));
        }
    }

    /// <summary>是否可以添加对应关系（需要先选中设备）。</summary>
    public bool CanAddMapping => _selectedDevice is not null && !IsBusy && !IsRunning;

    /// <summary>
    /// 是否已经选中了一台设备。
    /// <para>
    /// 没选中时右侧「对应关系」整块锁死（列表与按钮都不可操作），只显示「选择一个可移动设备」：
    /// 对应关系属于设备，没有目标设备时任何改动都没有可写回的地方，还容易把旧配置覆盖掉。
    /// </para>
    /// </summary>
    public bool IsDeviceSelected => _selectedDevice is not null;

    /// <summary>「对应关系」列表的可见性：只在选中设备后显示。</summary>
    public Visibility MappingListVisibility => IsDeviceSelected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>「选择一个可移动设备」提示的可见性：没选中设备时显示。</summary>
    public Visibility DeviceHintVisibility => IsDeviceSelected ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>「对应关系」整块是否可用：没有设备时连同移除按钮一起禁用。</summary>
    public bool IsMappingPanelEnabled => IsDeviceSelected && !IsBusy && !IsRunning;

    /// <summary>「已添加 2 条对应关系」。</summary>
    public string MappingSummary => Mappings.Count == 0
        ? Text.T("No mappings yet", "尚未添加对应关系")
        : Text.F("{0} mapping(s) added", "已添加 {0} 条对应关系", Mappings.Count);

    /// <summary>
    /// 扫描摘要（「扫描 1.2 s · 本机 36,383 项 / 设备 …」）。
    /// 句子本身按当前语言生成，因此把生成器一起存下来，切换语言后可以重新生成。
    /// </summary>
    public string ScanSummary
    {
        get => _scanSummary;
        private set => SetProperty(ref _scanSummary, value);
    }

    /// <summary>设置扫描摘要（同时记住生成方式）。</summary>
    private void SetScanSummary(Func<string> factory)
    {
        _scanSummaryFactory = factory;
        ScanSummary = factory();
    }

    /// <summary>「合并 · 将执行 812 项 · 4.02 GB · 共 836 项」。</summary>
    public string PlanSummary
    {
        get
        {
            SyncPlan? plan = _plan;
            if (plan is null || plan.TotalCount == 0)
            {
                return Text.T("No plan yet", "尚无计划");
            }

            string conflicts = ConflictCount > 0
                ? Action switch
                {
                    // 合并：两边都改过的文件保留较新版本（见 PlanBuilder.DecideConflict）。
                    ResultAction.Merge => Text.F(
                        " · {0} conflict(s): newer version wins",
                        " · {0} 项冲突取较新版本",
                        ConflictCount),

                    // 覆盖：冲突文件直接以源端为准写过去。
                    _ => Text.F(
                        " · {0} conflict(s) overwritten",
                        " · {0} 项冲突将被覆盖",
                        ConflictCount),
                }
                : string.Empty;

            return Text.F(
                "{0} · run {1} item(s) · {2} · {3} item(s) in total{4}",
                "{0} · 将执行 {1} 项 · {2} · 共 {3} 项{4}",
                EffectiveModeText,
                plan.SelectedCount,
                DeviceInfo.Format(plan.SelectedWriteBytes),
                plan.TotalCount,
                conflicts);
        }
    }

    private ResultAction _action;

    /// <summary>扫描结果弹窗里的处理方式（合并 / 覆盖某一侧内容）。</summary>
    public ResultAction Action
    {
        get => _action;
        set
        {
            if (!SetProperty(ref _action, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ActionIndex));
            OnPropertyChanged(nameof(ActionIconGlyph));
            OnPropertyChanged(nameof(MergeHintText));
            OnPropertyChanged(nameof(OverwriteLocalHintText));
            OnPropertyChanged(nameof(OverwriteDeviceHintText));
            OnPropertyChanged(nameof(MergeHintVisibility));
            OnPropertyChanged(nameof(OverwriteLocalHintVisibility));
            OnPropertyChanged(nameof(OverwriteDeviceHintVisibility));
            OnPropertyChanged(nameof(PlanSummary));
            Replan();
        }
    }

    /// <summary>与弹窗里 <c>RadioButtons.SelectedIndex</c> 双向绑定。</summary>
    public int ActionIndex
    {
        get => (int)Action;
        set
        {
            if (value >= 0 && value <= (int)ResultAction.OverwriteDeviceToLocal)
            {
                Action = (ResultAction)value;
            }
        }
    }

    /// <summary>本机侧盘符；多条对应关系位于不同盘时回退为「本机盘」。</summary>
    public string LocalDriveText
    {
        get
        {
            string[] drives =
            [
                .. Mappings.Select(static m => CompareRowViewModel.DriveOf(m.LocalRoot))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];

            return drives.Length == 1 ? drives[0] : Text.T("local disk", "本机盘");
        }
    }

    /// <summary>设备侧盘符（如 <c>E:</c>）。</summary>
    public string DeviceDriveText => Mappings.Count == 0
        ? Text.T("device", "设备盘")
        : CompareRowViewModel.DriveOf(Mappings[0].DeviceRoot);

    /// <summary>本机侧盘符卡片（小字角色 + 盘符），用于结果页的执行方式。</summary>
    public DriveCardViewModel LocalCard => new(Text.T("Local disk", "本机硬盘"), LocalDriveText);

    /// <summary>设备侧盘符卡片：角色里带上卷标（如「外置设备 · 0123」）。</summary>
    public DriveCardViewModel DeviceCard => new(DeviceRoleText, DeviceDriveText);

    /// <summary>
    /// 设备卡片的角色文案：移动设备是「外置设备（卷标）」，
    /// 打开「允许选择本机硬盘」后选中的本机数据盘则显示「本机硬盘」。
    /// </summary>
    private string DeviceRoleText => _selectedDevice switch
    {
        { IsRemovableStorage: false } => Text.T("Local disk", "本机硬盘"),
        { VolumeLabel.Length: > 0 } device => Text.F("Removable device · {0}", "外置设备 · {0}", device.VolumeLabel),
        _ => Text.T("Removable device", "外置设备"),
    };

    /// <summary>盘符卡片之间显示的图标：合并是双向（同步）图标，覆盖是单向箭头。</summary>
    public string ActionIconGlyph => Action switch
    {
        ResultAction.OverwriteLocalToDevice => "\uE111",   // →
        ResultAction.OverwriteDeviceToLocal => "\uE112",   // ←
        _ => "\uE117",                                     // 双向（同步）
    };

    /// <summary>「合并」的提示（蓝色）：双向补充较新文件，两边都改过的取较新版本。</summary>
    public string MergeHintText => Text.T(
        "Both sides fill in each other's newer files; files changed on both sides keep the newer version.",
        "两个方向互相补充较新的文件；两边都修改过的文件保留较新版本。");

    /// <summary>「用本机内容覆盖设备」的提示（红色）；下一行是黑色说明 +「执行策略」按钮。</summary>
    public string OverwriteLocalHintText => Text.F(
        "Overwrite the device with the contents of the local disk ({0}).",
        "用本机硬盘（{0}）的内容覆盖设备。",
        LocalDriveText);

    /// <summary>
    /// 「用设备内容覆盖本机硬盘」的提示（红色）。
    /// <para>
    /// 不再写死「本机多出来的文件会被删除」：删不删、是否移入回收站由<strong>全局执行策略</strong>决定，
    /// 下一行给出「执行策略」的跳转（与「覆盖设备」那句保持一致）。
    /// </para>
    /// </summary>
    public string OverwriteDeviceHintText => Text.F(
        "Overwrite the local disk ({0}) with the contents of the device.",
        "用设备的内容覆盖本机硬盘（{0}）。",
        LocalDriveText);

    public Visibility MergeHintVisibility => Action == ResultAction.Merge ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OverwriteLocalHintVisibility =>
        Action == ResultAction.OverwriteLocalToDevice ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OverwriteDeviceHintVisibility =>
        Action == ResultAction.OverwriteDeviceToLocal ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 当前处理方式对应的（同步模式, 是否强制覆盖）。
    /// <para>合并：两个方向互相补充较新的文件；两边都修改过的文件保留较新版本。</para>
    /// </summary>
    internal (SyncMode Mode, bool Force) EffectivePlan => Action switch
    {
        ResultAction.OverwriteLocalToDevice => (SyncMode.BackupToDevice, true),
        ResultAction.OverwriteDeviceToLocal => (SyncMode.DeviceToLocal, true),

        // 合并（默认）：两个方向互相补充较新的文件；两端都改过的文件保留双方。
        _ => (SyncMode.TwoWay, false),
    };

    /// <summary>「合并」/「覆盖 C: → E:」式的处理方式文案。</summary>
    internal string EffectiveModeText => Action switch
    {
        ResultAction.OverwriteLocalToDevice => Text.F("Overwrite {0} → {1}", "覆盖 {0} → {1}", LocalDriveText, DeviceDriveText),
        ResultAction.OverwriteDeviceToLocal => Text.F("Overwrite {0} → {1}", "覆盖 {0} → {1}", DeviceDriveText, LocalDriveText),
        _ => Text.T("Merge", "合并"),
    };

    public int ConflictCount => _plan?.CountOf(CompareStatus.Conflict) ?? 0;

    public bool HasResults => _plan is not null;
}

/// <summary>扫描结果弹窗里选择的处理方式。</summary>
public enum ResultAction
{
    /// <summary>合并（默认）：按页面选择的同步模式处理。</summary>
    Merge,

    /// <summary>覆盖本机盘的内容到设备（本机为准，强制覆盖）。</summary>
    OverwriteLocalToDevice,

    /// <summary>覆盖设备的内容到本机盘（设备为准，强制覆盖）。</summary>
    OverwriteDeviceToLocal,
}
