using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「日志」页 —— 读取与撤销（partial）。</summary>
public sealed partial class LogViewModel
{
    /// <summary>重新读取日志：旧日志直接删除，只保留最新一次运行。</summary>
    public void Reload()
    {
        // 日志内容重新加载后，上一条「已清除 / 已撤销」之类的提示就不作数了。
        DismissStatus();

        string? previous = _selectedJournal?.FilePath;

        Journal.DeleteOldJournals();

        // 旧日志已删除，这里通常只剩最新的一份；上一份被删掉时自动落到新的一份上。
        IReadOnlyList<JournalSummary> journals = Journal.ListSummaries();
        SelectedJournal =
            journals.FirstOrDefault(j => string.Equals(j.FilePath, previous, StringComparison.OrdinalIgnoreCase))
            ?? journals.FirstOrDefault();

        UpdateJournalAvailability(journals.Count > 0);
    }

    /// <summary>
    /// 清除全部日志。清除之后没有任何日志可用于「撤销本次运行」，
    /// 因此页面会先弹确认（清除日志后不能撤销上次操作）。
    /// </summary>
    public void Clear()
    {
        int deleted = Journal.DeleteAllJournals();

        // 清空选中项 + 列表 + 「上次同步」，整块日志区域回到空态。
        SelectedJournal = null;
        _allRows = [];
        Rows.Clear();
        LastSyncCaption = NoSyncCaption;
        UpdateJournalAvailability(hasJournals: false);

        SetStatus(() => deleted > 0
            ? AppText.Current.F("Cleared {0} log file(s).", "已清除 {0} 份日志。", deleted)
            : AppText.Current.T("No logs to clear.", "没有可清除的日志。"));
    }

    /// <summary>同步「清除日志」按钮的可用状态（有没有日志可清）。</summary>
    private void UpdateJournalAvailability(bool hasJournals)
    {
        if (_hasJournals == hasJournals)
        {
            return;
        }

        _hasJournals = hasJournals;
        OnPropertyChanged(nameof(CanClearLog));
        ClearCommand.RaiseCanExecuteChanged();
    }

    /// <summary>撤销当前日志所记录的那一次运行。</summary>
    private async Task UndoAsync()
    {
        JournalSummary? summary = _selectedJournal;
        if (summary is null || !CanUndo)
        {
            return;
        }

        IsBusy = true;

        try
        {
            string path = summary.FilePath;

            (UndoPlan plan, UndoResult result) = await Task.Run(() =>
            {
                UndoPlan built = JournalUndoer.BuildPlan(Journal.Read(path));
                if (!built.CanUndo)
                {
                    return (built, new UndoResult());
                }

                // 撤销动作写入一个新的日志文件，便于追溯。
                return (built, JournalUndoer.Execute(built, new Journal()));
            });

            // 先刷新日志内容（这一步会清掉旧提示），再显示本次撤销的结果。
            Reload();

            if (!plan.CanUndo)
            {
                string blocked = plan.Summary;
                SetStatus(() => blocked, isError: true);
                return;
            }

            SetStatus(
                () => AppText.Current.F(
                    "Undid {0} item(s): {1} deleted · {2} restored · {3} renamed",
                    "已撤销 {0} 项：删除 {1} · 还原 {2} · 改名 {3}",
                    result.Total,
                    result.Deleted,
                    result.Restored,
                    result.Renamed)
                    + (result.Skipped > 0
                        ? AppText.Current.F(" · {0} skipped", " · 跳过 {0}", result.Skipped)
                        : string.Empty)
                    + (result.Failed > 0
                        ? AppText.Current.F(" · {0} failed", " · 失败 {0}", result.Failed)
                        : string.Empty),
                isError: result.AnyFailure);

            // 撤销之后比较页的结果不再可信。
            App.GetCompareViewModel().InvalidateResults(() => AppText.Current.T(
                "Undo executed. Click \"Scan and compare\" again.",
                "已执行撤销，请重新点击「扫描并比较」。"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadEntries()
    {
        _allRows = _selectedJournal is null
            ? []
            : [.. Journal.Read(_selectedJournal.FilePath).Select(static e => new LogRowViewModel(e))];

        ApplyFilter();
        LastSyncCaption = BuildLastSyncCaption();

        // 行按时间顺序排列，通知页面滚到最新一条。
        RowsReloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 「处理方式 外置设备名称 盘符 年/月/日 时:分:秒」式的上次同步信息：
    /// 数据来自运行抬头（<see cref="JournalOperationKind.Move"/>）—— 它的
    /// <c>TargetPath</c> 是设备上的文件夹，<c>Message</c> 是处理方式。
    /// </summary>
    private string BuildLastSyncCaption()
    {
        JournalEntry? header = _allRows
            .FirstOrDefault(static r => r.Entry.Kind == JournalOperationKind.Move)?.Entry;

        if (header is null)
        {
            return NoSyncCaption;
        }

        string root = Path.GetPathRoot(header.TargetPath) ?? string.Empty;
        string letter = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string label = LabelOf(header, letter);
        // 新日志存的是处理方式代码（按当前语言现算）；旧日志里是当时记下的文字，直接用。
        string mode = string.IsNullOrWhiteSpace(header.Message)
            ? AppText.Current.T("Merge", "合并")
            : RunHeaderCode.Normalize(header.Message) switch
            {
                RunHeaderCode.Merge => AppText.Current.T("Merge", "合并"),
                RunHeaderCode.OverwriteLocalToDevice => AppText.Current.T("Overwrite", "覆盖"),
                RunHeaderCode.OverwriteDeviceToLocal => AppText.Current.T("Overwrite local disk", "覆盖本机硬盘"),
                _ => header.Message.Trim(),
            };
        string stamp = header.TimestampUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");

        return string.Join(' ', new[] { mode, label, letter, stamp }.Where(static part => part.Length > 0));
    }

    /// <summary>
    /// 外置设备名称：优先用日志里记下的卷标（设备没插也能显示）；
    /// 旧日志没记时按盘符现查当前接的设备。
    /// </summary>
    private static string LabelOf(JournalEntry header, string driveLetter)
    {
        if (!string.IsNullOrWhiteSpace(header.DeviceLabel))
        {
            return header.DeviceLabel.Trim();
        }

        return driveLetter.Length == 0
            ? string.Empty
            : VolumeInfo.GetDrives(DriveFilter.Removable)
                .FirstOrDefault(d => string.Equals(d.DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase))
                ?.VolumeLabel ?? string.Empty;
    }

    private void ApplyFilter()
    {
        string search = _searchText.Trim();
        IEnumerable<LogRowViewModel> query = _allRows;

        if (search.Length > 0)
        {
            // 只按文件名搜索，不匹配目录、操作与状态。
            query = query.Where(r => FileNameOf(r.Entry.RelativePath)
                .Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Rows.Clear();
        foreach (LogRowViewModel row in query)
        {
            Rows.Add(row);
        }
    }

    /// <summary>取相对路径里的文件名（目录条目回退为最后一段）。</summary>
    private static string FileNameOf(string relativePath)
    {
        string name = Path.GetFileName(relativePath.TrimEnd('\\', '/'));
        return name.Length == 0 ? relativePath : name;
    }

    private void SetStatus(Func<string> message, bool isError = false)
    {
        _statusFactory = message;
        StatusIsError = isError;
        StatusMessage = message();
    }
}
