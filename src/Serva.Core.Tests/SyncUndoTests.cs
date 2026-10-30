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

        // 关闭「保留临时副本」：覆盖就是直接盖掉，撤销无法还原被覆盖的内容（本用例验证的正是这条路）。
        options.KeepOverwriteBackup = false;

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
    public async Task Undo_RestoresOverwrittenOriginalFromBackup()
    {
        using var left = new TestTree("undo-backup-left");
        using var right = new TestTree("undo-backup-right");

        left.Add("a.txt", "LOCAL-NEW", CompareFixture.Base.AddDays(1));
        right.Add("a.txt", "DEVICE-OLD", CompareFixture.Base);

        // 默认开启「允许覆盖后保留临时副本用于撤销」。
        SyncRunResult run = await SyncFixture.RunAsync(
            left.Root, right.Root, SyncMode.BackupToDevice, SyncFixture.Options(), SyncFixture.NewTempDirectory("journal"));

        // 覆盖后设备上是新内容，原件被挪进同目录的隐藏目录 .ServaUndoTemp。
        Assert.Equal("LOCAL-NEW", right.Read("a.txt"));
        Assert.True(Directory.Exists(Path.Combine(right.Root, OverwriteBackup.FolderName)));
        Assert.True(right.Exists($"{OverwriteBackup.FolderName}/a.txt"));
        Assert.Equal("DEVICE-OLD", right.Read($"{OverwriteBackup.FolderName}/a.txt"));

        UndoResult undo = UndoFrom(run.JournalPath);

        // 撤销：删掉本次写入的文件，再把原件还原回原位。
        Assert.Equal(1, undo.Restored);
        Assert.Equal("DEVICE-OLD", right.Read("a.txt"));

        // 还原出来的文件不能是隐藏的；临时目录也应该已经被删掉。
        Assert.True((File.GetAttributes(right.PathOf("a.txt")) & FileAttributes.Hidden) == 0);
        Assert.False(right.Exists($"{OverwriteBackup.FolderName}/a.txt"));
        Assert.False(Directory.Exists(Path.Combine(right.Root, OverwriteBackup.FolderName)));
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
