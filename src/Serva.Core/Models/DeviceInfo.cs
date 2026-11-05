using Serva.Core.Localization;

namespace Serva.Core.Models;

/// <summary>一个可作为同步目标的存储设备（U 盘 / 移动硬盘 / 存储卡 / 其它数据盘）。</summary>
/// <remarks>
/// 注意：<see cref="FreeSpace"/> 会随时间变化，<strong>不要</strong>用 record 相等性判断
/// 「是不是同一台设备」—— 请用 <see cref="Identity"/>（硬件 ID + 卷序列号）。
/// 否则每次「重新扫描」都会被当成换了设备，从而清空用户的对应关系与比较结果。
/// </remarks>
public sealed record DeviceInfo(
    string RootPath,
    string DriveLetter,
    string VolumeLabel,
    string FileSystem,
    DriveType DriveType,
    long TotalSize,
    string VolumeSerial)
{
    /// <summary>可用空间（随时间变化，不用于设备身份判断）。</summary>
    public long FreeSpace { get; init; }

    /// <summary>硬件名称（设备固件里的「厂商 + 型号 + 版本」，如 <c>Kingston DataTraveler 3.0 PMAP</c>）。</summary>
    public string HardwareName { get; init; } = string.Empty;

    /// <summary>
    /// 硬件 ID：「厂商 | 型号 | 出厂序列号」。同一台设备换盘符后依然不变，
    /// 两台不同设备挂到同一盘符上也能区分开。
    /// </summary>
    public string HardwareId { get; init; } = string.Empty;

    /// <summary>设备所在总线（由 <c>IOCTL_STORAGE_QUERY_PROPERTY</c> 查询得到）。</summary>
    public StorageBusType BusType { get; init; } = StorageBusType.Unknown;

    /// <summary>总线查询是否成功。失败时只能退化为 <see cref="DriveType"/> 判定。</summary>
    public bool BusTypeKnown { get; init; }

    /// <summary>介质本身可移动（U 盘 / 存储卡等）。</summary>
    public bool RemovableMedia { get; init; }

    /// <summary>
    /// 是否属于「可移动存储设备」。判据是「总线为 USB / SD / MMC / 1394」或「介质可移动」或
    /// <see cref="DriveType"/> 为 <see cref="DriveType.Removable"/> —— 不只看 <see cref="DriveType"/>，
    /// 因为不少移动盘会被报告为固定磁盘。
    /// </summary>
    public bool IsRemovableStorage =>
        DriveType == DriveType.Removable
        || RemovableMedia
        || (BusTypeKnown && BusType is StorageBusType.Usb or StorageBusType.Sd or StorageBusType.Mmc or StorageBusType.FireWire);

    /// <summary>是否为系统盘（<c>%SystemRoot%</c> 所在卷）。</summary>
    public bool IsSystemDrive { get; init; }

    /// <summary>
    /// 设备身份：<see cref="HardwareId"/>（换盘符也不变）<strong>加卷序列号</strong>，读不到硬件信息时退化为
    /// 「卷序列号 + 卷标 + 容量」。<strong>不含盘符</strong> —— 盘符是会被系统重新分配的。
    /// <para>
    /// 必须带上卷序列号：同一块硬盘上的两个分区、或两块固件不提供唯一序列号的同型号 U 盘，
    /// 只靠硬件 ID 会被当成同一台设备，对应关系就会串台（在 A 上加的出现在 B 上）。
    /// </para>
    /// </summary>
    public string Identity => HardwareId.Length > 0
        ? $"{HardwareId}|{VolumeSerial}"
        : $"{VolumeSerial}|{VolumeLabel}|{TotalSize}";

    /// <summary>卷序列号是否可用（读失败时是 <c>00000000</c>）。</summary>
    public bool HasVolumeSerial =>
        VolumeSerial.Length > 0
        && !string.Equals(VolumeSerial, "00000000", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 在本机设置里区分「哪一台设备」用的键。
    /// <para>
    /// 卷序列号读不到时退化为「身份 + 盘符」—— 否则两块同型号 U 盘会共用一份对应关系。
    /// </para>
    /// </summary>
    public string StorageKey => HasVolumeSerial ? Identity : $"{Identity}|{RootPath}";

    /// <summary>
    /// 旧版本写下的身份（只有硬件 ID，或只有「卷序列号 + 卷标 + 容量」）。
    /// 升级后要能认领这些旧记录，否则看起来像「对应关系丢了」。
    /// </summary>
    private string LegacyIdentity => HardwareId.Length > 0
        ? HardwareId
        : $"{VolumeSerial}|{VolumeLabel}|{TotalSize}";

    /// <summary>设备文件 / 设置里记录的设备身份是否属于本设备（兼容旧格式）。</summary>
    public bool MatchesIdentity(string? identity) =>
        string.IsNullOrEmpty(identity)
        || string.Equals(identity, Identity, StringComparison.OrdinalIgnoreCase)
        || string.Equals(identity, LegacyIdentity, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为同一台设备（只看身份，不看盘符）。</summary>
    public bool IsSameDevice(DeviceInfo? other) =>
        other is not null
        && string.Equals(Identity, other.Identity, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为同一个「卷」（换盘符后返回 <c>false</c>：对应关系要按卷区分）。</summary>
    public bool IsSameVolume(DeviceInfo? other) =>
        other is not null
        && string.Equals(StorageKey, other.StorageKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>「E:\ KINGSTON」式的展示名。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(VolumeLabel)
        ? RootPath
        : $"{RootPath} {VolumeLabel}";

    /// <summary>
    /// 设备列表里的图标（Segoe MDL2 Assets）：可移动设备用 U 盘图标，其余用硬盘图标。
    /// </summary>
    public string Glyph => IsRemovableStorage ? "\uE88E" : "\uEDA2";

    /// <summary>「57.3 GB 可用 / 119.2 GB」式容量描述。</summary>
    public string CapacityText =>
        CoreText.Format("{0} free of {1}", "{0} 可用 / {1}", Format(FreeSpace), Format(TotalSize));

    /// <summary>「G:\ 0123 - 119.2 GB」式标题：名称 + 总容量（设置页设备卡片的大字）。</summary>
    public string TitleText => $"{DisplayName} - {Format(TotalSize)}";

    /// <summary>设备种类：「移动设备」/「本机硬盘」。</summary>
    public string KindText => IsRemovableStorage
        ? CoreText.Get("Removable device", "移动设备")
        : CoreText.Get("Local disk", "本机硬盘");

    /// <summary>「已使用 25 GB」。</summary>
    public string UsedText => CoreText.Format("Used {0}", "已使用 {0}", Format(TotalSize - FreeSpace));

    /// <summary>「94 GB 可用」。</summary>
    public string FreeText => CoreText.Format("{0} free", "{0} 可用", Format(FreeSpace));

    /// <summary>已用空间百分比（0–100），用于容量进度条。</summary>
    public double UsedPercent => TotalSize <= 0
        ? 0
        : Math.Clamp((TotalSize - FreeSpace) * 100.0 / TotalSize, 0, 100);

    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit <= 1
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }
}
