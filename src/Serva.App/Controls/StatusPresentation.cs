using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serva.App.Localization;
using Serva.Core.Models;

namespace Serva.App.Controls;

/// <summary>把 <see cref="CompareStatus"/> 映射为界面文案与颜色。</summary>
internal static class StatusPresentation
{
    /// <summary>状态文案。<paramref name="leftLabel"/> / <paramref name="rightLabel"/> 为对应关系两侧的盘符。</summary>
    public static string Text(CompareStatus status, string? leftLabel = null, string? rightLabel = null)
    {
        AppText text = AppText.Current;

        return status switch
        {
            CompareStatus.Same => text.T("Same", "相同"),
            CompareStatus.LeftOnly => text.T("Local only", "仅本机"),
            CompareStatus.RightOnly => text.T("Device only", "仅设备"),
            CompareStatus.LeftNewer => text.F("{0} is newer", "{0} 较新", leftLabel ?? text.T("Local", "本机")),
            CompareStatus.RightNewer => text.F("{0} is newer", "{0} 较新", rightLabel ?? text.T("Device", "设备")),
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

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
