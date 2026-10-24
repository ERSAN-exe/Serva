using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.App.ViewModels;
using Serva.Core.Models;

namespace Serva.App.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>关闭「移入回收文件夹」的取消流程里会回写开关，用它避免再次弹窗。</summary>
    private bool _suppressTrashToggle;

    /// <summary>页面加载完成前 IsOn 的变化来自绑定初始化，不当作「用户关闭」处理。</summary>
    private bool _trashToggleReady;

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _trashToggleReady = true;

        // 更新状态由应用级单例维护：页面在时订阅，离开时退订。
        UpdateService.Current.Changed += OnUpdatesChanged;
        ViewModel.RefreshUpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UpdateService.Current.Changed -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, EventArgs e) => ViewModel.RefreshUpdateState();

    /// <summary>关闭状态提示条：清掉视图模型里的句子，切换页面后不再出现。</summary>
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

    private void Status_CloseClick(InfoBar sender, object args) => ViewModel.DismissStatus();

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

    private void RemoveExcludeRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rule })
        {
            ViewModel.RemoveExcludeRuleCommand.Execute(rule);
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
