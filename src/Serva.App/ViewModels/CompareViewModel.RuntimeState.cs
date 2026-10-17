using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.Core.Models;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 运行态、进度与提示条（partial）。</summary>
public sealed partial class CompareViewModel
{
    private double _runningPercent;
    private int _progressCompleted;
    private int _progressGrandTotal;
    private long _progressBytes;
    private long _progressGrandBytes;
    private double _progressBytesPerSecond;
    private TimeSpan? _progressRemaining;
    private RunTextState _runState = RunTextState.Idle;
    private string _runCurrentRelativePath = string.Empty;
    private string _runningCompletedText = "0";
    private string _runningQueuedText = "0";
    private string _runningSkippedText = "0";
    private string _runningFailedText = "0";
    private InfoBarSeverity _bannerSeverity = InfoBarSeverity.Informational;

    public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IdleVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanAddMapping));
                RaiseCommands();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanAddMapping));
                OnPropertyChanged(nameof(RunningVisibility));
                OnPropertyChanged(nameof(IdleVisibility));
                RaiseCommands();
            }
        }
    }

    public bool CanScan => !IsBusy && !IsRunning && Mappings.Count > 0 && _selectedDevice is not null;

    public bool CanStart => !IsBusy && !IsRunning && _plan is { SelectedCount: > 0 };

    public string? BannerMessage
    {
        get => _bannerMessage;
        private set
        {
            if (SetProperty(ref _bannerMessage, value))
            {
                OnPropertyChanged(nameof(HasBanner));
            }
        }
    }

    public bool HasBanner => !string.IsNullOrEmpty(BannerMessage);

    public InfoBarSeverity BannerSeverity
    {
        get => _bannerSeverity;
        private set => SetProperty(ref _bannerSeverity, value);
    }

    /// <summary>表头「全选」。</summary>
    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (!SetProperty(ref _isAllSelected, value))
            {
                return;
            }

            foreach (CompareRowViewModel row in Rows)
            {
                row.IsSelected = value;
            }

            UpdateDerivedSummaries();
        }
    }

    /// <summary>结果列表只显示有差异的项目（隐藏两端完全一致的行）。</summary>
    public bool ShowOnlyDifferences
    {
        get => _showOnlyDifferences;
        set
        {
            if (SetProperty(ref _showOnlyDifferences, value))
            {
                ApplyRows();
            }
        }
    }

    /// <summary>过滤后列表是否还有行（没有时显示空态提示）。</summary>
    public bool HasVisibleRows => Rows.Count > 0;

    public Visibility RowsEmptyVisibility => Rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>列表说明：「共 36 项」或「共 36 项 · 显示 12 项」。</summary>
    public string RowCaptionText => Rows.Count == _allRows.Count
        ? Text.F("{0} item(s)", "共 {0} 项", _allRows.Count)
        : Text.F("{0} item(s) · showing {1}", "共 {0} 项 · 显示 {1} 项", _allRows.Count, Rows.Count);

    public double RunningPercent
    {
        get => _runningPercent;
        private set => SetProperty(ref _runningPercent, value);
    }

    /// <summary>「12 / 36 files · 33%」式的进度计数（按当前语言生成）。</summary>
    public string RunningCountText => Text.F(
        "{0:N0} / {1:N0} files · {2:0}%",
        "{0:N0} / {1:N0} 个文件 · {2:0}%",
        _progressCompleted,
        _progressGrandTotal,
        RunningPercent);

    /// <summary>「1.2 GB / 4.3 GB · 12 MB/s · 预计剩余 01:23」式的进度说明。</summary>
    public string RunningDetailText
    {
        get
        {
            string speed = _progressBytesPerSecond > 1
                ? DeviceInfo.Format((long)_progressBytesPerSecond) + "/s"
                : "—";

            string remaining = _progressRemaining is { } left
                ? Text.F("About {0} left", "预计剩余 {0}", left.ToString(@"mm\:ss"))
                : Text.T("Time left —", "预计剩余 —");

            return $"{DeviceInfo.Format(_progressBytes)} / {DeviceInfo.Format(_progressGrandBytes)} · {speed} · {remaining}";
        }
    }

    /// <summary>当前正在处理的文件；未开始 / 没有具体文件时显示「准备中…」。</summary>
    public string RunningCurrentFile => _runState switch
    {
        RunTextState.Completed => Text.T("Completed", "已完成"),
        RunTextState.Running when _runCurrentRelativePath.Length > 0 => _runCurrentRelativePath,
        _ => Text.T("Preparing…", "准备中…"),
    };

    public string RunningCompletedText
    {
        get => _runningCompletedText;
        private set => SetProperty(ref _runningCompletedText, value);
    }

    public string RunningQueuedText
    {
        get => _runningQueuedText;
        private set => SetProperty(ref _runningQueuedText, value);
    }

    public string RunningSkippedText
    {
        get => _runningSkippedText;
        private set => SetProperty(ref _runningSkippedText, value);
    }

    public string RunningFailedText
    {
        get => _runningFailedText;
        private set => SetProperty(ref _runningFailedText, value);
    }
}
