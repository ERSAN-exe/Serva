using System.Collections.ObjectModel;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>
/// 「比较」页的视图模型。
/// <para>
/// 核心模型是若干条「本机文件夹 ↔ 移动设备上的文件夹」<strong>对应关系</strong>：
/// 每条关系独立扫描 / 比较 / 生成计划，执行时按关系依次进行，并共用同一个日志文件。
/// </para>
/// </summary>
public sealed partial class CompareViewModel : ObservableObject, IDisposable
{
    private readonly SnapshotStore _snapshotStore = new();
    private readonly CompareEngine _compareEngine = new();
    private readonly PlanBuilder _planBuilder = new();
    private readonly SyncEngine _syncEngine = new();

    private readonly List<MappingState> _states = [];
    private readonly List<CompareRowViewModel> _allRows = [];

    private SyncPlan? _plan;
    private DeviceWatcher? _deviceWatcher;
    private CancellationTokenSource? _runCts;
    private string _patternSignature = string.Empty;

    private DeviceInfo? _selectedDevice;
    private string _scanSummary = string.Empty;
    private Func<string> _scanSummaryFactory = static () => string.Empty;
    private string? _bannerMessage;
    private Func<string>? _bannerFactory;
    private bool _allowLocalDrives;
    private bool _isBusy;
    private bool _isRunning;
    private bool _isAllSelected = true;
    private bool _showOnlyDifferences = true;

    /// <summary>与设置页<strong>共享</strong>的同一份设置实例。</summary>
    private static AppSettings _settings => SettingsService.Current.Value;

    /// <summary>当前语言的文案表：本页所有面向用户的句子都从这里取。</summary>
    private static AppText Text => AppText.Current;

    public CompareViewModel()
    {
        Mappings = [];
        Devices = [];
        Rows = [];

        // 同步方向不再由页面上的模式单选决定，而是每次扫描后在结果弹窗里选择处理方式。
        _patternSignature = PatternSignature();

        RemoveMappingCommand = new RelayCommand<MappingViewModel>(RemoveMapping);
        RefreshDevicesCommand = new RelayCommand(() => RefreshDevices(userInitiated: true));
        StartCommand = new AsyncRelayCommand(StartExecutionAsync, () => CanStart);
        CancelCommand = new RelayCommand(CancelExecution, () => IsRunning);

        SettingsService.Current.Changed += OnSettingsChanged;
        UpdateService.Current.Changed += OnUpdateChanged;

        _allowLocalDrives = _settings.AllowLocalDrives;

        ResetResults(() => Text.T(
            "Add a mapping, then click \"Scan and compare\".",
            "请添加对应关系，然后点击「扫描并比较」。"));

        RefreshDevices();
    }

    public ObservableCollection<MappingViewModel> Mappings { get; }

    public ObservableCollection<DeviceInfo> Devices { get; }

    public ObservableCollection<CompareRowViewModel> Rows { get; }

    /// <summary>日志（已并入本页）：每次运行的 JSONL 记录、导出与撤销。</summary>
    public LogViewModel Log { get; } = new();

    public RelayCommand<MappingViewModel> RemoveMappingCommand { get; }

    /// <summary>立即重新扫描移动设备列表。</summary>
    public RelayCommand RefreshDevicesCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>一条对应关系的扫描 / 比较 / 计划状态。</summary>
    private sealed class MappingState
    {
        public required MappingViewModel Mapping { get; init; }

        public ScanResult? Left { get; set; }

        public ScanResult? Right { get; set; }

        public SyncPlan? Plan { get; set; }
    }

    private void RaiseCommands()
    {
        RemoveMappingCommand.RaiseCanExecuteChanged();
        RefreshDevicesCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void SaveSettings() => SettingsService.Current.Save();

    /// <summary>排除 / 仅包含规则的指纹，用于判断设置页的改动是否需要重新扫描。</summary>
    private string PatternSignature() => string.Join(
        "|",
        _settings.Options.ExcludePatterns.Select(static p => "+" + p)
            .Concat(_settings.Options.IncludePatterns.Select(static p => "-" + p)));

    /// <summary>设置页保存后的响应：规则变了要求重扫，只改执行策略则重新生成计划即可。</summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // 「允许选择本机硬盘」变了：设备列表要重新枚举（可能多出 / 少掉本机数据盘）。
        if (_allowLocalDrives != _settings.AllowLocalDrives)
        {
            _allowLocalDrives = _settings.AllowLocalDrives;
            RefreshDevices();
        }

        string signature = PatternSignature();

        if (!string.Equals(signature, _patternSignature, StringComparison.Ordinal))
        {
            _patternSignature = signature;
            ResetResults(() => Text.T(
                "Filter rules changed. Click \"Scan and compare\" again.",
                "筛选规则已更新，请重新点击「扫描并比较」。"));
            return;
        }

        if (_states.Count > 0 && !IsRunning)
        {
            Replan();
        }
    }

    /// <summary>
    /// 语言切换后的收尾：界面文案由 <c>x:Bind</c> 自动刷新，
    /// 但「已经算好的句子」（扫描摘要、提示条、计划说明、进度说明）需要重新生成。
    /// </summary>
    internal void OnLanguageChanged()
    {
        ScanSummary = _scanSummaryFactory();

        // 只有「当前仍然显示着」的提示条才需要换语言；关掉过的不要借机复活。
        if (_bannerFactory is not null && BannerMessage is not null)
        {
            BannerMessage = _bannerFactory();
        }

        // 版本更新提示条里的句子也要跟着换语言。
        OnPropertyChanged(nameof(UpdateNoticeText));

        if (_isRunning)
        {
            RefreshRunningTexts();
            return;
        }

        // 日志行（操作 / 结果 / 上次同步）也按语言重新生成。
        Log.OnLanguageChanged();

        if (_states.Count > 0)
        {
            Replan();
            return;
        }

        UpdateDerivedSummaries();
    }
}
