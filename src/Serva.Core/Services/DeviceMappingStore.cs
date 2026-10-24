using System.Text.Json;
using System.Text.Json.Serialization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 移动设备根目录下的对应关系文件读写：<c>&lt;设备根&gt;\.serva-mappings.json</c>（隐藏文件）。
/// <para>
/// 设备随时可能被拔掉，所以这里的读写一律「失败即忽略」，绝不抛异常影响界面。
/// </para>
/// </summary>
public static class DeviceMappingStore
{
    /// <summary>文件名（点开头 + 隐藏属性，资源管理器里默认看不到）。</summary>
    public const string FileName = ".serva-mappings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>设备根目录下的对应关系文件路径。</summary>
    public static string FilePathFor(string deviceRoot) =>
        Path.Combine(Path.TrimEndingDirectorySeparator(deviceRoot), FileName);

    /// <summary>文件是否存在。</summary>
    public static bool Exists(string deviceRoot)
    {
        try
        {
            return File.Exists(FilePathFor(deviceRoot));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>读取；文件不存在 / 已损坏 / 设备已拔出时返回 <c>null</c>。</summary>
    public static DeviceMappingFile? Load(string deviceRoot)
    {
        try
        {
            string path = FilePathFor(deviceRoot);
            if (!File.Exists(path))
            {
                return null;
            }

            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<DeviceMappingFile>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                      or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 写入并设为隐藏文件；失败返回 <c>false</c>（只读设备 / 空间不足等）。
    /// <para>
    /// 注意：Windows 不允许直接用 <see cref="FileMode.Create"/> 覆写<strong>隐藏</strong>文件
    /// （会以「拒绝访问」失败），所以每次写入前先摘掉隐藏属性，写完再设回去 ——
    /// 否则文件只会在第一次生效，之后对应对关系的任何改动都写不进去。
    /// </para>
    /// </summary>
    public static bool Save(string deviceRoot, DeviceMappingFile file)
    {
        try
        {
            string path = FilePathFor(deviceRoot);

            // 保留这台设备上「其他电脑」的记录：只替换本机那一段，
            // 同一个设备在不同电脑上来回插时，各自的对应关系不会互相覆盖。
            DeviceMappingFile? existing = Load(deviceRoot);

            file.ComputerId = ComputerIdentity.CurrentId;
            file.ComputerName = ComputerIdentity.CurrentName;
            file.Computers = MergeSections(existing, file);

            ClearHidden(path);

            using (FileStream stream = File.Create(path))
            {
                JsonSerializer.Serialize(stream, file, JsonOptions);
            }

            Hide(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 取「本机」在这台设备上的对应关系。
    /// <para>本机还没有分段记录时（旧格式文件只有顶层 <c>Mappings</c>），沿用顶层记录。</para>
    /// </summary>
    public static IReadOnlyList<MappingSetting> MappingsForCurrentComputer(DeviceMappingFile file)
    {
        string id = ComputerIdentity.CurrentId;

        ComputerMappingSection? section = file.Computers
            .FirstOrDefault(c => string.Equals(c.ComputerId, id, StringComparison.OrdinalIgnoreCase));

        if (section is not null)
        {
            return section.Mappings;
        }

        return file.Computers.Count == 0 ? file.Mappings : [];
    }

    /// <summary>把「其他电脑」的分段原样保留，再写入（或替换）本机这一段。</summary>
    private static List<ComputerMappingSection> MergeSections(DeviceMappingFile? existing, DeviceMappingFile incoming)
    {
        string id = ComputerIdentity.CurrentId;
        var sections = new List<ComputerMappingSection>();

        foreach (ComputerMappingSection section in existing?.Computers ?? [])
        {
            if (!string.Equals(section.ComputerId, id, StringComparison.OrdinalIgnoreCase))
            {
                sections.Add(section);
            }
        }

        sections.Add(new ComputerMappingSection
        {
            ComputerId = id,
            ComputerName = ComputerIdentity.CurrentName,
            SavedAt = incoming.SavedAt ?? DateTime.Now.ToString("s"),
            Mappings = [.. incoming.Mappings.Select(static m => m.Clone())],
        });

        return sections;
    }

    /// <summary>摘掉隐藏属性（不存在 / 失败时忽略）。</summary>
    private static void ClearHidden(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

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

    /// <summary>设置隐藏属性（FAT / exFAT / NTFS 都支持）；失败时忽略 —— 文件本身已经写好了。</summary>
    private static void Hide(string path)
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
}
