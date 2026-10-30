using Microsoft.UI.Xaml;
using Serva.App.Services;
using Serva.App.ViewModels;
using Serva.Core.Services;

namespace Serva.App;

/// <summary>Serva 同步 —— 应用入口。</summary>
public partial class App : Application
{
    private static CompareViewModel? _compareViewModel;

    public App()
    {

        // 界面语言与明暗主题都必须在创建任何界面之前应用，否则 WinUI 会拒绝修改。
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        LanguageService.ApplyAtStartup(SettingsService.Current.Value);
        AppearanceService.ApplyAtStartup(SettingsService.Current.Value);
    }

    /// <summary>主窗口（供文件 / 文件夹选择器、设备监听与页面切换取得 HWND）。</summary>
    public static MainWindow? MainWindow { get; internal set; }

    /// <summary>最近一次未处理异常的内容（排查用）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>「比较」页视图模型：应用级单例，切换页面后仍保留扫描结果。</summary>
    public static CompareViewModel GetCompareViewModel() => _compareViewModel ??= new CompareViewModel();

    /// <summary>
    /// 语言切换后的收尾：界面文案靠 <c>x:Bind</c> 自动刷新，
    /// 但「已经算好的句子」（计划说明、扫描摘要、日志行）需要重新生成。
    /// </summary>
    internal static void OnLanguageChanged()
    {
        // 版本更新提示的文字由 UpdateService 生成：先让它重新生成（会通知比较页 / 设置页）。
        UpdateService.Current.RefreshTexts();
        _compareViewModel?.OnLanguageChanged();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        // 关闭软件时删掉本次运行保留的「覆盖前副本」（.ServaUndoTemp）——
        // 撤销只在本次运行内有效。
        MainWindow.Closed += (_, _) => OverwriteBackup.DeleteAll();

        MainWindow.Activate();

        // 启动时在后台检查新版本：不阻塞界面，失败静默（24 小时内不重复请求）。
        _ = UpdateService.Current.CheckAsync(manual: false);
    }

    /// <summary>把未处理异常写到 <c>%LocalAppData%\Serva\last-error.log</c>，便于排查启动失败。</summary>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LastError = e.Message + Environment.NewLine + e.Exception;

        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Serva");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-error.log"), $"{DateTime.Now:O}{Environment.NewLine}{LastError}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}