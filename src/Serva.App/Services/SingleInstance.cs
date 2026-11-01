using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Serva.App.Services;

/// <summary>
/// 单实例保护：同一时间只允许运行一个 Serva 进程。
/// <para>
/// 两个进程同时跑会同时写同一个 <c>settings.json</c>、同一份日志与撤销记录，
/// 还可能同时往同一块设备里拷文件 —— 所以第二个实例直接把已有窗口提到前台后退出。
/// </para>
/// </summary>
internal static class SingleInstance
{
    /// <summary>互斥体名字（<c>Local\</c> = 仅当前登录会话，多用户同时登录互不干扰）。</summary>
    private const string MutexName = @"Local\Serva.SingleInstance.7B1F2C4A";

    private const int SwRestore = 9;

    /// <summary>持有到进程结束：进程退出时系统自动释放。</summary>
    private static Mutex? _mutex;

    /// <summary>
    /// 尝试成为唯一实例。
    /// <returns>
    /// <c>true</c> = 当前进程是第一个实例，可以继续启动；
    /// <c>false</c> = 已经有实例在运行（已把它切到前台），调用方应当立即退出。
    /// </returns>
    /// </summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 拿不到互斥体（极少见）：宁可让程序照常启动，也不要因为保护机制打不开软件。
            return true;
        }

        ActivateExistingWindow();
        return false;
    }

    /// <summary>把已经在运行的那个 Serva 窗口还原并切到前台。</summary>
    private static void ActivateExistingWindow()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();

            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                {
                    if (process.Id == current.Id)
                    {
                        continue;
                    }

                    IntPtr window = process.MainWindowHandle;
                    if (window == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(window, SwRestore);
                    SetForegroundWindow(window);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // 找不到 / 没权限操作那个窗口：忽略，第二个实例照样退出。
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
