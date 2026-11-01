using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serva.App.Localization;
using Serva.Core.Models;

namespace Serva.App.Controls;

/// <summary>把 <see cref="CompareStatus"/> 映射为界面文案与颜色。</summary>
internal static class StatusPresentation
{
    /// <summary>
    /// 状态文案。<paramref name="leftLabel"/> / <paramref name="rightLabel"/> 为对应关系两侧的盘符。
    /// <para>
    /// <paramref name="useDriveNames"/> = 两侧同类（都在移动设备 / 都在本机硬盘）：
    /// 「仅本机 / 仅设备」此时分不清是哪一块盘，改用「仅 D: / 仅 G:」。
    /// </para>
    /// </summary>
    public static string Text(
        CompareStatus status,
        string? leftLabel = null,
        string? rightLabel = null,
        bool useDriveNames = false)
    {
        AppText text = AppText.Current;
        string left = leftLabel ?? text.T("Local", "本机");
        string right = rightLabel ?? text.T("Device", "设备");

        return status switch
        {
            CompareStatus.Same => text.T("Same", "相同"),
            CompareStatus.LeftOnly => useDriveNames
                ? text.F("{0} only", "仅 {0}", left)
                : text.T("Local only", "仅本机"),
            CompareStatus.RightOnly => useDriveNames
                ? text.F("{0} only", "仅 {0}", right)
                : text.T("Device only", "仅设备"),
            CompareStatus.LeftNewer => text.F("{0} is newer", "{0} 较新", left),
            CompareStatus.RightNewer => text.F("{0} is newer", "{0} 较新", right),
            CompareStatus.Conflict => text.T("Conflict", "冲突"),
            CompareStatus.Delete => text.T("Will delete", "将删除"),
            _ => status.ToString(),
        };
    }

    public static Brush Brush(CompareStatus status)
    {
        string key = status switch
        {
            CompareStatus.LeftNewer => "ServaStatusLeftNewerBrush",
            CompareStatus.RightNewer => "ServaStatusRightNewerBrush",
            CompareStatus.LeftOnly => "ServaStatusLeftOnlyBrush",
            CompareStatus.RightOnly => "ServaStatusRightOnlyBrush",
            CompareStatus.Conflict => "ServaStatusConflictBrush",
            CompareStatus.Delete => "ServaStatusDeleteBrush",
            _ => "ServaStatusSameBrush",
        };

        return Resource(key);
    }

    /// <summary>
    /// 警告弹窗的红色标题（与「失败」同色）。<see cref="ContentDialog.Title"/> 支持任意内容，
    /// 页面把 Title 设为它即可让标题统一成红色。
    /// </summary>
    public static TextBlock WarningTitle(string text) => new()
    {
        Text = text,
        Foreground = Resource("SystemFillColorCriticalBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>弹窗里的次要说明文字：小号、浅灰（跟随主题）。</summary>
    public static TextBlock MutedNote(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Resource("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
