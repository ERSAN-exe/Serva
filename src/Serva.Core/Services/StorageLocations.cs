namespace Serva.Core.Services;

/// <summary>应用的持久化位置（设置 / 增量快照 / 操作日志）。</summary>
public static class StorageLocations
{
    /// <summary><c>%LocalAppData%\Serva</c></summary>
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serva");

    /// <summary>设置文件。</summary>
    public static string SettingsFile => SettingsStore.DefaultSettingsPath;

    /// <summary>增量快照目录。</summary>
    public static string StateDirectory => SnapshotStore.DefaultStateDirectory;

    /// <summary>操作日志目录。</summary>
    public static string JournalDirectory => Journal.DefaultJournalDirectory;

    /// <summary>目录当前是否存在（用于界面提示）。</summary>
    public static bool DirectoryExists(string directory)
    {
        try
        {
            return Directory.Exists(directory);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
