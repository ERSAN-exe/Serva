using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serva.App.Controls;
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

    public string ModifiedText => Model.LastWriteTimeUtc is { } time
        ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "—";

    public CompareStatus DisplayStatus => Model.DisplayStatus;

    /// <summary>状态文案：较新的一侧显示该侧盘符（如「E: 较新」）。</summary>
    public string StatusText => StatusPresentation.Text(Model.DisplayStatus, LocalDrive, DeviceDrive);

    /// <summary>本机侧盘符（<c>D:</c>）。</summary>
    public string LocalDrive => DriveOf(Mapping.LocalRoot);

    /// <summary>设备侧盘符（<c>E:</c>）。</summary>
    public string DeviceDrive => DriveOf(Mapping.DeviceRoot);

    public Brush StatusBrush => StatusPresentation.Brush(Model.DisplayStatus);

    public string ActionText => Model.ActionText;

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
