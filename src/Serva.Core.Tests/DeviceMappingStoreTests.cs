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

    /// <summary>同一个设备在不同电脑上各用各的对应关系：保存本机那一段时不能覆盖别的电脑的记录。</summary>
    [Fact]
    public void Save_KeepsOtherComputersSections()
    {
        string root = SyncFixture.NewTempDirectory("device-mappings-multi-pc");
        Directory.CreateDirectory(root);

        Assert.True(DeviceMappingStore.Save(root, FileWith([@"C:\工作"], "备份/2026")));

        // 模拟「另一台电脑」也写过这个文件：直接改磁盘上的文件（先摘掉隐藏属性）。
        const string foreignId = "another-computer";
        string path = DeviceMappingStore.FilePathFor(root);
        DeviceMappingFile? onDisk = DeviceMappingStore.Load(root);
        Assert.NotNull(onDisk);

        onDisk!.Computers.Add(new ComputerMappingSection
        {
            ComputerId = foreignId,
            ComputerName = "OTHER-PC",
            SavedAt = DateTime.Now.ToString("s"),
            Mappings = [new MappingSetting { LocalRoot = @"D:\别的电脑", DeviceFolder = "别的目录" }],
        });

        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(onDisk, JsonOptions));

        // 本机再保存一次：不能把另一台电脑的那一段覆盖掉。
        Assert.True(DeviceMappingStore.Save(root, FileWith([@"C:\工作", @"D:\照片"], "备份/2026")));

        DeviceMappingFile? reloaded = DeviceMappingStore.Load(root);
        Assert.NotNull(reloaded);

        // 本机只看到自己的那一段。
        IReadOnlyList<MappingSetting> mine = DeviceMappingStore.MappingsForCurrentComputer(reloaded!);
        Assert.Equal(2, mine.Count);
        Assert.Equal(@"D:\照片", mine[1].LocalRoot);

        // 另一台电脑的记录原样保留。
        ComputerMappingSection other = Assert.Single(reloaded!.Computers, c => c.ComputerId == foreignId);
        Assert.Equal(@"D:\别的电脑", Assert.Single(other.Mappings).LocalRoot);
        Assert.Equal(2, reloaded.Computers.Count);
    }

    /// <summary>旧格式（只有顶层 Mappings、没有分段）仍能读出本机的对应关系。</summary>
    [Fact]
    public void LegacyFile_FallsBackToTopLevelMappings()
    {
        var legacy = new DeviceMappingFile
        {
            DeviceIdentity = "device-identity",
            DeviceName = @"E:\ KINGSTON",
            Mappings = [new MappingSetting { LocalRoot = @"C:\旧", DeviceFolder = "old" }],
        };

        MappingSetting mapping = Assert.Single(DeviceMappingStore.MappingsForCurrentComputer(legacy));
        Assert.Equal(@"C:\旧", mapping.LocalRoot);
    }
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()    {        WriteIndented = true,        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,    };    private static DeviceMappingFile FileWith(IReadOnlyList<string> localRoots, string deviceFolder) => new()
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
