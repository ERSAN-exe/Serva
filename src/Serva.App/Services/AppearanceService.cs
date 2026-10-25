using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serva.App.Localization;
using Serva.Core.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Serva.App.Services;

/// <summary>
/// 把「外观」设置（明暗主题）应用到窗口。
/// <para>
/// 明暗主题通过窗口根元素的 <see cref="FrameworkElement.RequestedTheme"/> 生效（可运行时切换）；
/// 启动时还会设置 <see cref="Application.RequestedTheme"/>，以便 Mica 等系统材质跟随。
/// </para>
/// <para>
/// 「跟随系统」会解析成<strong>具体的</strong>浅色 / 深色，并监听系统的明暗变化：
/// <see cref="ElementTheme.Default"/> 的含义是「用应用启动时确定的主题」，
/// 系统之后切换到深色 / 浅色时它不会跟着变，所以这里不用它。
/// </para>
/// <para>强调色始终跟随系统主题色，应用不做覆盖。</para>
/// </summary>
public static class AppearanceService
{
    public const string SystemTheme = "system";
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    /// <summary>系统级设置（明暗、主题色）；第一次用到时创建。</summary>
    private static UISettings? _systemSettings;

    /// <summary>系统明暗变化后需要就地重刷的窗口。</summary>
    private static Window? _watchedWindow;

    /// <summary>是否已经挂上「系统主题变化」的监听。</summary>
    private static bool _watchingSystemTheme;

    public static string NormalizeTheme(string? theme) =>
        theme is LightTheme or DarkTheme ? theme : SystemTheme;

    /// <summary>
    /// 把设置里的主题解析成「当下真正的」明暗主题。
    /// <para>
    /// 「跟随系统」在这里展开成具体的浅色 / 深色：<see cref="ElementTheme.Default"/> 表示的是
    /// 「用应用启动时确定的主题」，系统之后切换深色 / 浅色时它不会跟着变。
    /// </para>
    /// </summary>
    public static ElementTheme ResolveElementTheme(string? theme) => NormalizeTheme(theme) switch
    {
        LightTheme => ElementTheme.Light,
        DarkTheme => ElementTheme.Dark,
        _ => IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light,
    };

    /// <summary>
    /// 系统当前是否处于深色模式：读系统的「应用」背景色
    /// （浅色模式是纯白，深色模式接近纯黑，按亮度取阈值最稳）。
    /// </summary>
    public static bool IsSystemDark()
    {
        if (SystemSettings is not { } settings)
        {
            // 拿不到系统设置（极少数精简系统）：退回应用启动时确定的主题。
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }

        try
        {
            Color background = settings.GetColorValue(UIColorType.Background);
            return background.R + background.G + background.B < 3 * 128;
        }
        catch (Exception)
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }

    /// <summary>系统级设置实例：创建失败时返回 <see langword="null"/>，调用方自行退化。</summary>
    private static UISettings? SystemSettings
    {
        get
        {
            if (_systemSettings is not null)
            {
                return _systemSettings;
            }

            try
            {
                _systemSettings = new UISettings();
            }
            catch (Exception)
            {
                return null;
            }

            return _systemSettings;
        }
    }

    public static string ThemeLabel(string? theme) => NormalizeTheme(theme) switch
    {
        LightTheme => AppText.Current.ThemeLight,
        DarkTheme => AppText.Current.ThemeDark,
        _ => AppText.Current.ThemeSystem,
    };

    /// <summary>
    /// 当前应使用的外观主题。对话框（<c>ContentDialog</c>）挂在弹出层，不会继承窗口根元素的
    /// <see cref="FrameworkElement.RequestedTheme"/>，必须显式设置 —— 否则暗色模式下会弹出亮色对话框。
    /// </summary>
    public static ElementTheme CurrentElementTheme =>
        ResolveElementTheme(SettingsService.Current.Value.Theme);

    /// <summary>在创建窗口之前应用默认主题，使 Mica 等系统材质从一开始就正确。</summary>
    public static void ApplyAtStartup(AppSettings settings)
    {
        try
        {
            Application.Current.RequestedTheme = NormalizeTheme(settings.Theme) switch
            {
                LightTheme => ApplicationTheme.Light,
                DarkTheme => ApplicationTheme.Dark,
                _ => Application.Current.RequestedTheme,
            };
        }
        catch (InvalidOperationException)
        {
            // WinUI 只允许在创建界面之前修改；错过时机时忽略，运行时的元素级主题仍然生效。
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    /// <summary>把外观设置应用到已创建的窗口（设置页切换时立即生效）。</summary>
    public static void Apply(Window window, AppSettings settings)
    {
        ElementTheme theme = ResolveElementTheme(settings.Theme);

        try
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }

            ApplyTitleBar(window, theme);
            WatchSystemTheme(window);
        }
        catch (InvalidOperationException)
        {
            // 窗口已经关闭（退出过程中仍可能收到 Activated / 系统颜色变化）：直接忽略。
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 同上：WinUI 在窗口释放后再访问 Content / AppWindow 会抛 COMException。
        }
    }

    /// <summary>
    /// 订阅「系统明暗 / 主题色变化」：选择「跟随系统」时，系统切到深色或浅色后界面要立即跟着换。
    /// <para>固定为浅色 / 深色时重复应用同一个值不会产生可见变化，所以不需要判断当前选的是哪种。</para>
    /// </summary>
    private static void WatchSystemTheme(Window window)
    {
        _watchedWindow = window;

        if (_watchingSystemTheme || SystemSettings is not { } settings)
        {
            return;
        }

        settings.ColorValuesChanged += OnSystemColorsChanged;
        _watchingSystemTheme = true;
    }

    /// <summary>系统颜色变化：事件在后台线程触发，改界面主题必须切回 UI 线程。</summary>
    private static void OnSystemColorsChanged(UISettings sender, object args)
    {
        if (_watchedWindow is not { } window)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_watchedWindow is { } target)
            {
                Apply(target, SettingsService.Current.Value);
            }
        });
    }

    private static void ApplyTitleBar(Window window, ElementTheme theme)
    {
        // ResolveElementTheme 只会给出具体的浅色 / 深色，「跟随系统」在解析时就已经展开。
        bool dark = theme == ElementTheme.Dark;

        AppWindowTitleBar bar = window.AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x99, 0x00, 0x00, 0x00);
    }
}
