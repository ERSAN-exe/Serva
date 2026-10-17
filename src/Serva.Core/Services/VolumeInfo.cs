using System.Runtime.InteropServices;
using System.Text;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 驱动器 / 卷信息读取。除了 <see cref="System.IO.DriveInfo"/> 提供的容量、卷标、文件系统外，
/// 还通过 Win32 读取「卷序列号」——增量快照文件按卷序列号命名。
/// </summary>
public static class VolumeInfo
{
    /// <summary>枚举驱动器并按 <paramref name="filter"/> 筛选。</summary>
    public static IReadOnlyList<DeviceInfo> GetDrives(DriveFilter filter)
    {
        var result = new List<DeviceInfo>();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return result;
        }

        foreach (DriveInfo drive in drives)
        {
            DeviceInfo? info = TryDescribe(drive);
            if (info is null)
            {
                continue;
            }

            bool accepted = filter switch
            {
                DriveFilter.Removable => info.IsRemovableStorage,
                DriveFilter.AllDataDrives => !info.IsSystemDrive,
                _ => true,   // LocalDrives：TryDescribe 已剔除光驱 / 网络盘 / 未就绪的驱动器
            };

            if (accepted)
            {
                result.Add(info);
            }
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.RootPath, b.RootPath));
        return result;
    }

    /// <summary>描述单个驱动器；不可用时返回 <c>null</c>。</summary>
    public static DeviceInfo? TryDescribe(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }

            // 光驱 / 网络盘 / 无根目录的卷不作为同步目标。
            if (drive.DriveType is DriveType.CDRom or DriveType.NoRootDirectory or DriveType.Unknown or DriveType.Network)
            {
                return null;
            }

            string root = drive.Name;                        // 例如 "E:\"
            string letter = root.TrimEnd('\\', '/');
            string label = string.Empty;
            string format = string.Empty;

            try
            {
                label = drive.VolumeLabel ?? string.Empty;
            }
            catch (IOException)
            {
                // 某些可移动设备不支持读取卷标，忽略。
            }

            try
            {
                format = drive.DriveFormat ?? string.Empty;
            }
            catch (IOException)
            {
            }

            long total = SafeSize(() => drive.TotalSize);
            long free = SafeSize(() => drive.AvailableFreeSpace);

            // 总线查询：识别「以固定磁盘上报的 USB 设备」等只靠 DriveType 无法判定的情况，
            // 并顺带读取硬件标识（厂商 / 型号 / 序列号）用于区分「换盘符」与「换设备」。
            StorageBusType busType = StorageBusType.Unknown;
            bool removableMedia = false;
            StorageHardware hardware = StorageHardware.Empty;
            bool busKnown = letter.Length > 0
                            && StorageBus.TryQuery(letter[0], out busType, out removableMedia, out hardware);

            return new DeviceInfo(
                root,
                letter,
                label,
                format,
                drive.DriveType,
                total,
                GetVolumeSerial(root))
            {
                FreeSpace = free,
                BusType = busType,
                BusTypeKnown = busKnown,
                RemovableMedia = removableMedia,
                IsSystemDrive = IsSystemVolume(root),
                HardwareName = Normalize(hardware.Name),
                HardwareId = Normalize(hardware.Id),
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>该根路径是否为系统盘（<c>%SystemRoot%</c> 所在卷）。</summary>
    public static bool IsSystemVolume(string rootPath)
    {
        try
        {
            string? systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(systemRoot))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(rootPath).TrimEnd('\\', '/'),
                Path.GetFullPath(systemRoot).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>读取卷序列号（如 <c>1A2B3C4D</c>）；失败时返回全 0。</summary>
    public static string GetVolumeSerial(string rootPath)
    {
        try
        {
            if (GetVolumeInformationW(
                    rootPath,
                    null,
                    0,
                    out uint serial,
                    out _,
                    out _,
                    null,
                    0))
            {
                return serial.ToString("X8");
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return "00000000";
    }

    /// <summary>「E:\ KINGSTON」式的简短描述，用于结果表 / 日志。</summary>
    public static string DescribeRoot(string rootPath)
    {
        DeviceInfo? info = TryDescribe(new DriveInfo(Path.GetPathRoot(rootPath) ?? rootPath));
        return info?.DisplayName ?? rootPath;
    }

    /// <summary>固件字符串常带填充空格：去掉首尾空白并把连续空白折叠成单个空格。</summary>
    private static string Normalize(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private static long SafeSize(Func<long> getter)
    {
        try
        {
            return getter();
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
