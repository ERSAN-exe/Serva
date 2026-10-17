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
    /// 设备身份：优先用 <see cref="HardwareId"/>（换盘符也不变），读不到硬件信息时退化为
    /// 「卷序列号 + 卷标 + 容量」。<strong>不含盘符</strong> —— 盘符是会被系统重新分配的。
    /// </summary>
    public string Identity => HardwareId.Length > 0
        ? HardwareId
        : $"{VolumeSerial}|{VolumeLabel}|{TotalSize}";

    /// <summary>是否为同一台设备（只看身份，不看盘符）。</summary>
    public bool IsSameDevice(DeviceInfo? other) =>
        other is not null
        && string.Equals(Identity, other.Identity, StringComparison.OrdinalIgnoreCase);

    /// <summary>「E:\ KINGSTON」式的展示名。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(VolumeLabel)
        ? RootPath
        : $"{RootPath} {VolumeLabel}";

    /// <summary>「57.3 GB 可用 / 119.2 GB」式容量描述。</summary>
    public string CapacityText =>
        CoreText.Format("{0} free of {1}", "{0} 可用 / {1}", Format(FreeSpace), Format(TotalSize));

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
