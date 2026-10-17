using Microsoft.UI.Xaml.Media;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>日志列表中的一行。</summary>
public sealed class LogRowViewModel
{
    public LogRowViewModel(JournalEntry entry)
    {
        Entry = entry;
    }

    public JournalEntry Entry { get; }

    public string TimeText => Entry.TimeText;

    public string KindText => Entry.KindText;

    public string DetailText => Entry.DetailText;

    public string ResultText => Entry.ResultText;

    /// <summary>成功用绿色、失败用红色（跟随主题）。</summary>
    public Brush ResultBrush => Entry.Success
        ? Controls.StatusPresentation.Brush(Core.Models.CompareStatus.LeftOnly)
        : Controls.StatusPresentation.Brush(Core.Models.CompareStatus.Delete);
}
