using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>
/// 移动设备根目录下的对应关系文件（<c>&lt;设备根&gt;\.serva-mappings.json</c>，隐藏文件）。
/// </summary>
public class DeviceMappingStoreTests
{
    /// <summary>
    /// 每写一次都要能覆盖上一次的内容。
    /// <para>
    /// 回归用例：文件写完后带隐藏属性，而 Windows 不允许直接覆写隐藏文件 —— 写入前必须先摘掉隐藏属性，
    /// 否则「对应关系随用户选择变化」时，设备根目录上的标识文件永远停在第一次写入的内容。
    /// </para>
    /// </summary>
    [Fact]
    public void Save_OverwritesPreviousContent_EveryTime()
    {
        string root = SyncFixture.NewTempDirectory("device-mappings");
        Directory.CreateDirectory(root);

        Assert.True(DeviceMappingStore.Save(root, FileWith([@"C:\工作"], "备份/2026")));
        Assert.True(DeviceMappingStore.Save(root, FileWith([@"C:\工作", @"D:\照片"], "备份/2026")));
        Assert.True(DeviceMappingStore.Save(root, FileWith([@"D:\照片"], "照片")));

        DeviceMappingFile? reloaded = DeviceMappingStore.Load(root);

        Assert.NotNull(reloaded);
        MappingSetting mapping = Assert.Single(reloaded!.Mappings);
        Assert.Equal(@"D:\照片", mapping.LocalRoot);
        Assert.Equal("照片", mapping.DeviceFolder);

        // 覆盖写之后仍然是隐藏文件。
        FileAttributes attributes = File.GetAttributes(DeviceMappingStore.FilePathFor(root));
        Assert.True((attributes & FileAttributes.Hidden) != 0);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        string root = SyncFixture.NewTempDirectory("device-mappings-missing");
        Directory.CreateDirectory(root);

        Assert.False(DeviceMappingStore.Exists(root));
        Assert.Null(DeviceMappingStore.Load(root));
    }

    [Fact]
    public void RoundTrip_KeepsDeviceIdentityAndMappings()
    {
        string root = SyncFixture.NewTempDirectory("device-mappings-roundtrip");
        Directory.CreateDirectory(root);

        Assert.True(DeviceMappingStore.Save(root, FileWith([@"C:\工作"], "备份/2026")));

        DeviceMappingFile? reloaded = DeviceMappingStore.Load(root);

        Assert.NotNull(reloaded);
        Assert.Equal("device-identity", reloaded!.DeviceIdentity);
        Assert.Equal(@"E:\ KINGSTON", reloaded.DeviceName);
        Assert.Equal("备份/2026", Assert.Single(reloaded.Mappings).DeviceFolder);
    }

    private static DeviceMappingFile FileWith(IReadOnlyList<string> localRoots, string deviceFolder) => new()
    {
        DeviceIdentity = "device-identity",
        DeviceName = @"E:\ KINGSTON",
        SavedAt = DateTime.Now.ToString("s"),
        Mappings =
        [
            .. localRoots.Select(root => new MappingSetting { LocalRoot = root, DeviceFolder = deviceFolder }),
        ],
    };
}
