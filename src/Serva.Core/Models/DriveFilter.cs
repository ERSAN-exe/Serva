namespace Serva.Core.Models;

/// <summary>
/// 存储设备总线类型，取值对应 Win32 的 <c>STORAGE_BUS_TYPE</c>。
/// </summary>
public enum StorageBusType : uint
{
    Unknown = 0,
    Scsi = 1,
    Atapi = 2,
    Ata = 3,
    FireWire = 4,
    Ssa = 5,
    Fibre = 6,
    Usb = 7,
    Raid = 8,
    IScsi = 9,
    Sas = 10,
    Sata = 11,
    Sd = 12,
    Mmc = 13,
    Virtual = 14,
    FileBackedVirtual = 15,
    Spaces = 16,
    Nvme = 17,
    Scm = 18,
    Ufs = 19,
}

/// <summary>驱动器列表的筛选方式。</summary>
public enum DriveFilter
{
    /// <summary>
    /// 只显示可移动存储设备。判定依据是「设备所在总线是 USB / SD / MMC / 1394」或
    /// 「介质可移动」，而不是仅看 <see cref="System.IO.DriveType"/>。
    /// </summary>
    Removable,

    /// <summary>数据驱动器（排除系统盘、光驱、网络盘、未就绪的驱动器）。</summary>
    AllDataDrives,

    /// <summary>所有本地驱动器，包含系统盘（仅排除光驱、网络盘、未就绪的驱动器）。</summary>
    LocalDrives,
}
