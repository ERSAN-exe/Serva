using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.App.ViewModels;

namespace Serva.App.Views;

/// <summary>
/// 「正在执行」弹窗：执行期间显示整体进度、当前文件与四项统计
/// （原「比较」页上的「执行中」卡片搬到这里），执行结束自动关闭。
/// <para>
/// 运行中点「取消执行」或按 Esc 关闭 = 中止本次执行，不会留下一个看不见的后台执行。
/// </para>
/// </summary>
public sealed partial class RunProgressDialog : ContentDialog
{
    public RunProgressDialog(CompareViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // 对话框挂在弹出层，主题需要显式指定（否则暗色模式下会弹亮色）。
        RequestedTheme = AppearanceService.CurrentElementTheme;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public CompareViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    /// <summary>执行结束后自动关掉弹窗（调用方接着返回比较页看结果与日志）。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompareViewModel.IsRunning) && !ViewModel.IsRunning)
        {
            TryHide();
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // 还在跑就被关掉：等同于「取消执行」。
        if (ViewModel.IsRunning)
        {
            ViewModel.CancelCommand.Execute(null);
        }
    }

    /// <summary>关掉弹窗；还没真正显示出来时（执行瞬间就结束）忽略失败。</summary>
    private void TryHide()
    {
        try
        {
            Hide();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
        }
    }
}
