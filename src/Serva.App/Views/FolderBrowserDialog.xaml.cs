using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.Views;

/// <summary>浏览模式。</summary>
public enum FolderBrowserMode
{
    /// <summary>以一个根目录为边界（用于移动设备内部，无法越出该设备）。</summary>
    SingleRoot,

    /// <summary>先选择本机盘符，再进入盘符内的目录。</summary>
    LocalDrives,
}

/// <summary>列表中的一项：驱动器的根路径、用户文件夹或一个子目录。</summary>
public sealed record FolderEntry(
    string FullPath,
    string Name,
    bool IsDrive,
    bool IsPinned = false,
    bool IsUserFolder = false,
    string? CustomGlyph = null)
{
    /// <summary>
    /// 列表项图标：自定义图标优先（桌面 / 文档 / 图片 / 音乐 / 视频 → 各自的图标），
    /// 其次图钉=固定文件夹，用户文件夹=用户图标，盘符=硬盘，其余=文件夹。
    /// </summary>
    public string Glyph => CustomGlyph
        ?? (IsPinned ? "\uE840" : IsUserFolder ? "\uE77B" : IsDrive ? "\uEDA2" : "\uE8B7");
}

/// <summary>
/// 软件内置的文件夹浏览对话框（不使用系统资源管理器）。
/// </summary>
public sealed partial class FolderBrowserDialog : ContentDialog
{
    private readonly ObservableCollection<FolderEntry> _entries = [];

    /// <summary>不出现在盘符列表里的盘符根（本机模式下来自「当前选中的移动设备」）。</summary>
    private readonly IReadOnlyList<string> _excludedDrives;
    /// <summary>是否在盘符页列出「设置里固定的文件夹」。</summary>
    private readonly bool _showPinnedFolders;

    /// <summary>盘符页列出哪些驱动器。</summary>
    private readonly DriveFilter _driveFilter;
    private readonly FolderBrowserMode _mode;
    private readonly string _root;
    private string? _current;

    public FolderBrowserDialog(
        FolderBrowserMode mode,
        string? root = null,
        IEnumerable<string>? excludedDrives = null,
        bool showPinnedFolders = true,
        DriveFilter driveFilter = DriveFilter.LocalDrives,
        string? pairedFolder = null)
    {
        InitializeComponent();

        // 对话框挂在弹出层，主题需要显式指定（否则暗色模式下会弹亮色）。
        RequestedTheme = AppearanceService.CurrentElementTheme;

        _mode = mode;
        _root = string.IsNullOrWhiteSpace(root) ? string.Empty : Normalize(root);
        _current = mode == FolderBrowserMode.SingleRoot && _root.Length > 0 ? _root : null;
        _excludedDrives = [.. (excludedDrives ?? []).Select(NormalizeDrive).Where(static d => d.Length > 0)];
        _showPinnedFolders = showPinnedFolders;
        _driveFilter = driveFilter;

        Title = mode == FolderBrowserMode.SingleRoot
            ? AppText.Current.ChooseDeviceFolder
            : AppText.Current.ChooseLocalFolder;

        // 本机对话框：上方先列出「刚在设备上选好的文件夹」，中间用连接图标指向下面的本机路径。
        if (!string.IsNullOrWhiteSpace(pairedFolder))
        {
            PairedText.Text = Normalize(pairedFolder);
            PairedPanel.Visibility = Visibility.Visible;
            LinkIcon.Visibility = Visibility.Visible;
        }

        EntryList.ItemsSource = _entries;
        Load();
    }

    /// <summary>当前语言的界面文案（XAML 里用 <c>{x:Bind Text.Xxx, Mode=OneWay}</c>）。</summary>
    public AppText Text => AppText.Current;

    /// <summary>用户确认的文件夹绝对路径。</summary>
    public string? SelectedPath { get; private set; }

    private void Load()
    {
        _entries.Clear();

        if (_current is null)
        {
            LoadDrives();
            return;
        }

        CurrentLabel.Text = _mode == FolderBrowserMode.SingleRoot
            ? AppText.Current.DeviceFolderLabel
            : AppText.Current.LocalFolderLabel;

        // 当前文件夹的完整路径：单独一行、自动换行（以前挤在按钮行里被省略号截断）。
        CurrentText.Text = _current;

        UpButton.IsEnabled = true;

        // 只有本机模式在盘符内部才需要「返回盘符选择」（设备模式有上一级兜底，且无法越出设备）。
        BackToDrivesButton.Visibility = _mode == FolderBrowserMode.LocalDrives
            ? Visibility.Visible
            : Visibility.Collapsed;

        try
        {
            // 用户文件夹里：系统自带的 桌面 / 文档 / 下载 / 图片 / 音乐 / 视频 用各自的图标，并排在最前面。
            Dictionary<string, (string Glyph, int Rank)> special = IsUserProfileRoot(_current)
                ? BuildSpecialFolders()
                : [];

            var folders = new List<(FolderEntry Entry, int Rank)>();

            foreach (string directory in Directory.EnumerateDirectories(_current))
            {
                if (IsHiddenOrSystem(directory))
                {
                    continue;
                }

                string name = Path.GetFileName(directory);

                folders.Add(special.TryGetValue(Path.TrimEndingDirectorySeparator(directory), out var known)
                    ? (new FolderEntry(directory, name, false, CustomGlyph: known.Glyph), known.Rank)
                    : (new FolderEntry(directory, name, false), int.MaxValue));
            }

            // 特殊文件夹按固定顺序排在最前面，其余按名称排序。
            foreach ((FolderEntry entry, _) in folders
                         .OrderBy(static f => f.Rank)
                         .ThenBy(static f => f.Entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _entries.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无权限读取的子目录直接忽略。
        }

        SetSelection(_current);
    }

    private void LoadDrives()
    {
        // 盘符根排在最前面：选本机文件夹时绝大多数情况是从某个硬盘开始往下点。
        foreach (DeviceInfo drive in VolumeInfo.GetDrives(_driveFilter))
        {
            if (_excludedDrives.Contains(NormalizeDrive(drive.RootPath), StringComparer.Ordinal))
            {
                continue;
            }

            _entries.Add(new FolderEntry(drive.RootPath, drive.DisplayName, true));
        }

        // 用户文件夹（C:\Users\<用户名>）：紧跟盘符列表 —— 下载 / 文档 / 图片 / 桌面都在里面，
        // 点进去就能往下选，也能用「使用当前文件夹」直接把整个用户文件夹作为同步目录。
        string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userFolder) && Directory.Exists(userFolder))
        {
            _entries.Add(new FolderEntry(userFolder, AppText.Current.UserFolder, false, IsUserFolder: true));
        }

        // 设置里「固定」的本机文件夹：作为快捷入口排在最后，不挡在盘符前面。
        foreach (string folder in _showPinnedFolders ? SettingsService.Current.Value.PinnedFolders : [])
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            if (_excludedDrives.Contains(NormalizeDrive(folder), StringComparer.Ordinal))
            {
                continue;
            }

            _entries.Add(new FolderEntry(folder, PinnedName(folder), false, IsPinned: true));
        }

        CurrentLabel.Text = AppText.Current.LocalDrivesCaption;
        CurrentText.Text = AppText.Current.ChooseDrivePrompt;
        UpButton.IsEnabled = false;
        BackToDrivesButton.Visibility = Visibility.Collapsed;

        SetSelection(null);
    }

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FolderEntry entry })
        {
            _current = entry.FullPath;
            Load();
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        string? parent = GetParentDirectory(_current);

        if (parent is null)
        {
            // 已到盘符根：本地模式回到盘符列表，设备模式停在设备根。
            _current = _mode == FolderBrowserMode.LocalDrives ? null : _root;
        }
        else if (_mode == FolderBrowserMode.SingleRoot && parent.Length < _root.Length)
        {
            _current = _root;
        }
        else
        {
            _current = parent;
        }

        Load();
    }

    /// <summary>刷新：重新枚举当前目录 / 盘符列表（插拔移动硬盘后不用重开对话框）。</summary>
    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    /// <summary>固定文件夹在列表里的显示名：取最后一段；盘符根则显示盘符。</summary>
    private static string PinnedName(string folder)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return name.Length == 0 ? folder : name;
    }

    /// <summary>直接回到盘符列表（只在本机模式的盘符内部可用）。</summary>
    private void BackToDrives_Click(object sender, RoutedEventArgs e)
    {
        _current = null;
        Load();
    }

    private void SetSelection(string? path)
    {
        SelectedPath = path;
        IsPrimaryButtonEnabled = path is not null;
    }

    private static string? GetParentDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(parent) ? null : parent;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>把路径归一为「盘符根」（<c>C:\</c>），用于比较是否已在对应关系里。</summary>
    private static string NormalizeDrive(string path)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        return root is null
            ? string.Empty
            : Path.TrimEndingDirectorySeparator(root).ToUpperInvariant();
    }

    /// <summary>
    /// 用户文件夹里系统自带的「特殊文件夹」→ 图标 + 排序优先级。
    /// <para>
    /// 用<strong>解析后的真实路径</strong>比对：这些文件夹可能被重定向（OneDrive）或使用本地化名称，
    /// 不能按名字硬匹配。图标取自 Segoe MDL2 Assets（与本对话框其它图标同一套字体）。
    /// </para>
    /// </summary>
    private static readonly (Environment.SpecialFolder Folder, string Glyph, int Rank)[] SpecialFolders =
    [
        (Environment.SpecialFolder.Desktop, "\uE8FC", 0),       // 桌面
        (Environment.SpecialFolder.MyDocuments, "\uE8A5", 2),   // 文档
        (Environment.SpecialFolder.MyPictures, "\uE8B9", 3),    // 图片
        (Environment.SpecialFolder.MyMusic, "\uE8D6", 4),       // 音乐
        (Environment.SpecialFolder.MyVideos, "\uE714", 5),      // 视频
    ];

    /// <summary>当前路径是不是「用户文件夹」本身（只有在这一层才换图标、排到前面）。</summary>
    private static bool IsUserProfileRoot(string? path)
    {
        string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return !string.IsNullOrEmpty(userFolder)
               && !string.IsNullOrEmpty(path)
               && string.Equals(
                   Path.TrimEndingDirectorySeparator(path),
                   Path.TrimEndingDirectorySeparator(userFolder),
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 特殊文件夹的完整路径 →（图标, 排序优先级）。
    /// 「下载」在 .NET 里没有对应的 <see cref="Environment.SpecialFolder"/>，按用户文件夹拼出来。
    /// </summary>
    private static Dictionary<string, (string Glyph, int Rank)> BuildSpecialFolders()
    {
        var map = new Dictionary<string, (string Glyph, int Rank)>(StringComparer.OrdinalIgnoreCase);

        foreach ((Environment.SpecialFolder folder, string glyph, int rank) in SpecialFolders)
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path))
            {
                map[Path.TrimEndingDirectorySeparator(path)] = (glyph, rank);
            }
        }

        string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userFolder))
        {
            map[Path.Combine(userFolder, "Downloads")] = ("\uE896", 1);
        }

        return map;
    }

    private static bool IsHiddenOrSystem(string directory)
    {
        // 以点开头的目录（.git / .vs / .ServaUndoTemp …）：FAT / exFAT 上不一定带隐藏属性，
        // 按名字再挡一次，保证「选择文件夹」时看不到这些隐藏文件夹。
        if (Path.GetFileName(directory).StartsWith('.'))
        {
            return true;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(directory);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
