namespace Serva.Core.Models;

/// <summary>
/// 写在移动设备根目录下的对应关系文件（<c>.serva-mappings.json</c>，带隐藏属性）。
/// <para>
/// 对应关系跟着设备走：换一台电脑、或本机设置文件被清空后，只要设备还在，
/// 插上就能把原来的对应关系找回来。
/// </para>
/// </summary>
public sealed class DeviceMappingFile
{
    /// <summary>写入本文件的设备身份（硬件 ID + 卷序列号），用于确认文件确实是这台设备写的。</summary>
    public string? DeviceIdentity { get; set; }

    /// <summary>设备展示名（如 <c>E:\ KINGSTON</c>），仅便于人工查看。</summary>
    public string? DeviceName { get; set; }

    /// <summary>写入时间（ISO 8601），仅便于人工排查。</summary>
    public string? SavedAt { get; set; }

    /// <summary>写入本文件的电脑标识（见 <c>ComputerIdentity</c>）。</summary>
    public string? ComputerId { get; set; }

    /// <summary>写入本文件的电脑名（只便于人工查看）。</summary>
    public string? ComputerName { get; set; }

    /// <summary>
    /// 对应关系列表（<strong>本机</strong>的那一段，同时便于人眼查看）。
    /// 真正的存储按 <see cref="Computers"/> 分段保存。
    /// </summary>
    public List<MappingSetting> Mappings { get; set; } = [];

    /// <summary>按电脑分段保存的对应关系：同一个设备在不同电脑上各用各的配置。</summary>
    public List<ComputerMappingSection> Computers { get; set; } = [];
}

/// <summary>一台电脑在这台设备上保存过的对应关系。</summary>
public sealed class ComputerMappingSection
{
    public string ComputerId { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public string SavedAt { get; set; } = string.Empty;

    public List<MappingSetting> Mappings { get; set; } = [];
}
