using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>
/// 对应关系必须按设备隔离：多设备时删除 / 新增一台设备的对应关系，绝不能影响另一台，
/// 切换设备也不能把上一台的配置带过去。
/// </summary>
public class DeviceMappingsTests
{
    private static DeviceInfo Device(string root, string hardwareId, string volumeSerial) =>
        new(
            root,
            root.TrimEnd('\\'),
            "USB",
            "exFAT",
            System.IO.DriveType.Removable,
            32L * 1024 * 1024 * 1024,
            volumeSerial)
        {
            HardwareId = hardwareId,
        };

    private static MappingSetting Mapping(string local, string deviceFolder) =>
        new() { LocalRoot = local, DeviceFolder = deviceFolder };

    [Fact]
    public void SetMappingsForDevice_KeepsEachDeviceSeparate()
    {
        var settings = new AppSettings();
        DeviceInfo deviceA = Device(@"E:\", "Vendor|DiskA|1", "11111111");
        DeviceInfo deviceB = Device(@"F:\", "Vendor|DiskB|2", "22222222");

        settings.SetMappingsForDevice(deviceA, [Mapping(@"C:\A", "a")]);
        settings.SetMappingsForDevice(deviceB, [Mapping(@"C:\B", "b")]);

        Assert.True(settings.TryGetMappingsForDevice(deviceA, out IReadOnlyList<MappingSetting> a));
        Assert.True(settings.TryGetMappingsForDevice(deviceB, out IReadOnlyList<MappingSetting> b));
        Assert.Equal(@"C:\A", Assert.Single(a).LocalRoot);
        Assert.Equal(@"C:\B", Assert.Single(b).LocalRoot);
    }

    [Fact]
    public void RemovingAllMappingsForOneDevice_DoesNotAffectTheOther()
    {
        var settings = new AppSettings();
        DeviceInfo deviceA = Device(@"E:\", "Vendor|DiskA|1", "11111111");
        DeviceInfo deviceB = Device(@"F:\", "Vendor|DiskB|2", "22222222");

        settings.SetMappingsForDevice(deviceA, [Mapping(@"C:\A", "a")]);
        settings.SetMappingsForDevice(deviceB, [Mapping(@"C:\B", "b")]);

        // 用户在设备 A 上把对应关系全删了。
        settings.SetMappingsForDevice(deviceA, []);

        Assert.True(settings.TryGetMappingsForDevice(deviceA, out IReadOnlyList<MappingSetting> a));
        Assert.Empty(a);   // 空记录也算「这台设备有记录」，不能再去读设备文件把删掉的读回来

        Assert.True(settings.TryGetMappingsForDevice(deviceB, out IReadOnlyList<MappingSetting> b));
        Assert.Equal(@"C:\B", Assert.Single(b).LocalRoot);
    }

    [Fact]
    public void UnknownDevice_HasNoRecord()
    {
        var settings = new AppSettings();
        DeviceInfo device = Device(@"E:\", "Vendor|DiskA|1", "11111111");

        Assert.False(settings.TryGetMappingsForDevice(device, out IReadOnlyList<MappingSetting> mappings));
        Assert.Empty(mappings);
    }

    [Fact]
    public void LegacyHardwareIdKey_IsAdoptedByTheNewIdentity()
    {
        var settings = new AppSettings();
        DeviceInfo device = Device(@"E:\", "Vendor|DiskA|1", "11111111");

        // 旧版本设置文件里只有硬件 ID（没有卷序列号），仍要能认领。
        settings.SetMappingsForDevice("Vendor|DiskA|1", @"E:\", "USB", [Mapping(@"C:\A", "a")]);

        Assert.True(settings.TryGetMappingsForDevice(device, out IReadOnlyList<MappingSetting> mappings));
        Assert.Equal(@"C:\A", Assert.Single(mappings).LocalRoot);
    }

    [Fact]
    public void SettingsStore_MigratesLegacyGlobalMappingsToTheLastUsedDevice()
    {
        string directory = SyncFixture.NewTempDirectory("settings-migrate");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        var store = new SettingsStore(path);
        AppSettings settings = store.Load();
        settings.LastTargetIdentity = "Vendor|DiskA|1";
        settings.LastTargetRoot = @"E:\";
        settings.Mappings = [Mapping(@"C:\A", "a")];
        store.Save(settings);

        AppSettings reloaded = new SettingsStore(path).Load();

        Assert.Empty(reloaded.Mappings);   // 旧列表已经搬走，避免以后又冒出来

        DeviceInfo device = Device(@"E:\", "Vendor|DiskA|1", "11111111");
        Assert.True(reloaded.TryGetMappingsForDevice(device, out IReadOnlyList<MappingSetting> mappings));
        Assert.Equal(@"C:\A", Assert.Single(mappings).LocalRoot);
    }
}
