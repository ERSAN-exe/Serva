using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.Core.Models;

namespace Serva.App.ViewModels;

/// <summary>结果表中的一行。</summary>
public sealed class CompareRowViewModel : ObservableObject
{
    public CompareRowViewModel(PlanItem item, MappingViewModel mapping)
    {
        Model = item;
        Mapping = mapping;
    }

    /// <summary>勾选状态变化时通知父级刷新「将执行 N 项」。</summary>
    public event EventHandler? SelectionChanged;

    public PlanItem Model { get; }

    /// <summary>这一行属于哪条对应关系（执行时需要用它解析两侧的根目录）。</summary>
    public MappingViewModel Mapping { get; }

    public bool IsSelected
    {
        get => Model.IsSelected;
        set
        {
            if (Model.IsSelected == value)
            {
                return;
            }

            Model.IsSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string RelativePath => Model.RelativePath;

    /// <summary>结果表第一列显示的文件名（目录条目回退为最后一段）。</summary>
    public string FileName => Path.GetFileName(RelativePath) is { Length: > 0 } name ? name : RelativePath;

    public string SizeText => DeviceInfo.Format(Model.Size);

    /// <summary>
    /// 修改时间列：显示<strong>比较新旧时用的那份时间</strong> —— 状态列说哪一侧较新，这里就是那一侧的时间；
    /// 只在单侧存在时显示该侧时间，其余（相同 / 冲突 / 删除）取本机优先。
    /// </summary>
    public string ModifiedText => Model.DisplayStatus switch
    {
        CompareStatus.RightNewer when Model.Item.Right is { } right => FormatTime(right.LastWriteTimeUtc),
        CompareStatus.LeftNewer when Model.Item.Left is { } left => FormatTime(left.LastWriteTimeUtc),
        CompareStatus.RightOnly when Model.Item.Right is { } right => FormatTime(right.LastWriteTimeUtc),
        CompareStatus.LeftOnly when Model.Item.Left is { } left => FormatTime(left.LastWriteTimeUtc),
        _ => Model.LastWriteTimeUtc is { } time ? FormatTime(time) : "—",
    };

    /// <summary>
    /// 修改时间列的悬停说明：两端各自的修改时间（精确到秒）—— 新旧就是按这两个时间判定的。
    /// </summary>
    public string ModifiedTooltip
    {
        get
        {
            AppText text = AppText.Current;
            FileEntry? left = Model.Item.Left;
            FileEntry? right = Model.Item.Right;

            if (left is not null && right is not null)
            {
                return text.F(
                    "Local: {0}\nDevice: {1}",
                    "本机：{0}\n设备：{1}",
                    FormatTime(left.LastWriteTimeUtc, withSeconds: true),
                    FormatTime(right.LastWriteTimeUtc, withSeconds: true));
            }

            if (left is not null)
            {
                return text.F(
                    "Local only: {0}",
                    "仅本机：{0}",
                    FormatTime(left.LastWriteTimeUtc, withSeconds: true));
            }

            return right is not null
                ? text.F("Device only: {0}", "仅设备：{0}", FormatTime(right.LastWriteTimeUtc, withSeconds: true))
                : string.Empty;
        }
    }

    private static string FormatTime(DateTime utc, bool withSeconds = false) =>
        utc.ToLocalTime().ToString(withSeconds ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm");

    public CompareStatus DisplayStatus => Model.DisplayStatus;

    /// <summary>状态文案：较新的一侧显示该侧盘符（如「E: 较新」）。</summary>
    public string StatusText => StatusPresentation.Text(Model.DisplayStatus, LocalDrive, DeviceDrive);

    /// <summary>本机侧盘符（<c>D:</c>）。</summary>
    public string LocalDrive => DriveOf(Mapping.LocalRoot);

    /// <summary>设备侧盘符（<c>E:</c>）。</summary>
    public string DeviceDrive => DriveOf(Mapping.DeviceRoot);

    public Brush StatusBrush => StatusPresentation.Brush(Model.DisplayStatus);

    public string ActionText => Model.ActionText;

    /// <summary>
    /// 「将执行」列的徽章文案：目标端已有同名文件时用「覆盖到本机 / 覆盖到设备」，
    /// 新增文件用「复制到本机 / 复制到设备」，删除等其它动作沿用计划里的说明。
    /// </summary>
    public string ActionBadgeText
    {
        get
        {
            AppText text = AppText.Current;
            bool targetExists = Model.Action switch
            {
                PlanAction.CopyToLocal => Model.Item.Left is not null,
                PlanAction.CopyToDevice => Model.Item.Right is not null,
                _ => false,
            };

            return Model.Action switch
            {
                PlanAction.CopyToLocal => targetExists
                    ? text.T("Overwrite → local", "覆盖到本机")
                    : text.T("Copy → local", "复制到本机"),
                PlanAction.CopyToDevice => targetExists
                    ? text.T("Overwrite → device", "覆盖到设备")
                    : text.T("Copy → device", "复制到设备"),
                _ => Model.ActionText,
            };
        }
    }

    /// <summary>徽章颜色：覆盖 / 删除用红色（与「冲突 / 将删除」同一套语义色），新增复制用绿色，其余中性灰。</summary>
    public Brush ActionBadgeBrush => Model.Action switch
    {
        PlanAction.CopyToLocal when Model.Item.Left is not null => Resource("ServaStatusDeleteBrush"),
        PlanAction.CopyToDevice when Model.Item.Right is not null => Resource("ServaStatusDeleteBrush"),
        PlanAction.CopyToLocal or PlanAction.CopyToDevice => Resource("ServaStatusLeftOnlyBrush"),
        PlanAction.DeleteOnLocal or PlanAction.DeleteOnDevice => Resource("ServaStatusDeleteBrush"),
        _ => Resource("ServaStatusSameBrush"),
    };

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    internal static string DriveOf(string path)
    {
        string? root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root)
            ? path
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

/// <summary>状态统计卡已从界面移除（保留类型以便将来复用）。</summary>
public sealed class StatViewModel
{
    public StatViewModel(string value, string label, CompareStatus? status = null)
    {
        Value = value;
        Label = label;
        Foreground = status is null ? DefaultForeground() : StatusPresentation.Brush(status.Value);
    }

    public string Value { get; }

    public string Label { get; }

    /// <summary>为 <c>null</c> 时使用默认前景色。</summary>
    public Brush? Foreground { get; }

    /// <summary>主题正文色；取不到时退回中性灰。</summary>
    private static Brush DefaultForeground() =>
        Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
