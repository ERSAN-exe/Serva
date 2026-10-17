using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class SyncEngineBehaviorTests
{
    [Fact]
    public async Task MirrorDelete_SendsDeviceExtrasToRecycleBin()
    {
        using var left = new TestTree("mirror-left");
        using var right = new TestTree("mirror-right");
        left.Add("a.txt", "A", CompareFixture.Base);
        right.Add("a.txt", "A", CompareFixture.Base);
        right.Add("extra.txt", "EXTRA", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.MirrorDelete = true;

        string journalDirectory = SyncFixture.NewTempDirectory("journal");
        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, journalDirectory);

        Assert.Equal(0, result.Failed);
        Assert.False(right.Exists("extra.txt"));

        // 删除记录进日志，并注明是「移入系统回收站」（已放进回收站的文件不再可撤销）。
        JournalEntry entry = Assert.Single(
            Journal.Read(Directory.EnumerateFiles(journalDirectory, "*.jsonl").Single()));
        Assert.Equal(JournalOperationKind.Delete, entry.Kind);
        Assert.Equal("Moved to the Windows Recycle Bin", entry.Message);

        // 不再创建 _ServaTrash 目录。
        Assert.False(Directory.Exists(Path.Combine(right.Root, "_ServaTrash")));
    }

    [Fact]
    public async Task MirrorDelete_WithoutTrashDeletesPermanently()
    {
        using var left = new TestTree("mirror2-left");
        using var right = new TestTree("mirror2-right");
        left.Add("a.txt", "A", CompareFixture.Base);
        right.Add("a.txt", "A", CompareFixture.Base);
        right.Add("extra.txt", "EXTRA", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.MirrorDelete = true;
        options.UseTrashFolder = false;

        _ = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, SyncFixture.NewTempDirectory("journal"));

        Assert.False(right.Exists("extra.txt"));
        Assert.False(Directory.Exists(Path.Combine(right.Root, "_ServaTrash")));
    }

    [Fact]
    public async Task Conflict_KeepBoth_KeepsBothVersions()
    {
        using var left = new TestTree("both-left");
        using var right = new TestTree("both-right");

        // 同一时间窗、大小不同 → 判定为冲突（无法自动选边）。
        left.Add("a.txt", "LOCAL-VERSION", CompareFixture.Base);
        right.Add("a.txt", "DEVICE", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.ConflictResolution = ConflictResolution.KeepBoth;

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(0, result.Failed);
        Assert.Equal("LOCAL-VERSION", right.Read("a.txt"));
        Assert.Equal("DEVICE", right.Read("a（设备版本）.txt"));
    }

    [Fact]
    public async Task Conflict_Skip_LeavesDeviceUntouched()
    {
        using var left = new TestTree("skip-left");
        using var right = new TestTree("skip-right");
        left.Add("a.txt", "LOCAL", CompareFixture.Base);
        right.Add("a.txt", "DEVICE", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.ConflictResolution = ConflictResolution.Skip;

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(0, result.Completed);
        Assert.Equal("DEVICE", right.Read("a.txt"));
    }

    [Fact]
    public async Task CanceledToken_StopsImmediately()
    {
        using var left = new TestTree("cancel-left");
        using var right = new TestTree("cancel-right");
        left.Add("a.txt", "A");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        SyncRunResult result = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"), cts.Token);

        Assert.True(result.Canceled);
        Assert.Equal(0, result.Completed);
        Assert.False(right.Exists("a.txt"));
    }

    [Fact]
    public async Task ProgressReportsTotalsAndCompletion()
    {
        using var left = new TestTree("progress-left");
        using var right = new TestTree("progress-right");
        left.Add("a.txt", "AAAA", CompareFixture.Base.AddDays(1));
        left.Add("b.txt", "BBBB", CompareFixture.Base.AddDays(1));

        var scanner = new FileScanner(new ExcludeMatcher());
        CompareResult compare = new CompareEngine().Compare(scanner.Scan(left.Root), scanner.Scan(right.Root));
        SyncPlan plan = new PlanBuilder().Build(compare, SyncMode.BackupToDevice, SyncFixture.Options());

        var reports = new List<SyncProgress>();
        var progress = new Progress<SyncProgress>(reports.Add);

        SyncRunResult result = await new SyncEngine().ExecuteAsync(
            plan,
            SyncFixture.Options(),
            left.Root,
            right.Root,
            progress,
            SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(2, result.Completed);
        Assert.True(reports.Count > 0);
        Assert.Contains(reports, r => r.Total == 2);
    }

    [Fact]
    public void CheckSpace_ReportsShortfall()
    {
        using var left = new TestTree("space-left");
        using var right = new TestTree("space-right");
        left.Add("a.txt", new string('x', 64));

        var scanner = new FileScanner(new ExcludeMatcher());
        CompareResult compare = new CompareEngine().Compare(scanner.Scan(left.Root), scanner.Scan(right.Root));
        SyncPlan plan = new PlanBuilder().Build(compare, SyncMode.BackupToDevice, SyncFixture.Options());

        SpaceCheck check = SyncEngine.CheckSpace(plan, right.Root);

        Assert.Equal(64, check.RequiredBytes);
        Assert.True(check.FreeBytes > 0);
        Assert.True(check.Sufficient);
    }
}
