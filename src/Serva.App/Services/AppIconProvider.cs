using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Serva.App.Services;

/// <summary>
/// 应用图标（随程序输出的 <c>Assets\icon.png</c>），供窗口标题栏与「关于」卡片共用。
/// <para>
/// 非打包（unpackaged）应用里用绝对文件路径最稳妥，不依赖 <c>ms-appx:///</c> 的解析规则。
/// </para>
/// </summary>
internal static class AppIconProvider
{
    private static ImageSource? _icon;

    /// <summary>图标位图：首次访问时从磁盘加载，之后复用同一个实例（需在 UI 线程首次访问）。</summary>
    public static ImageSource Icon => _icon ??= Load();

    private static ImageSource Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
        return new BitmapImage(new Uri(path));
    }
}
