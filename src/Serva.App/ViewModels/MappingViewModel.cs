using Serva.App.Localization;
using Serva.Core.Models;

namespace Serva.App.ViewModels;

/// <summary>
/// 一条对应关系：本机文件夹 ↔ 移动设备上的文件夹。
/// </summary>
public sealed class MappingViewModel
{
    public MappingViewModel(string localRoot, string deviceFolder, string deviceRoot)
    {
        LocalRoot = localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DeviceFolder = Normalize(deviceFolder);
        DeviceRoot = deviceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// 当前语言的界面文案。
    /// <para>
    /// 列表的 <c>DataTemplate</c> 里 <c>x:Bind</c> 的根是<strong>这一行</strong>（而不是页面），
    /// 所以行视图模型也要暴露文案表，模板里的提示文字才能按语言刷新。
    /// </para>
    /// </summary>
    public AppText Text => AppText.Current;

    /// <summary>本机来源文件夹。</summary>
    public string LocalRoot { get; }

    /// <summary>设备上的文件夹（相对设备根目录；空表示设备根目录）。</summary>
    public string DeviceFolder { get; }

    /// <summary>该对应关系所属的设备根目录。</summary>
    public string DeviceRoot { get; }

    /// <summary>设备侧文件夹的绝对路径。</summary>
    public string DeviceFolderAbsolute => DeviceFolder.Length == 0
        ? DeviceRoot + Path.DirectorySeparatorChar
        : Path.Combine(DeviceRoot, DeviceFolder.Replace('/', Path.DirectorySeparatorChar));

    public string DeviceDisplay => DeviceFolder.Length == 0
        ? DeviceRoot + Path.DirectorySeparatorChar
        : (DeviceRoot + Path.DirectorySeparatorChar + DeviceFolder.Replace('/', Path.DirectorySeparatorChar));

    internal static string Normalize(string? value) =>
        (value ?? string.Empty).Replace('\\', '/').Trim('/');
}
