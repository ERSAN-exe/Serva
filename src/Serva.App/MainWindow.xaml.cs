using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.App.Views;
using Windows.Graphics;

namespace Serva.App;

/// <summary>
/// 主窗口：Mica 背景 + 自定义标题栏 + NavigationView 三页外壳。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        App.MainWindow = this;

        Title = "Serva";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetWindowIcon();
        ApplyMinWindowSize();

        // Fluent 材质：Mica（Windows 11）。不支持时自动退化为纯色背景。
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }

        ConfigureTitleBar();
        SizeToFitDisplay();

        // 导航项的文案由代码设置：切换语言时立即刷新（Window 上不使用 x:Bind）。
        AppText.Current.PropertyChanged += OnLanguageChanged;
        ApplyNavText();

        // 系统明暗可能在窗口最小化 / 失去焦点期间被改掉：回到前台时再对齐一次外观，
        // 避免漏掉 UISettings 的变更通知（例如从「设置」应用改完主题再切回来）。
        Activated += OnWindowActivated;

        // 有新版时给「设置」导航项挂一个小红点。
        UpdateService.Current.Changed += OnUpdateChanged;
        RefreshUpdateBadge();

        SelectInitialPage();
    }

    /// <summary>「设置」导航项上的更新小红点（有新版才显示）。</summary>
    private void RefreshUpdateBadge() =>
        SettingsNavItem.InfoBadge = UpdateService.Current.HasUpdate ? new InfoBadge { Value = 1 } : null;

    private void OnUpdateChanged(object? sender, EventArgs e) => RefreshUpdateBadge();

    /// <summary>
    /// 窗口重新回到前台：按当前的系统明暗再对齐一次外观。
    /// 「跟随系统」时系统主题可能在窗口失焦期间被改掉，这里兜底一次，不依赖事件一定送达。
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) =>
        AppearanceService.Apply(this, SettingsService.Current.Value);

    /// <summary>
    /// 窗口 / 任务栏图标：使用随程序输出的 <c>Assets\icon.ico</c>；标题栏文字左侧的小图标用 <c>Assets\icon.png</c>。
    /// <para>非打包（unpackaged）应用没有包清单里的图标资源，必须显式设置。</para>
    /// </summary>
    private void SetWindowIcon()
    {
        AppTitleIcon.Source = AppIconProvider.Icon;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            AppWindow.SetIcon(path);
        }
        catch (ArgumentException)
        {
            // 图标文件损坏 / 格式不支持：保留系统默认图标。
        }
    }

    // ============================ 最小窗口尺寸 ============================

    private const int WmGetMinMaxInfo = 0x0024;
    private const int GwlWndProc = -4;

    /// <summary>最小内容尺寸（逻辑像素）：再小设备卡片与结果表格就挤成一团了。</summary>
    private const int MinWidthDip = 900;

    private const int MinHeightDip = 600;

    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>必须保留委托引用，否则被 GC 回收后窗口消息会崩。</summary>
    private WindowProc? _windowProc;

    private nint _previousWindowProc;

    /// <summary>
    /// 限制窗口最小尺寸：WinUI 3 没有现成的 MinWidth/MinHeight，
    /// 这里接管窗口过程并处理 <c>WM_GETMINMAXINFO</c>，按当前 DPI 换算成物理像素。
    /// </summary>
    private void ApplyMinWindowSize()
    {
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        _windowProc = OnWindowMessage;
        _previousWindowProc = SetWindowLongPtr(
            handle, GwlWndProc, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_windowProc));
    }

    private nint OnWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmGetMinMaxInfo)
        {
            double scale = GetDpiForWindow(hwnd) / 96.0;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            MinMaxInfo info = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = (int)Math.Round(MinWidthDip * scale);
            info.MinTrackSize.Y = (int)Math.Round(MinHeightDip * scale);
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
        }

        return CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint newValue);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint previous, nint hwnd, uint message, nint wParam, nint lParam);
    /// <summary>把当前语言的导航文案写进 NavigationView。</summary>
    private void ApplyNavText()
    {
        CompareNavItem.Content = AppText.Current.NavCompare;
        SettingsNavItem.Content = AppText.Current.NavSettings;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ApplyNavText();

    /// <summary>
    /// 按屏幕工作区与当前 DPI 计算窗口尺寸：内容按设计尺寸（1280×880 逻辑像素）放大，
    /// 再限制在屏幕工作区的 92% 以内，并居中显示 —— 各种分辨率下都能完整看到界面。
    /// </summary>
    private void SizeToFitDisplay()
    {
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);

        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        int maxWidth = Math.Max(400, (int)(area.WorkArea.Width * 0.92));
        int maxHeight = Math.Max(300, (int)(area.WorkArea.Height * 0.92));
        int width = Math.Min((int)Math.Round(1280 * scale), maxWidth);
        int height = Math.Min((int)Math.Round(880 * scale), maxHeight);

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            area.WorkArea.X + ((area.WorkArea.Width - width) / 2),
            area.WorkArea.Y + ((area.WorkArea.Height - height) / 2)));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private const string PageArgumentPrefix = "--page=";

    /// <summary>
    /// 选择启动页：默认「比较」，可用 <c>--page=settings</c> 指定设置页、
    /// <c>--page=result</c> 直接打开「比较结果」页，便于单独验证某个页面的加载。
    /// </summary>
    private void SelectInitialPage()
    {
        string? argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(static a => a.StartsWith(PageArgumentPrefix, StringComparison.OrdinalIgnoreCase));

        string page = argument is null
            ? string.Empty
            : argument[PageArgumentPrefix.Length..].Trim().ToLowerInvariant();

        // 比较结果页不在导航菜单里（它由比较页跳转进入），单独处理。
        if (page == "result")
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(
                typeof(CompareResultPage),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
            return;
        }

        int index = page switch
        {
            "settings" => 1,
            _ => 0,
        };

        NavView.SelectedItem = NavView.MenuItems[index];

        // 某些时序下设置 SelectedItem 不会立刻触发导航，这里兜底一次。
        if (ContentFrame.CurrentSourcePageType != PageTypeFor(index))
        {
            ContentFrame.Navigate(PageTypeFor(index), null, new EntranceNavigationTransitionInfo());
        }
    }

    private static Type PageTypeFor(int index) => index switch
    {
        1 => typeof(SettingsPage),
        _ => typeof(ComparePage),
    };

    /// <summary>
    /// 供其它页面调用：切换到「设置」页（同时同步 NavigationView 的选中项）。
    /// 例如比较结果页里的「执行策略」链接。
    /// </summary>
    public void NavigateToSettings()
    {
        NavView.SelectedItem = NavView.MenuItems[1];

        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
        }
    }

    private void ConfigureTitleBar()
    {
        AppWindowTitleBar titleBar = AppWindow.TitleBar;

        // 标题栏按钮色彩由外观设置决定（含明暗主题）。
        AppearanceService.Apply(this, SettingsService.Current.Value);

        // 让标题栏内容避开系统提供的左侧 / 右侧（最小化、最大化、关闭）留白。
        LeftPaddingColumn.Width = new GridLength(titleBar.LeftInset);
        RightPaddingColumn.Width = new GridLength(titleBar.RightInset);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        Type pageType = tag switch
        {
            "settings" => typeof(SettingsPage),
            _ => typeof(ComparePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        }
    }
}
