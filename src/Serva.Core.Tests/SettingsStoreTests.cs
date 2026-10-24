using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void RoundTrip_PersistsAllSections()
    {
        string directory = SyncFixture.NewTempDirectory("settings");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        var store = new SettingsStore(path);
        AppSettings settings = store.Load();
        settings.Theme = "dark";
        settings.Language = "zh-Hans";
        settings.Options.Concurrency = 6;
        settings.Options.MirrorDelete = true;
        settings.Options.UseTrashFolder = false;
        settings.Options.SkipHiddenAndSystem = true;
        settings.Options.ExcludePatterns = ["*.bak", "temp/**"];
        settings.Options.IncludePatterns = ["**/*.jpg"];
        settings.Options.ConflictResolution = ConflictResolution.KeepBoth;
        settings.Mappings = [new MappingSetting { LocalRoot = @"C:\工作", DeviceFolder = "备份/2026" }];
        settings.CheckUpdatesOnStartup = false;
        settings.LastUpdateCheckUtc = new DateTimeOffset(2026, 10, 17, 8, 30, 0, TimeSpan.Zero);
        settings.DismissedUpdateVersion = "0.2.0";
        settings.PinnedFolders = [@"C:\Pictures", @"D:\Cards"];
        store.Save(settings);

        AppSettings reloaded = new SettingsStore(path).Load();

        Assert.Equal("dark", reloaded.Theme);
        Assert.Equal("zh-Hans", reloaded.Language);
        Assert.Equal(6, reloaded.Options.Concurrency);
        Assert.True(reloaded.Options.MirrorDelete);
        Assert.False(reloaded.Options.UseTrashFolder);
        Assert.True(reloaded.Options.SkipHiddenAndSystem);
        Assert.Equal(["*.bak", "temp/**"], reloaded.Options.ExcludePatterns);
        Assert.Equal(["**/*.jpg"], reloaded.Options.IncludePatterns);
        Assert.Equal(ConflictResolution.KeepBoth, reloaded.Options.ConflictResolution);
        Assert.False(reloaded.CheckUpdatesOnStartup);
        Assert.Equal(settings.LastUpdateCheckUtc, reloaded.LastUpdateCheckUtc);
        Assert.Equal("0.2.0", reloaded.DismissedUpdateVersion);
        Assert.Equal([@"C:\Pictures", @"D:\Cards"], reloaded.PinnedFolders);

        MappingSetting mapping = Assert.Single(reloaded.Mappings);
        Assert.Equal(@"C:\工作", mapping.LocalRoot);
        Assert.Equal("备份/2026", mapping.DeviceFolder);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        string path = Path.Combine(SyncFixture.NewTempDirectory("settings-missing"), "settings.json");

        AppSettings settings = new SettingsStore(path).Load();

        Assert.Equal("system", settings.Theme);
        Assert.Equal("system", settings.Language);   // 默认语言：跟随系统
        Assert.False(settings.AllowLocalDrives);     // 默认不允许选本机硬盘
        Assert.Equal(SyncOptions.DefaultConcurrency, settings.Options.Concurrency);
        Assert.Empty(settings.Mappings);
        Assert.Equal(ExcludeMatcher.DefaultExcludePatterns.Count, settings.Options.ExcludePatterns.Count);
        Assert.True(settings.CheckUpdatesOnStartup);   // 默认启动时检查更新
        Assert.Null(settings.LastUpdateCheckUtc);
        Assert.Null(settings.DismissedUpdateVersion);
        Assert.Empty(settings.PinnedFolders);
    }

    [Fact]
    public void CorruptedFile_FallsBackToDefaults()
    {
        string directory = SyncFixture.NewTempDirectory("settings-broken");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        AppSettings settings = new SettingsStore(path).Load();

        Assert.Equal("system", settings.Theme);
        Assert.Equal(SyncOptions.DefaultConcurrency, settings.Options.Concurrency);
    }

    [Fact]
    public void Normalize_ResetsOptionsNoLongerOfferedInUi()
    {
        var options = new SyncOptions
        {
            Concurrency = 1,
            ConflictResolution = ConflictResolution.KeepLeft,
        };

        options.Normalize();

        Assert.Equal(SyncOptions.DefaultConcurrency, options.Concurrency);
        Assert.Equal(ConflictResolution.KeepBoth, options.ConflictResolution);
    }

    [Fact]
    public void ExportImportAndReset_Work()
    {
        string directory = SyncFixture.NewTempDirectory("settings-io");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        string export = Path.Combine(directory, "exported.json");

        var store = new SettingsStore(path);
        AppSettings settings = store.Load();
        settings.Theme = "light";
        settings.Options.Concurrency = 7;
        store.Save(settings);
        store.Export(export, settings);

        AppSettings reset = store.Reset();
        Assert.Equal("system", reset.Theme);
        Assert.False(File.Exists(path));

        AppSettings imported = store.Import(export);
        Assert.Equal("light", imported.Theme);
        Assert.Equal(7, imported.Options.Concurrency);
        Assert.Equal("light", new SettingsStore(path).Load().Theme);
    }

    [Fact]
    public void Clone_DeepCopiesMutableSections()
    {
        var settings = new AppSettings();
        AppSettings clone = settings.Clone();

        clone.Options.ExcludePatterns.Add("added");
        clone.Mappings.Add(new MappingSetting { LocalRoot = @"D:\x" });

        Assert.DoesNotContain("added", settings.Options.ExcludePatterns);
        Assert.Empty(settings.Mappings);
    }

    [Fact]
    public void StorageLocations_PointIntoLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, StorageLocations.SettingsFile);
        Assert.StartsWith(local, StorageLocations.StateDirectory);
        Assert.StartsWith(local, StorageLocations.JournalDirectory);
        Assert.Contains("Serva", StorageLocations.RootDirectory);
    }
}
