using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>撤销计划的执行（真实文件系统）。</summary>
public class JournalUndoerExecuteTests
{
    private static JournalEntry Entry(
        JournalOperationKind kind,
        string relativePath,
        string source,
        string target,
        long bytes = 10,
        bool? overwritten = null) =>
        new(DateTime.UtcNow, kind, relativePath, source, target, bytes, true, null) { Overwritten = overwritten };

    [Fact]
    public void Execute_DeletesCreatedFile()
    {
        using var target = new TestTree("undo-delete");
        string file = target.Add("a.txt", "HELLO");

        UndoResult result = JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", "src", file, bytes: 5, overwritten: false),
        ]));

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Execute_DeletesDirectoriesCreatedByTheRun()
    {
        using var target = new TestTree("undo-created-dirs");
        string file = target.Add("new/nested/a.txt", "HELLO");
        string inner = target.PathOf("new/nested");
        string outer = target.PathOf("new");

        UndoResult result = JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "new/nested/a.txt", "src", file, bytes: 5, overwritten: false)
                with { CreatedDirectories = [outer, inner] },
        ]));

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(inner));   // 本次运行新建的空目录一并删除
        Assert.False(Directory.Exists(outer));
        Assert.True(Directory.Exists(target.Root));
    }

    [Fact]
    public void Execute_KeepsCreatedDirectoryThatStillHasOtherFiles()
    {
        using var target = new TestTree("undo-created-dirs-kept");
        string file = target.Add("shared/a.txt", "HELLO");
        target.Add("shared/keep.txt", "OTHER");
        string directory = target.PathOf("shared");

        JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "shared/a.txt", "src", file, bytes: 5, overwritten: false)
                with { CreatedDirectories = [directory] },
        ]));

        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(directory));   // 目录里还有别的文件 → 保留
        Assert.True(File.Exists(target.PathOf("shared/keep.txt")));
    }
    [Fact]
    public void Execute_SkipsFileModifiedAfterTheRun()
    {
        using var target = new TestTree("undo-modified");
        string file = target.Add("a.txt", "CHANGED-CONTENT");

        // 日志记录的大小与实际不符 → 文件在运行后又被改过，撤销必须跳过。
        UndoResult result = JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", "src", file, bytes: 5, overwritten: false),
        ]));

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(file));
        Assert.Single(result.Messages);
    }

    [Fact]
    public void Execute_RestoresFileFromTrash()
    {
        using var root = new TestTree("undo-trash");
        string original = root.PathOf("docs/a.txt");
        string trashed = root.Add("_ServaTrash/20261011/docs/a.txt", "CONTENT");

        UndoResult result = JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.MoveToTrash, "docs/a.txt", original, trashed, bytes: 7),
        ]));

        Assert.Equal(1, result.Restored);
        Assert.Equal("CONTENT", File.ReadAllText(original));
        Assert.False(File.Exists(trashed));
    }

    [Fact]
    public void Execute_RenamesBackKeepBothVariant()
    {
        using var root = new TestTree("undo-keepboth");
        string original = root.Add("a.txt", "LOCAL");
        string variant = root.Add("a（设备版本）.txt", "DEVICE");

        UndoResult result = JournalUndoer.Execute(JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Rename, "a.txt", original, variant, bytes: 0),
            Entry(JournalOperationKind.Copy, "a.txt", "src", original, bytes: 5, overwritten: false),
        ]));

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Renamed);
        Assert.Equal("DEVICE", File.ReadAllText(original));
        Assert.False(File.Exists(variant));
    }

    [Fact]
    public void Execute_WritesUndoEntriesIntoNewJournal()
    {
        using var root = new TestTree("undo-journal");
        string file = root.Add("a.txt", "HELLO");
        var undoJournal = new Journal(root.PathOf("journal"));

        UndoResult result = JournalUndoer.Execute(
            JournalUndoer.BuildPlan([
                Entry(JournalOperationKind.Copy, "a.txt", "src", file, bytes: 5, overwritten: false),
            ]),
            undoJournal);

        Assert.Equal(1, result.Deleted);

        IReadOnlyList<JournalEntry> entries = Journal.Read(undoJournal.FilePath);
        Assert.Equal(JournalOperationKind.Undo, Assert.Single(entries).Kind);
    }

    [Fact]
    public void Summarize_CountsOperationsFailuresAndUndoableSteps()
    {
        using var tree = new TestTree("undo-summary");
        var journal = new Journal(tree.Root);
        string target = tree.Add("a.txt", "HELLO");

        journal.Append(JournalOperationKind.Copy, "a.txt", "src", target, 5, true, null, overwritten: false);
        journal.Append(JournalOperationKind.Error, "b.txt", "src", target, 0, false, "拒绝访问");

        JournalSummary summary = Journal.Summarize(journal.FilePath);

        Assert.Equal(2, summary.OperationCount);
        Assert.Equal(1, summary.FailureCount);
        Assert.Equal(1, summary.UndoableCount);
        Assert.Contains("1 failed", summary.Caption);
    }
}
