using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>
/// 「日志」视图模型：查看最新一次运行的 JSONL 日志、按文件名搜索与撤销本次运行。
/// </summary>
public sealed partial class LogViewModel : ObservableObject
{
    private JournalSummary? _selectedJournal;
    private IReadOnlyList<LogRowViewModel> _allRows = [];
    private string _searchText = string.Empty;
    private string? _statusMessage;
    private Func<string>? _statusFactory;
    private string _lastSyncCaption = NoSyncCaption;
    private bool _statusIsError;
    private bool _isBusy;
    private bool _hasJournals;

    /// <summary>没有任何运行记录时的占位文案（按当前语言生成）。</summary>
    private static string NoSyncCaption => AppText.Current.T("No sync record yet", "尚无同步记录");

    public LogViewModel()
    {
        Rows = [];

        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndo);
        ClearCommand = new RelayCommand(Clear, () => CanClearLog);

        Reload();
    }

    /// <summary>日志行已重新加载：页面据此把列表滚到最新一条。</summary>
    public event EventHandler? RowsReloaded;

    public ObservableCollection<LogRowViewModel> Rows { get; }

    public AsyncRelayCommand UndoCommand { get; }

    /// <summary>清除全部日志（调用方先弹出确认：清除后不能撤销上次操作）。</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>当前查看的日志文件。</summary>
    public JournalSummary? SelectedJournal
    {
        get => _selectedJournal;
        set
        {
            if (!SetProperty(ref _selectedJournal, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(UndoCaption));
            UndoCommand.RaiseCanExecuteChanged();
            LoadEntries();
        }
    }

    /// <summary>搜索关键词：只按文件名匹配。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// 上次同步信息：「处理方式 + 外置设备名称 + 盘符 + 年/月/日 时:分:秒」，
    /// 例如 <c>合并 0123 E: 2026/10/12 12:34:56</c>。
    /// </summary>
    public string LastSyncCaption
    {
        get => _lastSyncCaption;
        private set => SetProperty(ref _lastSyncCaption, value);
    }

    public bool CanUndo => _selectedJournal?.UndoableCount > 0 && !IsBusy;

    /// <summary>有日志可清、且当前不忙时，「清除日志」才可用。</summary>
    public bool CanClearLog => _hasJournals && !IsBusy;

    public string UndoCaption => _selectedJournal is { UndoableCount: > 0 } summary
        ? AppText.Current.F("Undo this run ({0})", "撤销本次运行（{0} 项）", summary.UndoableCount)
        : AppText.Current.T("Undo this run", "撤销本次运行");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanClearLog));
                UndoCommand.RaiseCanExecuteChanged();
                ClearCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set
        {
            if (SetProperty(ref _statusIsError, value))
            {
                OnPropertyChanged(nameof(StatusSeverity));
            }
        }
    }

    /// <summary>语言切换后：日志行、「上次同步」信息与状态提示都按新语言重新生成。</summary>
    internal void OnLanguageChanged()
    {
        LoadEntries();

        if (_statusFactory is not null)
        {
            StatusMessage = _statusFactory();
        }

        OnPropertyChanged(nameof(UndoCaption));
    }

    public InfoBarSeverity StatusSeverity =>
        StatusIsError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
}
