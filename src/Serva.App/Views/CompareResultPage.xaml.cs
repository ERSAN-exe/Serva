using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serva.App.Localization;
using Serva.App.ViewModels;

namespace Serva.App.Views;

/// <summary>
/// 「比较结果」页：选择执行方式（合并 / 覆盖某一侧内容）、查看逐项比较结果并开始执行。
/// <para>由比较页在扫描完成后跳转进入（原来是一个独立弹窗）。</para>
/// </summary>
public sealed partial class CompareResultPage : Page
{
    public CompareResultPage()
    {
        ViewModel = App.GetCompareViewModel();
        InitializeComponent();
    }

    public CompareViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>开始执行：随即回到比较页查看进度与日志。</summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanStart)
        {
            return;
        }

        ViewModel.StartCommand.Execute(null);
        GoBack();
    }

    private void GoBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        Frame.Navigate(typeof(ComparePage));
    }

    /// <summary>「执行策略」链接：跳到设置页面，让用户查看/修改执行策略。</summary>
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateToSettings();
}
