using Microsoft.UI.Xaml;
using Serva.App.Localization;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>
/// 「设置 → 排除规则」列表里的一条规则。
/// <para>
/// 内置规则（<see cref="ExcludeMatcher.DefaultExcludePatterns"/>，例如
/// <c>System Volume Information</c>、<c>Thumbs.db</c>、Serva 自己的临时文件）<strong>不可删除</strong>：
/// 它们保证同步不会碰到系统目录、回收站与 Serva 自身的工作文件。
/// </para>
/// </summary>
public sealed record ExcludeRuleViewModel(string Pattern, bool IsBuiltIn)
{
    /// <summary>内置规则旁边锁形标记的提示文字（按当前语言生成）。</summary>
    public string BuiltInTooltip =>
        AppText.Current.T("Built-in rule (cannot be removed)", "内置规则（不可删除）");

    /// <summary>删除按钮的可见性：内置规则不显示删除按钮。</summary>
    public Visibility RemoveButtonVisibility => IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>锁形标记的可见性：只有内置规则显示。</summary>
    public Visibility BuiltInBadgeVisibility => IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
}
