namespace Serva.Core.Services;

/// <summary>
/// 覆盖前把原文件挪进<strong>同目录</strong>下的隐藏文件夹 <c>.ServaUndoTemp</c>，供「撤销本次运行」还原。
/// <para>
/// 副本只在本次运行内有效：关闭软件时统一删除（见 <see cref="DeleteAll"/>），
/// 因此重启后无法再撤销已覆盖的文件 —— 与「关掉软件就放弃撤销副本」的设计一致。
/// </para>
/// <para>
/// 目录名已加入默认排除规则，扫描时不会被当成用户文件；镜像删除也不会碰它。
/// </para>
/// </summary>
public static class OverwriteBackup
{
    /// <summary>临时副本目录名（与原文件同目录、隐藏）。</summary>
    public const string FolderName = ".ServaUndoTemp";

    private static readonly object Sync = new();
    private static readonly List<string> Created = [];

    /// <summary>
    /// <paramref name="targetPath"/> 对应的临时副本路径：
    /// <c>&lt;原文件所在目录&gt;\.ServaUndoTemp\&lt;文件名&gt;</c>。
    /// </summary>
    public static string PathFor(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        return Path.Combine(directory, FolderName, Path.GetFileName(targetPath));
    }

    /// <summary>
    /// 把 <paramref name="targetPath"/> 挪进临时副本目录并返回副本路径。
    /// <para>
    /// 只把<strong>目录</strong>设为隐藏，<strong>不</strong>给文件加隐藏属性 ——
    /// 撤销时就是把同一个文件挪回原位，加了隐藏属性会让还原出来的原文件也变成隐藏的。
    /// </para>
    /// <para>失败时抛异常（调用方决定是记日志还是放弃备份）。</para>
    /// </summary>
    public static string Move(string targetPath)
    {
        string backupPath = PathFor(targetPath);
        string? folder = Path.GetDirectoryName(backupPath);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            MakeHidden(folder);
        }

        File.Move(targetPath, backupPath, overwrite: true);
        Register(backupPath);
        return backupPath;
    }

    /// <summary>
    /// 撤销：把副本移回原位，并顺手删掉已经变空的 <c>.ServaUndoTemp</c> 目录。
    /// </summary>
    public static void Restore(string backupPath, string targetPath)
    {
        string? folder = Path.GetDirectoryName(backupPath);

        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        // 兼容旧版本留下的「隐藏副本」：还原出来的文件不能带隐藏属性。
        MakeVisible(backupPath);

        File.Move(backupPath, targetPath);
        Forget(backupPath);
        TryDeleteFolder(folder);
    }

    /// <summary>删除空的临时副本目录；目录里还有东西时保留（不能替用户丢数据）。</summary>
    public static void TryDeleteFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                MakeVisible(folder);
                Directory.Delete(folder);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>登记一个已创建的副本（退出时按它清理）。</summary>
    public static void Register(string backupPath)
    {
        lock (Sync)
        {
            if (!Created.Contains(backupPath, StringComparer.OrdinalIgnoreCase))
            {
                Created.Add(backupPath);
            }
        }
    }

    /// <summary>副本已被「撤销」移回原位：不再参与退出清理。</summary>
    public static void Forget(string backupPath)
    {
        lock (Sync)
        {
            Created.RemoveAll(path => string.Equals(path, backupPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 删除本次运行创建的全部临时副本，并清掉变空的 <c>.ServaUndoTemp</c> 目录。
    /// </summary>
    /// <returns>删除的副本文件数。</returns>
    public static int DeleteAll()
    {
        string[] paths;
        lock (Sync)
        {
            paths = [.. Created];
            Created.Clear();
        }

        int deleted = 0;

        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    MakeVisible(path);
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (string folder in paths
                     .Select(static path => Path.GetDirectoryName(path))
                     .OfType<string>()
                     .Where(static folder => folder.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryDeleteFolder(folder);
        }

        return deleted;
    }

    /// <summary>临时副本目录 / 文件的完整路径集合（测试与诊断用）。</summary>
    public static IReadOnlyList<string> Registered
    {
        get
        {
            lock (Sync)
            {
                return [.. Created];
            }
        }
    }

    private static void MakeHidden(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void MakeVisible(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Hidden) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.Hidden);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
