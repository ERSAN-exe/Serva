using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.App.ViewModels;
using Serva.Core.Models;
using Serva.Core.Services;
using Windows.System;

namespace Serva.App.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>关闭「移入回收文件夹」的取消流程里会回写开关，用它避免再次弹窗。</summary>
    private bool _suppressTrashToggle;

    /// <summary>页面加载完成前 IsOn 的变化来自绑定初始化，不当作「用户关闭」处理。</summary>
    private bool _trashToggleReady;

    /// <summary>同 <see cref="_suppressTrashToggle"/>：关闭「保留覆盖副本」的取消流程里回写开关。</summary>
    private bool _suppressBackupToggle;

    /// <summary>同 <see cref="_trashToggleReady"/>：绑定初始化阶段不弹窗。</summary>
    private bool _backupToggleReady;

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 换语言后「主题 / 语言」两个下拉框收起时显示的文案不会自己刷新（见 RefreshComboBoxTexts）。
        ViewModel.LocalizedTextsRefreshed += (_, _) => RefreshComboBoxTexts();

        // 首次显示时回到顶部：新增规则输入框拿到焦点会把整页滚到底部。
        SettingsScroll.Loaded += (_, _) => SettingsScroll.ChangeView(null, 0, null);
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    /// <summary>
    /// 语言切换后刷新两个下拉框「收起时显示」的文案。
    /// <para>
    /// WinUI 的 <c>ComboBox</c> 只在选择变化时重新取一次「已选项」的内容：
    /// 换语言后选项本身（<c>ComboBoxItem.Content</c>）已经换成新文案，展开的列表也是新的，
    /// 但收起时显示的那一行仍是旧文案。这里让选择先清空再选回同一项，逼它重新取一次
    /// （视图模型会忽略这个临时的 -1，见 <c>SettingsViewModel.ThemeIndex / LanguageIndex</c>）。
    /// </para>
    /// </summary>
    private void RefreshComboBoxTexts()
    {
        RefreshComboBoxText(ThemeCombo);
        RefreshComboBoxText(LanguageCombo);
    }

    private static void RefreshComboBoxText(ComboBox combo)
    {
        int index = combo.SelectedIndex;

        if (index < 0)
        {
            return;
        }

        combo.SelectedIndex = -1;
        combo.SelectedIndex = index;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _trashToggleReady = true;
        _backupToggleReady = true;

        // 更新状态由应用级单例维护：页面在时订阅，离开时退订。
        UpdateService.Current.Changed += OnUpdatesChanged;
        ViewModel.RefreshUpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UpdateService.Current.Changed -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, EventArgs e) => ViewModel.RefreshUpdateState();

    /// <summary>防止连点同时弹出两个对话框（WinUI 只允许一个 ContentDialog）。</summary>
    private bool _pickingPinnedFolder;

    /// <summary>固定一个本机文件夹：选好后会作为快捷入口出现在「选择本机文件夹」对话框里。</summary>
    private async void AddPinnedFolder_Click(object sender, RoutedEventArgs e)
    {
        // 防止连点同时弹出两个对话框（WinUI 只允许一个 ContentDialog）。
        if (_pickingPinnedFolder)
        {
            return;
        }

        _pickingPinnedFolder = true;

        try
        {
            // 固定文件夹时只列本机硬盘，也不重复列出已经固定过的文件夹。
            var dialog = new FolderBrowserDialog(
                FolderBrowserMode.LocalDrives,
                showPinnedFolders: false,
                driveFilter: DriveFilter.FixedDrives)
            {
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedPath is { } folder)
            {
                ViewModel.AddPinnedFolder(folder);
            }
        }
        finally
        {
            _pickingPinnedFolder = false;
        }
    }

    /// <summary>取消固定。</summary>
    private void RemovePinnedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string folder })
        {
            ViewModel.RemovePinnedFolder(folder);
        }
    }

    /// <summary>
    /// 关闭「删除时移入回收文件夹」意味着删除不可找回，先警告一次；
    /// 用户选择取消时把开关拨回去。
    /// </summary>
    private async void UseTrashFolder_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_trashToggleReady || _suppressTrashToggle || sender is not ToggleSwitch toggle || toggle.IsOn)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = StatusPresentation.WarningTitle(AppText.Current.RecycleOffDialogTitle),
            Content = new TextBlock
            {
                Text = AppText.Current.RecycleOffDialogBody,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = AppText.Current.TurnOffButton,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            return;
        }

        // 取消：把开关拨回「开」，并保证设置值也是「开」。
        _suppressTrashToggle = true;
        toggle.IsOn = true;
        _suppressTrashToggle = false;
        ViewModel.UseTrashFolder = true;
    }

    /// <summary>
    /// 关闭「允许覆盖后保留临时副本用于撤销」意味着覆盖后无法还原，先弹红色确认框；
    /// 用户选择取消时把开关拨回去。
    /// </summary>
    private async void KeepOverwriteBackup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_backupToggleReady || _suppressBackupToggle || sender is not ToggleSwitch toggle || toggle.IsOn)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = StatusPresentation.WarningTitle(AppText.Current.BackupOffDialogTitle),
            Content = new TextBlock
            {
                Text = AppText.Current.BackupOffDialogBody,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = AppText.Current.TurnOffButton,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            return;
        }

        // 取消：把开关拨回「开」，并保证设置值也是「开」。
        _suppressBackupToggle = true;
        toggle.IsOn = true;
        _suppressBackupToggle = false;
        ViewModel.KeepOverwriteBackup = true;
    }

    /// <summary>
    /// 手动「检查更新」：只有用户主动点这个按钮时才弹窗提示；
    /// 启动时的检查只更新主页上方的提示条（不打扰）。
    /// </summary>
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await UpdateService.Current.CheckAsync(manual: true);

        UpdateService updates = UpdateService.Current;
        if (!updates.HasUpdate || updates.LatestVersion is not { Length: > 0 } version)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = AppText.Current.F("Update available: {0}", "发现新版本 {0}", version),
            Content = new TextBlock
            {
                Text = AppText.Current.F(
                    "A newer version is available (current version: {0}). Open the download page?",
                    "有新版本可用（当前版本 {0}）。是否打开下载页面？",
                    AppVersion.Current),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = AppText.Current.OpenRelease,
            CloseButtonText = AppText.Current.LaterNotNow,
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Launcher.LaunchUriAsync(UpdateService.ReleasePageUri);
        }
    }

    private void RemoveExcludeRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ExcludeRuleViewModel rule })
        {
            ViewModel.RemoveExcludeRuleCommand.Execute(rule.Pattern);
        }
    }

    private void RemoveIncludeRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rule })
        {
            ViewModel.RemoveIncludeRuleCommand.Execute(rule);
        }
    }
}
