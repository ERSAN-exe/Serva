namespace Serva.Core.Models;

/// <summary>持久化到 <c>%LocalAppData%\Serva\settings.json</c> 的应用设置。</summary>
public sealed class AppSettings
{
    /// <summary>明暗主题：<c>system</c> / <c>light</c> / <c>dark</c>。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// 界面语言：<c>system</c>（跟随系统，默认）/ <c>en-US</c> / <c>zh-Hans</c>。
    /// </summary>
    public string Language { get; set; } = "system";

    /// <summary>
    /// 是否允许同时选择两个可移动设备（一块作为同步目标，另一块作为本机来源）。
    /// <para>
    /// 打开时「选择本机文件夹」的盘符列表里会多出<strong>其它</strong>可移动设备
    /// （当前选中的目标设备始终排除）；关闭后只列本机硬盘。
    /// </para>
    /// </summary>
    public bool AllowTwoRemovableDevices { get; set; } = true;

    /// <summary>
    /// 是否允许把本机硬盘（<strong>不含系统盘</strong>）也当作同步目标。
    /// 默认关闭：设备列表里只显示 U 盘 / 移动硬盘。
    /// </summary>
    public bool AllowLocalDrives { get; set; }

    /// <summary>执行策略与排除规则。</summary>
    public SyncOptions Options { get; set; } = new();

    /// <summary>
    /// 按设备分开保存的对应关系（键见 <see cref="DeviceInfo.StorageKey"/>）。
    /// <para>多设备时只读写当前选中的那一台，切换设备不会互相影响。</para>
    /// </summary>
    public List<DeviceMappingsSetting> DeviceMappings { get; set; } = [];

    /// <summary>
    /// 旧版本设置文件里的「全局」对应关系列表：启动时会被搬进 <see cref="DeviceMappings"/>
    /// （见 <c>SettingsStore</c>），之后不再使用。
    /// </summary>
    public List<MappingSetting> Mappings { get; set; } = [];

    /// <summary>上次使用的目标设备根路径。</summary>
    public string? LastTargetRoot { get; set; }

    /// <summary>
    /// 上次使用的目标设备身份（硬件 ID + 卷序列号）。用于在设备换了盘符、或别的设备占用
    /// 同一盘符时，仍能准确判断「是不是同一台设备」。
    /// </summary>
    public string? LastTargetIdentity { get; set; }

    /// <summary>启动时是否检查 GitHub 上的新版本（默认开启）。</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>上次检查更新的时间（UTC），用于几分钟内的重复启动去重。</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// 上次检查时远程发布的最新版本号。
    /// <para>
    /// 启动时先用它在本地恢复「发现新版本」的状态，打开软件就能看到提示条 / 导航小红点，
    /// 不必等这一轮联网检查（断网时也能提示上次查到的结果）。
    /// </para>
    /// </summary>
    public string? LatestKnownVersion { get; set; }

    /// <summary>用户已经忽略过的新版本号，避免每次启动重复提醒。</summary>
    public string? DismissedUpdateVersion { get; set; }

    /// <summary>
    /// 固定（收藏）的本机文件夹：会作为快捷入口出现在「选择本机文件夹」对话框里。
    /// </summary>
    public List<string> PinnedFolders { get; set; } = [];

    /// <summary>
    /// 取这台设备保存过的对应关系；<c>false</c> 表示本机还没有这台设备的记录
    /// （此时才应该去读设备根目录里的文件）。
    /// </summary>
    public bool TryGetMappingsForDevice(DeviceInfo device, out IReadOnlyList<MappingSetting> mappings)
    {
        DeviceMappingsSetting? entry = FindDeviceEntry(device);
        mappings = entry?.Mappings ?? [];
        return entry is not null;
    }

    /// <summary>
    /// 写入这台设备的对应关系（空列表也会保留一条空记录，见 <see cref="DeviceMappingsSetting.Mappings"/>）。
    /// </summary>
    public void SetMappingsForDevice(DeviceInfo device, IEnumerable<MappingSetting> mappings)
    {
        // 同一台设备只留一段：清掉键不同、但身份相同的旧段（旧版本只写硬件 ID）。
        DeviceMappings.RemoveAll(
            entry => !string.Equals(entry.DeviceKey, device.StorageKey, StringComparison.OrdinalIgnoreCase)
                     && entry.DeviceKey.Length > 0
                     && device.MatchesIdentity(entry.DeviceKey));

        SetMappingsForDevice(device.StorageKey, device.RootPath, device.VolumeLabel, mappings);
    }

    /// <summary>按设备键写入对应关系（迁移旧设置时用）。</summary>
    public void SetMappingsForDevice(
        string deviceKey,
        string rootPath,
        string? volumeLabel,
        IEnumerable<MappingSetting> mappings)
    {
        DeviceMappingsSetting? entry = DeviceMappings.FirstOrDefault(
            item => string.Equals(item.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            entry = new DeviceMappingsSetting { DeviceKey = deviceKey };
            DeviceMappings.Add(entry);
        }

        entry.RootPath = rootPath;
        entry.VolumeLabel = volumeLabel;
        entry.SavedAt = DateTimeOffset.UtcNow;
        entry.Mappings = [.. mappings.Select(static m => m.Clone())];
    }

    /// <summary>
    /// 找这台设备的记录：先按键精确匹配，再按设备身份兜底（旧版本键里没有卷序列号）。
    /// </summary>
    private DeviceMappingsSetting? FindDeviceEntry(DeviceInfo device)
    {
        DeviceMappingsSetting? exact = DeviceMappings.FirstOrDefault(
            entry => string.Equals(entry.DeviceKey, device.StorageKey, StringComparison.OrdinalIgnoreCase));

        return exact ?? DeviceMappings.FirstOrDefault(
            entry => entry.DeviceKey.Length > 0 && device.MatchesIdentity(entry.DeviceKey));
    }

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        Language = Language,
        AllowLocalDrives = AllowLocalDrives,
        AllowTwoRemovableDevices = AllowTwoRemovableDevices,
        Options = Options.Clone(),
        DeviceMappings = [.. DeviceMappings.Select(static d => d.Clone())],
        Mappings = [.. Mappings.Select(static m => m.Clone())],
        LastTargetRoot = LastTargetRoot,
        LastTargetIdentity = LastTargetIdentity,
        CheckUpdatesOnStartup = CheckUpdatesOnStartup,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        LatestKnownVersion = LatestKnownVersion,
        DismissedUpdateVersion = DismissedUpdateVersion,
        PinnedFolders = [.. PinnedFolders],
    };
}
