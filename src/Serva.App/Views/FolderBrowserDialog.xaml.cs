using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
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

/// <summary>列表中的一项：驱动器的根路径或一个子目录。</summary>
public sealed record FolderEntry(string FullPath, string Name, bool IsDrive);

/// <summary>
/// 软件内置的文件夹浏览对话框（不使用系统资源管理器）。
/// </summary>
public sealed partial class FolderBrowserDialog : ContentDialog
{
    private readonly ObservableCollection<FolderEntry> _entries = [];

    /// <summary>不出现在盘符列表里的盘符根（本机模式下来自「当前选中的移动设备」）。</summary>
    private readonly IReadOnlyList<string> _excludedDrives;
    private readonly FolderBrowserMode _mode;
    private readonly string _root;
    private string? _current;

    public FolderBrowserDialog(
        FolderBrowserMode mode,
        string? root = null,
        IEnumerable<string>? excludedDrives = null)
    {
        InitializeComponent();

        _mode = mode;
        _root = string.IsNullOrWhiteSpace(root) ? string.Empty : Normalize(root);
        _current = mode == FolderBrowserMode.SingleRoot && _root.Length > 0 ? _root : null;
        _excludedDrives = [.. (excludedDrives ?? []).Select(NormalizeDrive).Where(static d => d.Length > 0)];

        Title = mode == FolderBrowserMode.SingleRoot
            ? AppText.Current.ChooseDeviceFolder
            : AppText.Current.ChooseLocalFolder;

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

        RootText.Text = _mode == FolderBrowserMode.SingleRoot
            ? AppText.Current.DeviceRootLabel + _root
            : AppText.Current.DriveRootLabel + (Path.GetPathRoot(_current) ?? _current);

        string baseRoot = _mode == FolderBrowserMode.SingleRoot ? _root : Path.GetPathRoot(_current) ?? _current;
        string relative = Path.GetRelativePath(baseRoot, _current);
        CurrentText.Text = relative == "." ? "\\" : "\\" + relative.Replace('/', '\\');

        UpButton.IsEnabled = true;

        // 只有本机模式在盘符内部才需要「返回盘符选择」（设备模式有上一级兜底，且无法越出设备）。
        BackToDrivesButton.Visibility = _mode == FolderBrowserMode.LocalDrives
            ? Visibility.Visible
            : Visibility.Collapsed;

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_current)
                         .OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (IsHiddenOrSystem(directory))
                {
                    continue;
                }

                _entries.Add(new FolderEntry(directory, Path.GetFileName(directory), false));
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
        foreach (DeviceInfo drive in VolumeInfo.GetDrives(DriveFilter.LocalDrives))
        {
            if (_excludedDrives.Contains(NormalizeDrive(drive.RootPath), StringComparer.Ordinal))
            {
                continue;
            }

            _entries.Add(new FolderEntry(drive.RootPath, drive.DisplayName, true));
        }

        // 桌面：排在盘符列表下方，点进去后可用「使用当前文件夹」直接选桌面。
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
        {
            _entries.Add(new FolderEntry(desktop, AppText.Current.DesktopFolder, false));
        }

        RootText.Text = AppText.Current.LocalDrivesCaption;
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

    private static bool IsHiddenOrSystem(string directory)
    {
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
