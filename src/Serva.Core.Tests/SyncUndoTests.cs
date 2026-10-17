using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>端到端：跑一次真实备份后再撤销，验证设备回到运行前的状态。</summary>
public class SyncUndoTests
{
    private static UndoResult UndoFrom(string journalPath, Journal? undoJournal = null)
    {
        IReadOnlyList<JournalEntry> entries = Journal.Read(journalPath);
        UndoPlan plan = JournalUndoer.BuildPlan(entries);
        return JournalUndoer.Execute(plan, undoJournal);
    }

    [Fact]
    public async Task Undo_RemovesFilesCreatedByBackup()
    {
        using var left = new TestTree("undo-run-left");
        using var right = new TestTree("undo-run-right");

        left.Add("new.txt", "NEW", CompareFixture.Base.AddDays(1));
        left.Add("docs/deep.txt", "DEEP", CompareFixture.Base.AddDays(1));

        SyncRunResult run = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.Equal(2, run.Completed);
        Assert.True(right.Exists("new.txt"));

        UndoResult undo = UndoFrom(run.JournalPath);

        Assert.Equal(2, undo.Deleted);
        Assert.False(right.Exists("new.txt"));
        Assert.False(right.Exists("docs/deep.txt"));
    }

    [Fact]
    public async Task Undo_KeepsRecycledFileAndOverwrittenContent()
    {
        using var left = new TestTree("undo-mirror-left");
        using var right = new TestTree("undo-mirror-right");

        left.Add("a.txt", "LOCAL-NEW", CompareFixture.Base.AddDays(1));
        right.Add("a.txt", "DEVICE-OLD", CompareFixture.Base);
        right.Add("extra.txt", "EXTRA", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.MirrorDelete = true;

        SyncRunResult run = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, SyncFixture.NewTempDirectory("journal"));

        Assert.False(right.Exists("extra.txt"));

        UndoResult undo = UndoFrom(run.JournalPath);

        // 删除的文件在系统回收站里（由用户在回收站还原），撤销不自动还原；
        // 被覆盖的 a.txt 因原始内容已丢失同样不自动还原。
        Assert.Equal(0, undo.Restored);
        Assert.False(right.Exists("extra.txt"));
        Assert.Equal("LOCAL-NEW", right.Read("a.txt"));
        Assert.Contains(undo.Messages, m => m.Contains("Recycle Bin"));
        Assert.Contains(undo.Messages, m => m.Contains("overwritten"));
    }

    [Fact]
    public async Task Undo_KeepBoth_RestoresDeviceVersionToOriginalName()
    {
        using var left = new TestTree("undo-both-left");
        using var right = new TestTree("undo-both-right");

        left.Add("a.txt", "LOCAL-VERSION", CompareFixture.Base);
        right.Add("a.txt", "DEVICE", CompareFixture.Base);

        SyncOptions options = SyncFixture.Options();
        options.ConflictResolution = ConflictResolution.KeepBoth;

        SyncRunResult run = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, options, SyncFixture.NewTempDirectory("journal"));

        Assert.Equal("LOCAL-VERSION", right.Read("a.txt"));
        Assert.Equal("DEVICE", right.Read("a（设备版本）.txt"));

        UndoResult undo = UndoFrom(run.JournalPath);

        Assert.Equal(1, undo.Deleted);
        Assert.Equal(1, undo.Renamed);
        Assert.Equal("DEVICE", right.Read("a.txt"));
        Assert.False(right.Exists("a（设备版本）.txt"));
    }

    [Fact]
    public async Task Undo_DeviceToLocal_RemovesPulledFiles()
    {
        using var left = new TestTree("undo-pull-left");
        using var right = new TestTree("undo-pull-right");
        right.Add("from-device.txt", "DEVICE");

        SyncRunResult run = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.DeviceToLocal, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        Assert.True(left.Exists("from-device.txt"));

        UndoResult undo = UndoFrom(run.JournalPath);

        Assert.Equal(1, undo.Deleted);
        Assert.False(left.Exists("from-device.txt"));
    }
}
