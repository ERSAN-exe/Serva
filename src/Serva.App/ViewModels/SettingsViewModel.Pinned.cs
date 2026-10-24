using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Serva.App.Localization;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 固定文件夹（会出现在「选择本机文件夹」对话框里的快捷入口）。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>已固定的本机文件夹（按添加顺序显示）。</summary>
    public ObservableCollection<string> PinnedFolders { get; } = [];

    /// <summary>还没有固定任何文件夹时显示一行提示。</summary>
    public Visibility PinnedEmptyVisibility =>
        PinnedFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>把一个文件夹固定到「选择本机文件夹」列表里（重复添加会被忽略）。</summary>
    public void AddPinnedFolder(string folder)
    {
        string normalized = folder.Trim();

        if (normalized.Length == 0
            || PinnedFolders.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _settings.Value.PinnedFolders.Add(normalized);
        RebuildPinnedFolders();
        Save();
        SetStatus(() => AppText.Current.F("Pinned: {0}", "已固定：{0}", normalized));
    }

    /// <summary>取消固定。</summary>
    public void RemovePinnedFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (_settings.Value.PinnedFolders.RemoveAll(
                p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        RebuildPinnedFolders();
        Save();
        SetStatus(() => AppText.Current.F("Unpinned: {0}", "已取消固定：{0}", folder));
    }

    /// <summary>把设置里的固定文件夹重新读入列表。</summary>
    private void RebuildPinnedFolders()
    {
        PinnedFolders.Clear();

        foreach (string folder in _settings.Value.PinnedFolders)
        {
            PinnedFolders.Add(folder);
        }

        OnPropertyChanged(nameof(PinnedEmptyVisibility));
    }
}