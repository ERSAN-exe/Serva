using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serva.App.Localization;
using Serva.Core.Models;
using Windows.UI;

namespace Serva.App.Services;

/// <summary>
/// 把「外观」设置（明暗主题）应用到窗口。
/// <para>
/// 明暗主题通过窗口根元素的 <see cref="FrameworkElement.RequestedTheme"/> 生效（可运行时切换）；
/// 启动时还会设置 <see cref="Application.RequestedTheme"/>，以便 Mica 等系统材质跟随。
/// </para>
/// <para>强调色始终跟随系统主题色，应用不做覆盖。</para>
/// </summary>
public static class AppearanceService
{
    public const string SystemTheme = "system";
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    public static string NormalizeTheme(string? theme) =>
        theme is LightTheme or DarkTheme ? theme : SystemTheme;

    public static ElementTheme ResolveElementTheme(string? theme) => NormalizeTheme(theme) switch
    {
        LightTheme => ElementTheme.Light,
        DarkTheme => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

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

        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        ApplyTitleBar(window, theme);
    }

    private static void ApplyTitleBar(Window window, ElementTheme theme)
    {
        bool dark = theme == ElementTheme.Dark
                    || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

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
