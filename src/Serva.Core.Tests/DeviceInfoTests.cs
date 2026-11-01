using Serva.Core.Models;

namespace Serva.Core.Tests;

/// <summary>
/// 设备身份的判定：多设备（尤其是同型号 U 盘 / 同一块硬盘的多个分区）不能混成一台，
/// 否则「选择哪台设备」和「对应关系属于哪台设备」都会串台。
/// </summary>
public class DeviceInfoTests
{
    private static DeviceInfo Device(
        string root,
        string hardwareId,
        string volumeSerial,
        string label = "KINGSTON") =>
        new(
            root,
            root.TrimEnd('\\'),
            label,
            "exFAT",
            System.IO.DriveType.Removable,
            64L * 1024 * 1024 * 1024,
            volumeSerial)
        {
            HardwareId = hardwareId,
        };

    [Fact]
    public void Identity_IncludesVolumeSerial_SoTwoVolumesOfOneDiskStayApart()
    {
        DeviceInfo first = Device(@"E:\", "Kingston|DataTraveler|001", "1A2B3C4D");
        DeviceInfo second = Device(@"F:\", "Kingston|DataTraveler|001", "9F8E7D6C");

        // 硬件 ID 相同（同一块硬盘的两个分区），身份必须不同。
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.False(first.IsSameDevice(second));
        Assert.False(first.IsSameVolume(second));
    }

    [Fact]
    public void Identity_IsIndependentOfDriveLetter_SoRepluggingKeepsTheSameVolume()
    {
        DeviceInfo before = Device(@"E:\", "Kingston|DataTraveler|001", "1A2B3C4D");
        DeviceInfo after = Device(@"G:\", "Kingston|DataTraveler|001", "1A2B3C4D");

        Assert.True(before.IsSameDevice(after));
        Assert.True(before.IsSameVolume(after));
        Assert.Equal(before.StorageKey, after.StorageKey);
    }

    [Fact]
    public void StorageKey_FallsBackToDriveLetter_WhenVolumeSerialIsUnreadable()
    {
        DeviceInfo first = Device(@"E:\", "Generic|Flash|", "00000000");
        DeviceInfo second = Device(@"F:\", "Generic|Flash|", "00000000");

        Assert.False(first.HasVolumeSerial);
        Assert.NotEqual(first.StorageKey, second.StorageKey);
    }

    [Fact]
    public void MatchesIdentity_AcceptsLegacyHardwareIdOnlyRecords()
    {
        DeviceInfo device = Device(@"E:\", "Kingston|DataTraveler|001", "1A2B3C4D");

        Assert.True(device.MatchesIdentity(device.Identity));
        Assert.True(device.MatchesIdentity("Kingston|DataTraveler|001"));   // 旧版本只写硬件 ID
        Assert.True(device.MatchesIdentity(null));
        Assert.False(device.MatchesIdentity("Other|Disk|999|0000ABCD"));
    }
}
