using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>撤销计划构建（不落盘）。</summary>
public class JournalUndoerTests
{
    private static JournalEntry Entry(
        JournalOperationKind kind,
        string relativePath,
        string source,
        string target,
        long bytes = 10,
        bool? overwritten = null,
        bool success = true) =>
        new(DateTime.UtcNow, kind, relativePath, source, target, bytes, success, null) { Overwritten = overwritten };

    [Fact]
    public void NewCopy_IsUndoableByDeletingCreatedFile()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", @"C:\src\a.txt", @"E:\a.txt", overwritten: false),
        ]);

        Assert.Equal(UndoActionKind.DeleteCreatedFile, Assert.Single(plan.Steps).Action);
        Assert.True(plan.CanUndo);
    }

    [Fact]
    public void OverwritingCopy_IsNotAutomaticallyUndoable()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", @"C:\src\a.txt", @"E:\a.txt", overwritten: true),
        ]);

        Assert.Equal(UndoActionKind.NotPossible, Assert.Single(plan.Steps).Action);
        Assert.False(plan.CanUndo);
    }

    [Fact]
    public void UnknownOverwrittenFlag_IsTreatedAsUnsafe()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", @"C:\src\a.txt", @"E:\a.txt", overwritten: null),
        ]);

        Assert.Equal(UndoActionKind.NotPossible, Assert.Single(plan.Steps).Action);
    }

    [Fact]
    public void PermanentDelete_IsNotUndoable()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Delete, "gone.txt", @"E:\gone.txt", string.Empty),
        ]);

        Assert.Equal(UndoActionKind.NotPossible, Assert.Single(plan.Steps).Action);
    }

    [Fact]
    public void FailedOperationsAreIgnored()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", @"C:\src\a.txt", @"E:\a.txt", overwritten: false, success: false),
        ]);

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void StepsAreProducedInReverseOrder()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "first.txt", @"C:\src\first.txt", @"E:\first.txt", overwritten: false),
            Entry(JournalOperationKind.Copy, "second.txt", @"C:\src\second.txt", @"E:\second.txt", overwritten: false),
        ]);

        Assert.Equal("second.txt", plan.Steps[0].RelativePath);
        Assert.Equal("first.txt", plan.Steps[1].RelativePath);
    }

    [Fact]
    public void KeepBothCombination_DeletesCopyThenRestoresOriginalName()
    {
        // 「两者都保留」先改名再写入，撤销时必须逆序执行。
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Rename, "a.txt", @"E:\a.txt", @"E:\a（设备版本）.txt", bytes: 0),
            Entry(JournalOperationKind.Copy, "a.txt", @"C:\src\a.txt", @"E:\a.txt", overwritten: false),
        ]);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(UndoActionKind.DeleteCreatedFile, plan.Steps[0].Action);
        Assert.Equal(UndoActionKind.RenameBack, plan.Steps[1].Action);
        Assert.Equal(@"E:\a.txt", plan.Steps[1].SourcePath);
        Assert.Equal(@"E:\a（设备版本）.txt", plan.Steps[1].TargetPath);
        Assert.Equal(2, plan.ExecutableCount);
    }

    [Fact]
    public void Summary_DescribesCounts()
    {
        UndoPlan plan = JournalUndoer.BuildPlan([
            Entry(JournalOperationKind.Copy, "a.txt", "s", "t", overwritten: false),
            Entry(JournalOperationKind.Delete, "b.txt", "s", "t"),
        ]);

        Assert.Equal(2, plan.TotalCount);
        Assert.Equal(1, plan.ExecutableCount);
        Assert.Equal(1, plan.NotPossibleCount);
        Assert.Contains("1 item(s) can be undone", plan.Summary);
    }
}
