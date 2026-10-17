using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class SyncEngineTests
{
    [Fact]
    public async Task Backup_CopiesMissingAndNewerFiles()
    {
        using var left = new TestTree("backup-left");
        using var right = new TestTree("backup-right");

        left.Add("docs/a.txt", "A-NEW", CompareFixture.Base.AddDays(1));
        left.Add("docs/b.txt", "B1", CompareFixture.Base.AddDays(1));
        left.Add("sub/c.txt", "C1", CompareFixture.Base.AddDays(1));
        right.Add("docs/a.txt", "A-OLD", CompareFixture.Base);

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.False(result.Canceled);
        Assert.Equal(3, result.Completed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Failures);
        Assert.Equal("A-NEW", right.Read("docs/a.txt"));
        Assert.Equal("B1", right.Read("docs/b.txt"));
        Assert.Equal("C1", right.Read("sub/c.txt"));
    }

    [Fact]
    public async Task Backup_LeavesNoPartFilesBehind()
    {
        using var left = new TestTree("part-left");
        using var right = new TestTree("part-right");
        left.Add("big.txt", new string('x', 5000));

        _ = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.Empty(Directory.EnumerateFiles(right.Root, "*.serva-part", SearchOption.AllDirectories));
        Assert.Equal(new FileInfo(left.PathOf("big.txt")).Length, new FileInfo(right.PathOf("big.txt")).Length);
    }

    [Fact]
    public async Task Backup_PreservesTimestamps()
    {
        using var left = new TestTree("time-left");
        using var right = new TestTree("time-right");
        DateTime stamp = CompareFixture.Base.AddDays(-3);
        left.Add("a.txt", "A", stamp);

        _ = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(right.PathOf("a.txt")));
    }

    [Fact]
    public async Task DeviceToLocal_PullsFilesBack()
    {
        using var left = new TestTree("pull-left");
        using var right = new TestTree("pull-right");
        right.Add("from-device.txt", "DEVICE");

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.DeviceToLocal, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(1, result.Completed);
        Assert.Equal("DEVICE", left.Read("from-device.txt"));
    }

    [Fact]
    public async Task TwoWay_SyncsBothDirections()
    {
        using var left = new TestTree("two-left");
        using var right = new TestTree("two-right");
        left.Add("to-device.txt", "UP");
        right.Add("to-local.txt", "DOWN");

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.TwoWay, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(2, result.Completed);
        Assert.Equal("UP", right.Read("to-device.txt"));
        Assert.Equal("DOWN", left.Read("to-local.txt"));
    }

    [Fact]
    public async Task Journal_RecordsEveryOperation()
    {
        using var left = new TestTree("journal-left");
        using var right = new TestTree("journal-right");
        left.Add("a.txt", "A");
        left.Add("b.txt", "B");

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.True(File.Exists(result.JournalPath));
        IReadOnlyList<JournalEntry> entries = Journal.Read(result.JournalPath);

        Assert.Equal(2, entries.Count(e => e.Kind == JournalOperationKind.Copy));
        Assert.All(entries, e => Assert.True(e.Success));
    }
}
