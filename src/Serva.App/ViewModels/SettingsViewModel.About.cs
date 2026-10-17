using Microsoft.UI.Xaml.Media;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 关于（partial）。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>应用名（关于卡片的标题）。</summary>
    public string AppName => "Serva";

    /// <summary>版本号（唯一来源：Serva.App.csproj 的 <c>&lt;Version&gt;</c>）。</summary>
    public string VersionText => "v" + AppVersion.Current;

    /// <summary>作者（姓名是专有名词，两种语言下都保持中文写法）。</summary>
    public string AuthorText => AppText.Current.F("By {0}", "作者：{0}", "铃一贰叁");

    /// <summary>
    /// 关于卡片的小图标：随程序输出的 <c>Assets\icon.png</c>（与窗口标题栏共用）。
    /// </summary>
    public ImageSource AppIcon => AppIconProvider.Icon;

    /// <summary>GitHub 项目地址（「项目地址」链接的跳转目标）。</summary>
    public Uri ProjectUri { get; } = new("https://github.com/ERSAN-exe/Serva");
}
