namespace Serva.Core.Services;

/// <summary>某个本机路径为什么不能作为同步来源。</summary>
public enum ProtectedPathKind
{
    /// <summary>可以选。</summary>
    None,

    /// <summary>系统盘（通常是 <c>C:\</c>）的根目录。</summary>
    SystemDriveRoot,

    /// <summary>系统文件夹本身，或系统文件夹里的子目录。</summary>
    SystemFolder,
}

/// <summary>
/// 不能作为同步来源的本机路径：系统盘根目录与系统文件夹。
/// <para>
/// 这类目录一旦参与同步，Windows / 程序 / 回收站就被当成普通数据 ——
/// 镜像删除会把它们当「目标端多余」删掉，所以界面上不允许选择，业务层也再兜一层。
/// </para>
/// </summary>
public static class ProtectedPaths
{
    /// <summary>各卷根目录里的系统目录（名字固定，任何盘上都一样）。</summary>
    private static readonly string[] SystemDirectoryNames =
    [
        "System Volume Information",
        "$RECYCLE.BIN",
        "RECYCLER",
        "RECYCLED",
        "RECOVERY",
    ];

    /// <summary>本机上的系统文件夹（含它们的子目录）。首次使用时计算一次。</summary>
    private static readonly Lazy<IReadOnlyList<string>> SystemFolders = new(BuildSystemFolders);

    /// <summary>判断该路径能否作为同步目录；不能时说明原因。</summary>
    public static ProtectedPathKind Classify(string? path)
    {
        string normalized = Normalize(path);
        if (normalized.Length == 0)
        {
            return ProtectedPathKind.None;
        }

        // C:\ 这样的盘符根：系统盘的根目录一律不允许。
        string? root = Path.GetPathRoot(normalized);
        if (root is not null
            && string.Equals(Path.TrimEndingDirectorySeparator(root), normalized, StringComparison.OrdinalIgnoreCase)
            && VolumeInfo.IsSystemVolume(normalized))
        {
            return ProtectedPathKind.SystemDriveRoot;
        }

        if (IsSystemDirectoryName(normalized))
        {
            return ProtectedPathKind.SystemFolder;
        }

        foreach (string folder in SystemFolders.Value)
        {
            if (IsSameOrInside(normalized, folder))
            {
                return ProtectedPathKind.SystemFolder;
            }
        }

        // 兜底：带「系统」属性的目录（各卷的隐藏系统目录、驱动程序目录等）。
        return HasSystemAttribute(normalized) ? ProtectedPathKind.SystemFolder : ProtectedPathKind.None;
    }

    /// <summary>是否为「不能选作同步目录」的路径。</summary>
    public static bool IsProtected(string? path) => Classify(path) is not ProtectedPathKind.None;

    /// <summary>路径本身是否就是「System Volume Information」这类固定名字的系统目录。</summary>
    private static bool IsSystemDirectoryName(string normalized)
    {
        string name = Path.GetFileName(normalized);
        return name.Length > 0
               && SystemDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <paramref name="path"/> 是否就是 <paramref name="folder"/>，或者位于它里面。
    /// 用「前缀 + 分隔符」比较，避免 <c>C:\Windows</c> 误伤 <c>C:\Windows7</c>。
    /// </summary>
    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool HasSystemAttribute(string path)
    {
        try
        {
            return Directory.Exists(path)
                   && (File.GetAttributes(path) & FileAttributes.System) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildSystemFolders()
    {
        var folders = new List<string>();

        void Add(string? path, string? sub = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string full = Normalize(sub is null ? path : Path.Combine(path, sub));
                if (full.Length > 0)
                {
                    folders.Add(full);
                }
            }
            catch (ArgumentException)
            {
            }
        }

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Add(windows);
        Add(windows, "System32");
        Add(windows, "SysWOW64");
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));   // C:\ProgramData
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData");  // 用户配置（隐藏系统目录）

        string systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 1 } root
            ? Path.GetPathRoot(root) ?? string.Empty
            : string.Empty;

        foreach (string name in SystemDirectoryNames)
        {
            Add(systemDrive, name);
        }

        return folders;
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.Trim();

        // 「C:」表示「C 盘的当前目录」，直接传给 GetFullPath 会得到进程的工作目录；
        // 补上分隔符才是盘符根目录。
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            trimmed += Path.DirectorySeparatorChar;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
