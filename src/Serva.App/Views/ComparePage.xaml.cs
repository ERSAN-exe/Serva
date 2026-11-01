using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Models;
using Serva.App.ViewModels;

namespace Serva.App.Views;

public sealed partial class ComparePage : Page
{
    public ComparePage()
    {
        ViewModel = App.GetCompareViewModel();
        Log = ViewModel.Log;
        InitializeComponent();

        Log.RowsReloaded += OnLogRowsReloaded;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CompareViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    /// <summary>已并入本页的日志（与比较共用同一个视图模型实例）。</summary>
    public LogViewModel Log { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is { } window)
        {
            ViewModel.AttachWindow(window);
        }

        Log.Reload();
    }

    /// <summary>关闭「发现新版本」提示条：记住这个版本，之后不再重复提醒。</summary>
    private void UpdateNotice_CloseClick(InfoBar sender, object args) => ViewModel.DismissUpdateNotice();

    /// <summary>点选设备：重新读取这台设备的对应关系（对应关系跟着设备走）。</summary>
    private void Device_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DeviceInfo device)
        {
            ViewModel.SelectDevice(device);
        }
    }

    /// <summary>
    /// 键盘 / 触控选择设备时同步给视图模型。
    /// <para>
    /// 只处理「选中了某一台」：列表重建（重新扫描 / 插入拔出）时 ListView 会先把自己清空，
    /// 那不是用户的意图，忽略即可。选中项的单向绑定由视图模型负责，所以这里不回写 null。
    /// </para>
    /// </summary>
    private void Device_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is DeviceInfo device)
        {
            ViewModel.SelectDevice(device);
        }
    }

    /// <summary>关闭提示条：清掉视图模型里的句子，切换页面后不再出现。</summary>
    private void Banner_CloseClick(InfoBar sender, object args) => ViewModel.DismissBanner();

    /// <summary>关闭日志卡片的提示：同上。</summary>
    private void LogStatus_CloseClick(InfoBar sender, object args) => Log.DismissStatus();

    private void OnUnloaded(object sender, RoutedEventArgs e) => Log.RowsReloaded -= OnLogRowsReloaded;

    /// <summary>日志刷新后跳到最新一条（行按时间顺序排列，最后一条即最新）。</summary>
    private void OnLogRowsReloaded(object? sender, EventArgs e)
    {
        if (Log.Rows.Count > 0)
        {
            LogList.ScrollIntoView(Log.Rows[^1]);
        }
    }

    /// <summary>撤销会删除 / 还原文件，先让用户确认。</summary>
    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!Log.CanUndo)
        {
            return;
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = AppText.Current.UndoDialogBody,
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = AppText.Current.UndoDialogTitle,
            Content = content,
            PrimaryButtonText = AppText.Current.UndoButton,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Log.UndoCommand.Execute(null);
        }
    }

    /// <summary>清除日志会一并失去「撤销本次运行」的能力，先让用户确认。</summary>
    private async void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (!Log.CanClearLog)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = StatusPresentation.WarningTitle(AppText.Current.ClearLogDialogTitle),
            Content = new TextBlock
            {
                Text = AppText.Current.ClearLogDialogBody,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = AppText.Current.ClearButton,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Log.ClearCommand.Execute(null);
        }
    }

    /// <summary>添加一条对应关系：先选设备上的文件夹，紧接着选与它对应的本机文件夹。</summary>
    private async void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        string? deviceRoot = ViewModel.SelectedDevice?.RootPath;
        if (string.IsNullOrEmpty(deviceRoot) || !ViewModel.CanAddMapping)
        {
            return;
        }

        // 1) 设备上的文件夹（浏览范围限定在该设备内部，无法误选本机硬盘）。
        var deviceDialog = new FolderBrowserDialog(FolderBrowserMode.SingleRoot, deviceRoot) { XamlRoot = XamlRoot };
        if (await deviceDialog.ShowAsync() != ContentDialogResult.Primary
            || deviceDialog.SelectedPath is not { } deviceFolder)
        {
            return;
        }

        // 2) 与它对应的本机文件夹（先选盘符，再进目录；设备所在的移动磁盘不出现在盘符列表里，避免把设备当成"本机盘"）。
        //    「允许同时选择两个移动设备」打开时，列表里还会出现**其它**可移动设备（这块目标设备始终排除）；
        //    关闭后只列本机硬盘。系统盘根目录与系统文件夹在对话框里无法选中。
        var localDialog = new FolderBrowserDialog(
            FolderBrowserMode.LocalDrives,
            excludedDrives: [deviceRoot],
            pairedFolder: deviceFolder,
            driveFilter: SettingsService.Current.Value.AllowTwoRemovableDevices
                ? DriveFilter.LocalDrives
                : DriveFilter.FixedDrives)
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
        };
        if (await localDialog.ShowAsync() != ContentDialogResult.Primary
            || localDialog.SelectedPath is not { } localFolder)
        {
            return;
        }

        string relative = Path.GetRelativePath(deviceRoot, deviceFolder);

        // 根目录警告放在两个文件夹都选完之后：设备根 + 本机盘根同时出现时要一次说清两块盘。
        if (!await ConfirmDriveRootsAsync(deviceRoot, deviceFolder, localFolder))
        {
            return;
        }

        ViewModel.TryAddMapping(localFolder, relative == "." ? string.Empty : relative);
    }

    /// <summary>
    /// 两侧选了磁盘根目录时先警告一次（整块盘都会参与同步，不只是某个文件夹）。
    /// <returns><c>true</c> = 继续添加；<c>false</c> = 用户取消。</returns>
    /// </summary>
    private async Task<bool> ConfirmDriveRootsAsync(string deviceRoot, string deviceFolder, string localFolder)
    {
        bool deviceIsRoot = IsDriveRoot(deviceFolder);
        bool localIsRoot = IsDriveRoot(localFolder);

        if (!deviceIsRoot && !localIsRoot)
        {
            return true;
        }

        (string title, string body) = (deviceIsRoot, localIsRoot) switch
        {
            (true, true) => (AppText.Current.RootFolderWarningTitle, AppText.Current.BothRootsWarningBody(deviceRoot, localFolder)),
            (true, false) => (AppText.Current.DeviceRootWarningTitle, AppText.Current.DeviceRootWarningBody(deviceRoot)),
            _ => (AppText.Current.RootFolderWarningTitle, AppText.Current.LocalRootWarningBody(localFolder)),
        };

        var warning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = StatusPresentation.WarningTitle(title),
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = AppText.Current.ContinueButton,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        return await warning.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>该路径是否就是某个盘符的根目录（<c>D:\</c>）。</summary>
    private static bool IsDriveRoot(string path)
    {
        string? root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root)
               && string.Equals(
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MappingViewModel mapping })
        {
            ViewModel.RemoveMappingCommand.Execute(mapping);
        }
    }

    /// <summary>扫描超过这个时长才弹出「正在比对文件」，避免小目录一闪而过。</summary>
    private static readonly TimeSpan ComparingDialogDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>扫描并比较；有结果时跳到「比较结果」页选择执行方式。</summary>
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanScan)
        {
            return;
        }

        Task scanning = ViewModel.ScanAndCompareAsync();
        ContentDialog? comparing = null;

        try
        {
            // 文件多时扫描要几十秒：等待一小会儿仍未结束就弹窗说明正在比对（窗口无按钮，完成后自动关闭）。
            if (await Task.WhenAny(scanning, Task.Delay(ComparingDialogDelay)) != scanning)
            {
                comparing = BuildComparingDialog();
                _ = comparing.ShowAsync();
            }

            await scanning;
        }
        finally
        {
            try
            {
                comparing?.Hide();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                // 弹窗还没真正显示（或已被 Esc 关掉）时 Hide 会失败：忽略即可。
            }
        }

        if (ViewModel.HasResults && Frame is { } frame)
        {
            // 从右侧滑入，明确「进入下一层」的方向感。
            var transition = new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight,
            };

            frame.Navigate(typeof(CompareResultPage), null, transition);
        }
    }

    /// <summary>「正在比对文件」弹窗：只有一个标题 + 转圈，扫描结束后由调用方关掉。</summary>
    private ContentDialog BuildComparingDialog() => new()
    {
        XamlRoot = XamlRoot,
        RequestedTheme = AppearanceService.CurrentElementTheme,
        Title = AppText.Current.ComparingFilesTitle,
        Content = new ProgressRing
        {
            IsActive = true,
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };
}
