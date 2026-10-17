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

    /// <summary>对应关系列表。</summary>
    public List<MappingSetting> Mappings { get; set; } = [];
}
