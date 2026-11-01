using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serva.App.Controls;
using Serva.App.Localization;
using Serva.App.Services;
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

        // 每次进入结果页，筛选都回到「显示所有」。
        ViewModel.ResetFilter();
    }

    public CompareViewModel ViewModel { get; }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>开始执行：先确认执行方式与文件数，随即回到比较页查看进度与日志。</summary>
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanStart)
        {
            return;
        }

        // 执行前确认：当前处理方式 + 逐个动作的文件数（整块明细用小号浅灰）。
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = AppText.Current.F(
                "Run with \"{0}\"?",
                "是否以「{0}」执行操作？",
                ViewModel.ActionDisplayName),
            TextWrapping = TextWrapping.Wrap,
        });

        content.Children.Add(StatusPresentation.MutedNote(ViewModel.RunSummaryTotalText));

        var details = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        foreach (string line in ViewModel.RunSummaryLines())
        {
            details.Children.Add(StatusPresentation.MutedNote(line));
        }

        content.Children.Add(details);

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = AppearanceService.CurrentElementTheme,
            Title = AppText.Current.T("Confirm before running", "执行前确认"),
            Content = content,
            PrimaryButtonText = AppText.Current.StartRun,
            CloseButtonText = AppText.Current.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // 保留覆盖副本的空间不够：红色标题弹窗确认是否继续（继续则被覆盖的文件无法撤销）。
        if (ViewModel.BackupSpaceWarning() is { } warning)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = AppearanceService.CurrentElementTheme,
                Title = StatusPresentation.WarningTitle(AppText.Current.BackupSpaceDialogTitle),
                Content = new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = AppText.Current.ContinueButton,
                CloseButtonText = AppText.Current.Cancel,
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        // 执行期间用弹窗显示进度与统计（原来是「比较」页上的「执行中」卡片）；
        // 结束后自动关闭，再回到比较页查看结果与日志。
        var progress = new RunProgressDialog(ViewModel) { XamlRoot = XamlRoot };
        progress.Opened += (_, _) => StartRun(progress);

        await progress.ShowAsync();
        GoBack();
    }

    /// <summary>弹窗显示出来之后才真正开始执行（避免执行瞬间结束、弹窗却还没打开）。</summary>
    private void StartRun(RunProgressDialog dialog)
    {
        ViewModel.StartCommand.Execute(null);

        // 没能开始（例如空间预检未通过）：别把空弹窗晾在那里。
        if (!ViewModel.IsRunning)
        {
            dialog.Hide();
        }
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
}
