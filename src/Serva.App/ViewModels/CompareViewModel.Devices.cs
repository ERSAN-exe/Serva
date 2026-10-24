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

    /// <summary>本次刷新是否由用户主动「重新扫描」触发：是的话对应关系以设备上的文件为准。</summary>
    private bool _reloadMappingsFromDevice;

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

    private void RefreshDevices()
    {
        _deviceSignature = null;

        // 用户主动「重新扫描」：对应关系属于设备，先清空界面上的旧列表，再按设备上的文件重新读取；
        // 上一次的比较结果也一并作废（设备可能已经换过 / 内容已被改过）。
        if (_selectedDevice is not null)
        {
            ClearMappingsInMemory();
            ResetResults(MappingsUpdatedPrompt);
            _reloadMappingsFromDevice = true;
        }

        try
        {
            ApplyDevices(ResolveVisibleDevices());
        }
        finally
        {
            _reloadMappingsFromDevice = false;
        }

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
            return;
        }

        _deviceSignature = signature;

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

        SelectedDevice = FindDevice(devices, previousIdentity, previousRoot);

        // 只有重新接入「上次使用的那台设备」（身份一致）时才沿用本机设置里的对应关系；
        // 设备自己根目录里的 .serva-mappings.json 则由 SelectedDevice 的 setter 负责读取。
        if (_selectedDevice is { } current)
        {
            // 「重新扫描」时以设备上的文件为准，不沿用本机设置里的兜底记录。
            RestoreMappings(
                current,
                _reloadMappingsFromDevice ? null : lastKnownIdentity,
                _reloadMappingsFromDevice ? null : lastKnownRoot);
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
