using System.Globalization;
using Serva.App.Localization;
using Serva.Core.Localization;
using Serva.Core.Models;
using Windows.Globalization;

namespace Serva.App.Services;

/// <summary>
/// 界面语言：把 <see cref="AppSettings.Language"/> 应用到界面文案与 .NET 区域设置。
/// <para>
/// 可选值：<c>system</c>（跟随系统，默认）/ <c>zh-Hans</c>（简体中文）/ <c>en-US</c>（English）。
/// 「跟随系统」按系统的界面语言取中文或英文 —— 软件只提供这两种语言。
/// </para>
/// <para>
/// 文案本身由 <see cref="AppText"/>（界面）与 <see cref="CoreText"/>（核心层）提供，
/// 这里只负责决定「现在用哪种语言」，并把结果通知出去。
/// </para>
/// </summary>
public static class LanguageService
{
    /// <summary>跟随系统。</summary>
    public const string SystemLanguage = "system";

    /// <summary>English。</summary>
    public const string EnglishLanguage = "en-US";

    /// <summary>简体中文。</summary>
    public const string ChineseLanguage = "zh-Hans";

    private static string? _systemCulture;

    /// <summary>把设置里的语言值归一为三种取值之一（未知值按「跟随系统」处理）。</summary>
    public static string Normalize(string? language) => language switch
    {
        ChineseLanguage or "zh" or "zh-CN" => ChineseLanguage,
        EnglishLanguage or "en" => EnglishLanguage,
        _ => SystemLanguage,
    };

    /// <summary>0 = 跟随系统，1 = 简体中文，2 = English（与设置页的单选按钮对应）。</summary>
    public static int IndexOf(string? language) => Normalize(language) switch
    {
        ChineseLanguage => 1,
        EnglishLanguage => 2,
        _ => 0,
    };

    public static string FromIndex(int index) => index switch
    {
        1 => ChineseLanguage,
        2 => EnglishLanguage,
        _ => SystemLanguage,
    };

    /// <summary>把设置应用到界面（语言切换时立即调用）。</summary>
    public static void Apply(AppSettings settings)
    {
        string culture = ResolveCulture(settings.Language);
        AppText.Current.SetLanguage(culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        ApplyCulture(culture);
    }

    /// <summary>启动时应用：先记住系统语言（之后当前线程的区域设置会被覆盖），再按设置生效。</summary>
    public static void ApplyAtStartup(AppSettings settings)
    {
        _systemCulture = DetectSystemCulture();

        // 核心层（Serva.Core）的文案走同一个语言状态。
        CoreText.Resolver = static (english, chinese) => AppText.Current.T(english, chinese);

        Apply(settings);
    }

    /// <summary>当前设置对应的区域名：<c>en-US</c> 或 <c>zh-Hans</c>。</summary>
    public static string ResolveCulture(string? language) => Normalize(language) switch
    {
        ChineseLanguage => "zh-Hans",
        EnglishLanguage => "en-US",
        _ => _systemCulture ??= DetectSystemCulture(),
    };

    /// <summary>系统界面语言是中文则用简体中文，否则英文（软件只有这两种语言）。</summary>
    private static string DetectSystemCulture() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-Hans"
            : "en-US";

    private static void ApplyCulture(string cultureName)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (ArgumentException)
        {
            // 系统不支持该区域：保持现状，文案表仍然已经切换。
            return;
        }

        try
        {
            // 让框架自带的文本（如系统对话框）也跟随所选语言；不支持时忽略。
            ApplicationLanguages.PrimaryLanguageOverride = cultureName;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
