using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>存储设备固件里读到的硬件标识（读不到时全部为空串）。</summary>
/// <param name="VendorId">厂商（如 <c>Kingston</c>）。</param>
/// <param name="ProductId">型号（如 <c>DataTraveler 3.0</c>）。</param>
/// <param name="Revision">固件版本（如 <c>PMAP</c>）。</param>
/// <param name="SerialNumber">出厂序列号。</param>
internal readonly record struct StorageHardware(
    string VendorId,
    string ProductId,
    string Revision,
    string SerialNumber)
{
    public static StorageHardware Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>「Kingston DataTraveler 3.0 PMAP」式的硬件名称。</summary>
    public string Name => Compose(' ', VendorId, ProductId, Revision);

    /// <summary>「厂商 | 型号 | 序列号」式的硬件 ID；三者全为空时返回空串。</summary>
    public string Id => VendorId.Length == 0 && ProductId.Length == 0 && SerialNumber.Length == 0
        ? string.Empty
        : Compose('|', VendorId, ProductId, SerialNumber);

    private static string Compose(char separator, params string[] parts) =>
        string.Join(separator, parts.Where(static p => p.Length > 0));
}

/// <summary>
/// 通过 <c>IOCTL_STORAGE_QUERY_PROPERTY</c> 查询驱动器所在总线、「介质可移动」标志与硬件标识
/// （厂商 / 型号 / 序列号）。
/// <para>
/// 为什么需要它：相当多的 U 盘 / 移动硬盘（USB 转接的 SSD、虚拟机直通的 USB 设备、
/// 大容量 exFAT 移动盘等）会被 Windows 报告为「固定磁盘」（<c>DriveType.Fixed</c>），
/// 只凭 <c>DriveType</c> 无法识别为可移动存储设备。总线类型才是可靠判据。
/// </para>
/// <para>
/// 硬件标识用来区分「换盘符」与「换设备」：同一台设备插到别的盘符上身份不变，
/// 而另一台设备占用同一盘符时身份会变化，据此决定是否还能沿用已保存的对应关系。
/// </para>
/// </summary>
internal static class StorageBus
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint StorageDeviceProperty = 0;
    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const int QuerySize = 12;

    /// <summary>描述符缓冲区：固定头部（36 字节）+ 固件字符串（厂商 / 型号 / 序列号）。</summary>
    private const int DescriptorSize = 1024;

    /// <summary>查询驱动器信息；失败返回 <c>false</c>（调用方应回退到 <see cref="DriveType"/> 判定）。</summary>
    public static bool TryQuery(char driveLetter, out StorageBusType busType, out bool removableMedia) =>
        TryQuery(driveLetter, out busType, out removableMedia, out _);

    /// <summary>查询驱动器信息，并读取设备固件里的硬件标识。</summary>
    public static bool TryQuery(
        char driveLetter,
        out StorageBusType busType,
        out bool removableMedia,
        out StorageHardware hardware)
    {
        busType = StorageBusType.Unknown;
        removableMedia = false;
        hardware = StorageHardware.Empty;

        SafeFileHandle? handle = null;
        nint queryBuffer = 0;
        nint descriptorBuffer = 0;

        try
        {
            // 访问权限传 0 即可：查询属性不需要读写权限，普通用户也能调用。
            handle = CreateFileW(
                $@"\\.\{driveLetter}:",
                0,
                FileShareReadWrite,
                0,
                OpenExisting,
                0,
                0);

            if (handle.IsInvalid)
            {
                return false;
            }

            queryBuffer = Marshal.AllocHGlobal(QuerySize);
            Marshal.WriteInt32(queryBuffer, 0, (int)StorageDeviceProperty);
            Marshal.WriteInt32(queryBuffer, 4, 0);
            Marshal.WriteInt32(queryBuffer, 8, 0);

            descriptorBuffer = Marshal.AllocHGlobal(DescriptorSize);

            if (!DeviceIoControl(
                    handle,
                    IoctlStorageQueryProperty,
                    queryBuffer,
                    QuerySize,
                    descriptorBuffer,
                    DescriptorSize,
                    out uint returned,
                    0))
            {
                return false;
            }

            // STORAGE_DEVICE_DESCRIPTOR 头部偏移：
            // 0 Version, 4 Size, 8 DeviceType, 9 DeviceTypeModifier,
            // 10 RemovableMedia, 11 CommandQueueing, 28 BusType
            if (Marshal.ReadInt32(descriptorBuffer, 4) < 32)
            {
                return false;
            }

            removableMedia = Marshal.ReadByte(descriptorBuffer, 10) != 0;
            busType = (StorageBusType)(uint)Marshal.ReadInt32(descriptorBuffer, 28);
            hardware = ReadHardware(descriptorBuffer, returned);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (queryBuffer != 0)
            {
                Marshal.FreeHGlobal(queryBuffer);
            }

            if (descriptorBuffer != 0)
            {
                Marshal.FreeHGlobal(descriptorBuffer);
            }

            handle?.Dispose();
        }
    }

    /// <summary>读取 <c>STORAGE_DEVICE_DESCRIPTOR</c> 里的厂商 / 型号 / 版本 / 序列号。</summary>
    private static StorageHardware ReadHardware(nint descriptor, uint bytesReturned)
    {
        int limit = (int)Math.Min(bytesReturned, DescriptorSize);

        // 头部偏移：12 VendorIdOffset, 16 ProductIdOffset, 20 ProductRevisionOffset, 24 SerialNumberOffset
        return new StorageHardware(
            ReadDescriptorString(descriptor, Marshal.ReadInt32(descriptor, 12), limit),
            ReadDescriptorString(descriptor, Marshal.ReadInt32(descriptor, 16), limit),
            ReadDescriptorString(descriptor, Marshal.ReadInt32(descriptor, 20), limit),
            ReadDescriptorString(descriptor, Marshal.ReadInt32(descriptor, 24), limit));
    }

    /// <summary>读取描述符内的 ANSI 字符串（偏移相对描述符起点）；越界 / 为空时返回空串。</summary>
    private static string ReadDescriptorString(nint descriptor, int offset, int limit)
    {
        if (offset <= 0 || offset >= limit)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(64);
        for (int i = offset; i < limit; i++)
        {
            byte value = Marshal.ReadByte(descriptor, i);
            if (value == 0)
            {
                break;
            }

            if (value is >= 32 and < 127)
            {
                builder.Append((char)value);
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>判断该总线是否属于「可插拔的外部存储」。</summary>
    public static bool IsExternalBus(StorageBusType busType) =>
        busType is StorageBusType.Usb
            or StorageBusType.Sd
            or StorageBusType.Mmc
            or StorageBusType.FireWire;

    public static string Describe(StorageBusType busType) => busType switch
    {
        StorageBusType.Usb => "USB",
        StorageBusType.Sd => "SD",
        StorageBusType.Mmc => "MMC",
        StorageBusType.FireWire => "1394",
        StorageBusType.Sata => "SATA",
        StorageBusType.Nvme => "NVMe",
        StorageBusType.Scsi => "SCSI",
        StorageBusType.Ata or StorageBusType.Atapi => "ATA",
        StorageBusType.Raid => "RAID",
        StorageBusType.Spaces => CoreText.Get("Storage spaces", "存储空间"),
        StorageBusType.Virtual or StorageBusType.FileBackedVirtual => CoreText.Get("Virtual disk", "虚拟磁盘"),
        _ => CoreText.Get("Other", "其它"),
    };

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        int nInBufferSize,
        nint lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);
}
