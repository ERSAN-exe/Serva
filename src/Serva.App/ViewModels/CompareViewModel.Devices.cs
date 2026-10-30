using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 窗口挂接与移动设备发现（partial）。</summary>
public sealed partial class CompareViewModel
{
    private DispatcherQueue? _dispatcher;
    private Window? _window;
    private string? _deviceSignature;
    private bool _isRefreshingDevices;

    /// <summary>
    /// 当前的设备集合（身份 + 盘符），用于判断「设备集合是否真的变了（插入 / 拔出）」。
    /// <para>
    /// 不能用 <see cref="_deviceSignature"/> 判断：用户点「重新扫描」时会先把它清空，
    /// 那样同一台设备也会被当成「设备变了」。
    /// </para>
    /// </summary>
    private readonly HashSet<string> _knownDevices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 用户点过「重新扫描」：界面上的对应关系被清空、设备也不再自动选中，
    /// 直到用户自己点选设备（或插拔设备）为止 —— 避免在空白列表上改配置，把设备里的旧配置覆盖掉。
    /// </summary>
    private bool _mappingsClearedByRescan;

    /// <summary>挂接窗口：注册 WM_DEVICECHANGE 并启动 3 秒兜底轮询。</summary>
    public void AttachWindow(Window window)
    {
        if (_deviceWatcher is not null)
        {
            return;
        }

        _window = window;
        _dispatcher = window.DispatcherQueue;
        _deviceWatcher = new DeviceWatcher(DriveFilter.Removable);
        _deviceWatcher.DevicesChanged += OnDevicesChanged;
        _deviceWatcher.AttachToWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        _deviceWatcher.Start();
    }

    public void Dispose()
    {
        SettingsService.Current.Changed -= OnSettingsChanged;
        UpdateService.Current.Changed -= OnUpdateChanged;

        if (_deviceWatcher is not null)
        {
            _deviceWatcher.DevicesChanged -= OnDevicesChanged;
            _deviceWatcher.Dispose();
            _deviceWatcher = null;
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
    }

    /// <summary>供设置页调用：立即重新扫描设备列表。</summary>
    public void RequestDeviceRefresh() => RefreshDevices();

    /// <summary>供日志页调用：撤销之后比较结果已不可信，清空并提示重扫。</summary>
    public void InvalidateResults(Func<string> summary) => ResetResults(summary);

    /// <param name="userInitiated">是否是用户点了「重新扫描」（会清空界面上的对应关系，并等用户重新选择设备）。</param>
    private void RefreshDevices(bool userInitiated = false)
    {
        _deviceSignature = null;

        if (userInitiated)
        {
            // 重新扫描：清空界面上的对应关系、取消设备选中 —— 右侧「对应关系」随之锁定，
            // 只提示「选择一个可移动设备」；等用户重新点选设备时才读回那台设备的配置。
            // （配置本身仍在设备文件与本机设置里，不会丢。）
            _mappingsClearedByRescan = true;
            ClearMappingsInMemory();
            SelectedDevice = null;
            ResetResults(() => Text.T(
                "Device list refreshed. Select a removable device.",
                "设备列表已刷新，请选择一个可移动设备。"));
        }

        ApplyDevices(ResolveVisibleDevices());

        _deviceWatcher?.CheckNow();
        UpdateDerivedSummaries();
    }

    /// <summary>
    /// 设备列表：默认只列出<strong>移动存储设备</strong>（U 盘 / 移动硬盘 / 存储卡）；
    /// 在设置里打开「允许选择本机硬盘」后，本机的数据盘也会一起列出（系统盘始终排除）。
    /// <para>
    /// 移动设备的判定依据是设备总线（USB / SD / MMC / 1394）与「介质可移动」标志，而不是 <c>DriveType</c> ——
    /// 相当多的移动盘会被 Windows 报成固定磁盘。
    /// </para>
    /// </summary>
    private static IReadOnlyList<DeviceInfo> ResolveVisibleDevices() =>
        VolumeInfo.GetDrives(_settings.AllowLocalDrives ? DriveFilter.AllDataDrives : DriveFilter.Removable);

    /// <summary>
    /// 应用设备列表。仅在结果集合真正变化时重建，因此 3 秒轮询不会反复刷新列表、也不会重置用户的选择。
    /// </summary>
    private void ApplyDevices(IReadOnlyList<DeviceInfo> devices)
    {
        // 指纹里带「设备身份」：同一个盘符换成另一台设备时也要重建列表。
        string signature = string.Join(
            "|",
            devices.Select(static d => $"{d.RootPath}:{d.Identity}:{d.VolumeLabel}:{d.TotalSize}"));

        if (string.Equals(signature, _deviceSignature, StringComparison.Ordinal))
        {
            // 列表本身没变，但界面上的对应关系可能还是空的（设备刚挂载时设备文件还没读到、
            // 或上一次读取因为设备忙而失败）：每次轮询顺手再读一次 ——
            // 读不到东西不会有副作用（不会写设备文件），读到了就把配置补上。
            if (_selectedDevice is { } stale && Mappings.Count == 0)
            {
                RestoreMappings(stale, _settings.LastTargetIdentity, _settings.LastTargetRoot);
            }

            return;
        }

        _deviceSignature = signature;

        // 设备集合真的变了（插入 / 拔出）：结束「重新扫描后等用户重选」的状态 ——
        // 插上设备就应该自动选中它并读回它自己的对应关系。
        if (UpdateKnownDevices(devices))
        {
            _mappingsClearedByRescan = false;
        }

        DeviceInfo? activeDeviceBefore = _selectedDevice;

        // 注意：下面的赋值会覆盖 LastTargetRoot / LastTargetIdentity，所以要先把「上次使用的设备」取出来。
        string? lastKnownRoot = _settings.LastTargetRoot;
        string? lastKnownIdentity = _settings.LastTargetIdentity;
        string? previousIdentity = _selectedDevice?.Identity ?? lastKnownIdentity;
        string? previousRoot = _selectedDevice?.RootPath ?? lastKnownRoot;

        // 重建列表期间忽略 ListView 回写的空选中（见 SelectedDevice 的说明）。
        _isRefreshingDevices = true;
        try
        {
            Devices.Clear();
            foreach (DeviceInfo device in devices)
            {
                Devices.Add(device);
            }
        }
        finally
        {
            _isRefreshingDevices = false;
        }

        if (_mappingsClearedByRescan)
        {
            // 刚点过「重新扫描」、设备集合也没变：保持未选中状态（右侧锁定 + 提示选择一个可移动设备），
            // 等用户自己点选设备时才读回那台设备的配置。
            SelectedDevice = null;
        }
        else
        {
            SelectedDevice = FindDevice(devices, previousIdentity, previousRoot);

            // 只有重新接入「上次使用的那台设备」（身份一致）时才沿用本机设置里的对应关系；
            // 设备自己根目录里的 .serva-mappings.json 则由 SelectedDevice 的 setter 负责读取。
            if (_selectedDevice is { } current)
            {
                RestoreMappings(current, lastKnownIdentity, lastKnownRoot);
            }
        }

        UpdateDeviceBanner(devices);

        // 运行中目标设备被拔出：立即中止执行（已完成的部分保留）。
        bool deviceGone = activeDeviceBefore is { } before
                          && !devices.Any(d => d.IsSameDevice(before))
                          && !Directory.Exists(before.RootPath);

        if (deviceGone && IsRunning)
        {
            CancelExecution();
            SetBanner(
                () => Text.T(
                    "The target device was unplugged; the run was stopped. Files already written are kept.",
                    "目标设备已被拔出，执行已中止；已完成的文件保持原样。"),
                InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// 记录当前的设备集合并返回「集合是否真的变了（插入 / 拔出）」。
    /// <para>同一台设备换盘符 / 换卷标不算插拔，只有设备的加入或移除才算。</para>
    /// </summary>
    private bool UpdateKnownDevices(IReadOnlyList<DeviceInfo> devices)
    {
        var current = new HashSet<string>(
            devices.Select(static d => $"{d.Identity}|{d.RootPath}"),
            StringComparer.OrdinalIgnoreCase);

        bool changed = current.Count != _knownDevices.Count || !current.SetEquals(_knownDevices);

        _knownDevices.Clear();
        _knownDevices.UnionWith(current);
        return changed;
    }

    /// <summary>
    /// 用户点选了设备列表里的某一台设备：重新读取这台设备的对应关系
    /// （对应关系跟着设备走，重新选择设备就应该看到它自己的配置）。
    /// </summary>
    public void SelectDevice(DeviceInfo device)
    {
        // 用户主动选择设备：结束「重新扫描」后的清空状态，读回这台设备的配置。
        _mappingsClearedByRescan = false;

        if (!ReferenceEquals(_selectedDevice, device))
        {
            SelectedDevice = device;
        }

        if (_selectedDevice is { } current)
        {
            RestoreMappings(current, _settings.LastTargetIdentity, _settings.LastTargetRoot);
        }

        OnPropertyChanged(nameof(MappingSummary));
        OnPropertyChanged(nameof(CanScan));
        RaiseMappingPanelState();
    }
    /// <summary>优先按设备身份匹配（换了盘符也能找回），其次按盘符匹配，最后退回第一台设备。</summary>
    private static DeviceInfo? FindDevice(IReadOnlyList<DeviceInfo> devices, string? identity, string? rootPath)
    {
        if (!string.IsNullOrEmpty(identity))
        {
            DeviceInfo? byIdentity = devices.FirstOrDefault(
                d => string.Equals(d.Identity, identity, StringComparison.OrdinalIgnoreCase));

            if (byIdentity is not null)
            {
                return byIdentity;
            }
        }

        if (!string.IsNullOrEmpty(rootPath))
        {
            DeviceInfo? byRoot = devices.FirstOrDefault(
                d => string.Equals(d.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

            if (byRoot is not null)
            {
                return byRoot;
            }
        }

        return devices.FirstOrDefault();
    }

    /// <summary>
    /// 是否为「上次使用的那台设备」：优先比对设备身份；旧版设置文件里没有身份时退回盘符比对。
    /// </summary>
    private static bool IsLastKnownDevice(DeviceInfo device, string? identity, string? rootPath) =>
        !string.IsNullOrEmpty(identity)
            ? string.Equals(device.Identity, identity, StringComparison.OrdinalIgnoreCase)
            : string.Equals(device.RootPath, rootPath, StringComparison.OrdinalIgnoreCase);

    private void UpdateDeviceBanner(IReadOnlyList<DeviceInfo> devices)
    {
        if (devices.Count > 0)
        {
            if (BannerSeverity == InfoBarSeverity.Warning)
            {
                ClearBanner();
            }

            return;
        }

        SetBanner(
            () => Text.T(
                "No removable storage device detected. Plug in a USB drive, then click \"Rescan\".",
                "未检测到移动存储设备。请插入 U 盘 / 移动硬盘后点击「重新扫描」。"),
            InfoBarSeverity.Warning);
    }

    private void OnDevicesChanged(object? sender, IReadOnlyList<DeviceInfo> devices)
    {
        if (_dispatcher is null)
        {
            ApplyDevices(devices);
            return;
        }

        _dispatcher.TryEnqueue(() => ApplyDevices(devices));
    }
}
