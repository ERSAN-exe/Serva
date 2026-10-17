using System.Runtime.InteropServices;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 可移动设备监听：<b>3 秒兜底轮询</b> + 可选 <c>WM_DEVICECHANGE</c> 消息挂钩（插入 / 拔出即时刷新）。
/// <para>事件可能在线程池线程上触发，UI 层需自行调度回 UI 线程。</para>
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    private const uint WmDeviceChange = 0x0219;
    private const uint DbtDeviceArrival = 0x8000;
    private const uint DbtDeviceRemoveComplete = 0x8004;
    private const uint DbtDevNodesChanged = 0x0007;
    private const int GwlWndProc = -4;

    private DriveFilter _filter;
    private readonly object _gate = new();
    private readonly Timer _pollTimer;
    private readonly Timer _debounceTimer;

    private string _signature;
    private bool _disposed;
    private nint _hwnd;
    private nint _previousWndProc;
    private WndProcDelegate? _wndProcDelegate;

    public DeviceWatcher(DriveFilter filter = DriveFilter.Removable, TimeSpan? interval = null)
    {
        _filter = filter;
        Interval = interval ?? TimeSpan.FromSeconds(3);
        _pollTimer = new Timer(OnPoll, null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer = new Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);
        _signature = ComputeSignature();
    }

    /// <summary>兜底轮询间隔（默认 3 秒）。</summary>
    public TimeSpan Interval { get; set; }

    /// <summary>设备集合发生变化。参数为变化后的设备列表。</summary>
    public event EventHandler<IReadOnlyList<DeviceInfo>>? DevicesChanged;

    /// <summary>设备列表筛选方式（可随时切换，切换后由调用方自行触发一次刷新）。</summary>
    public DriveFilter Filter
    {
        get => _filter;
        set => _filter = value;
    }

    /// <summary>立即读取当前设备列表。</summary>
    public IReadOnlyList<DeviceInfo> GetDevices() => VolumeInfo.GetDrives(_filter);

    /// <summary>开始监听（含 3 秒兜底轮询）。</summary>
    public void Start()
    {
        lock (_gate)
        {
            _signature = ComputeSignature();
        }

        _pollTimer.Change(Interval, Interval);
    }

    /// <summary>停止监听。</summary>
    public void Stop() => _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>
    /// 挂接窗口以接收 <c>WM_DEVICECHANGE</c>（卷的到达 / 移除会广播给所有顶层窗口，无需注册通知）。
    /// </summary>
    /// <param name="hwnd">WinUI 3 窗口的 HWND（<c>WinRT.Interop.WindowNative.GetWindowHandle</c>）。</param>
    public void AttachToWindow(nint hwnd)
    {
        if (hwnd == 0 || _hwnd == hwnd)
        {
            return;
        }

        DetachFromWindow();

        _wndProcDelegate = WindowProc;
        _previousWndProc = SetWindowLongPtrW(hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        if (_previousWndProc == 0)
        {
            _wndProcDelegate = null;
            return;
        }

        _hwnd = hwnd;
    }

    /// <summary>解除窗口挂钩。</summary>
    public void DetachFromWindow()
    {
        if (_hwnd == 0 || _previousWndProc == 0)
        {
            return;
        }

        SetWindowLongPtrW(_hwnd, GwlWndProc, _previousWndProc);
        _hwnd = 0;
        _previousWndProc = 0;
        _wndProcDelegate = null;
    }

    /// <summary>手动触发一次检查（例如「立即重新扫描」按钮）。</summary>
    public void CheckNow()
    {
        string signature = ComputeSignature();
        lock (_gate)
        {
            if (string.Equals(signature, _signature, StringComparison.Ordinal))
            {
                return;
            }

            _signature = signature;
        }

        DevicesChanged?.Invoke(this, GetDevices());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachFromWindow();
        _pollTimer.Dispose();
        _debounceTimer.Dispose();
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmDeviceChange)
        {
            uint evt = unchecked((uint)wParam);
            if (evt is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged)
            {
                // 防抖：卷的到达 / 移除会连续广播多条消息。
                _debounceTimer.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
            }
        }

        return CallWindowProcW(_previousWndProc, hWnd, msg, wParam, lParam);
    }

    private void OnDebounce(object? state)
    {
        if (!_disposed)
        {
            CheckNow();
        }
    }

    private void OnPoll(object? state)
    {
        if (!_disposed)
        {
            CheckNow();
        }
    }

    /// <summary>签名刻意不含剩余空间（它时刻在变，会导致误报）。</summary>
    private string ComputeSignature()
    {
        IReadOnlyList<DeviceInfo> devices = GetDevices();
        return string.Join(
            "|",
            devices.Select(static d => $"{d.RootPath}:{(int)d.DriveType}:{d.VolumeLabel}:{d.TotalSize}:{d.VolumeSerial}"));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProcW(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);
}
