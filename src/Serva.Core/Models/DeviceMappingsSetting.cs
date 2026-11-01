namespace Serva.Core.Models;

/// <summary>
/// 某一台设备（本机视角）的对应关系缓存。
/// <para>
/// 对应关系的「正本」在设备根目录的 <c>.serva-mappings.json</c> 里；这里再按设备存一份，
/// 一是设备文件写不进去（只读卡 / 空间不足）时仍然记得用户的删除与新增，
/// 二是切换设备时只读写这台设备自己那一段，绝不可能串台。
/// </para>
/// </summary>
public sealed class DeviceMappingsSetting
{
    /// <summary>设备键（见 <see cref="DeviceInfo.StorageKey"/>）：卷序列号唯一时就是设备身份。</summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>上次看到这台设备时的盘符（仅便于人工查看；盘符会变，所以不作为键）。</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>卷标（仅便于人工查看）。</summary>
    public string? VolumeLabel { get; set; }

    /// <summary>最后一次修改时间（UTC，便于排查）。</summary>
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>
    /// 这台设备的对应关系。空列表<strong>有意义</strong>：表示用户在这台设备上把对应关系全删了，
    /// 不能因为设备文件里还留着旧内容就又读回来。
    /// </summary>
    public List<MappingSetting> Mappings { get; set; } = [];

    public DeviceMappingsSetting Clone() => new()
    {
        DeviceKey = DeviceKey,
        RootPath = RootPath,
        VolumeLabel = VolumeLabel,
        SavedAt = SavedAt,
        Mappings = [.. Mappings.Select(static m => m.Clone())],
    };
}
