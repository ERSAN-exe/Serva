using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class PlanBuilderConflictTests
{
    private readonly PlanBuilder _builder = new();

    [Fact]
    public void CompareOnly_PlansNothing()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("a.txt", CompareFixture.Left("a.txt"), CompareFixture.Right("a.txt"), CompareStatus.LeftNewer),
            ("b.txt", CompareFixture.Left("b.txt"), null, CompareStatus.LeftOnly));

        SyncPlan plan = _builder.Build(compare, SyncMode.CompareOnly, new SyncOptions());

        Assert.Equal(0, plan.ActionableCount);
        Assert.Equal(0, plan.SelectedWriteBytes);
    }

    [Fact]
    public void Conflict_KeepBoth_RenamesTargetVersion()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("源码/serva/main.cs", CompareFixture.Left("源码/serva/main.cs", 5), CompareFixture.Right("源码/serva/main.cs", 6), CompareStatus.Conflict));

        SyncPlan plan = _builder.Build(
            compare,
            SyncMode.BackupToDevice,
            new SyncOptions { ConflictResolution = ConflictResolution.KeepBoth });

        PlanItem item = plan.Items.Single();
        Assert.Equal(PlanAction.CopyToDevice, item.Action);
        Assert.Equal("源码/serva/main（设备版本）.cs", item.PreserveTargetAs);
        Assert.Equal(CompareStatus.Conflict, item.DisplayStatus);
    }

    [Fact]
    public void Conflict_KeepLeft_OnlyWritesToDevice()
    {
        var options = new SyncOptions { ConflictResolution = ConflictResolution.KeepLeft };
        CompareResult compare = CompareFixture.BuildCompare(
            ("a.txt", CompareFixture.Left("a.txt", 5), CompareFixture.Right("a.txt", 6), CompareStatus.Conflict));

        Assert.Equal(PlanAction.CopyToDevice, _builder.Build(compare, SyncMode.BackupToDevice, options).Items.Single().Action);

        // 设备 → 本机是「强制覆盖」模式，冲突也用设备版本覆盖本机。
        Assert.Equal(PlanAction.CopyToLocal, _builder.Build(compare, SyncMode.DeviceToLocal, options).Items.Single().Action);
    }

    [Fact]
    public void Conflict_Skip_LeavesItemUntouched()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("a.txt", CompareFixture.Left("a.txt", 5), CompareFixture.Right("a.txt", 6), CompareStatus.Conflict));

        SyncPlan plan = _builder.Build(
            compare,
            SyncMode.BackupToDevice,
            new SyncOptions { ConflictResolution = ConflictResolution.Skip });

        Assert.Equal(PlanAction.Conflict, plan.Items.Single().Action);
        Assert.False(plan.Items.Single().IsActionable);
    }

    [Fact]
    public void UnselectedItemsAreExcludedFromTotals()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("a.txt", CompareFixture.Left("a.txt", 100), null, CompareStatus.LeftOnly),
            ("b.txt", CompareFixture.Left("b.txt", 200), null, CompareStatus.LeftOnly));

        SyncPlan plan = _builder.Build(compare, SyncMode.BackupToDevice, new SyncOptions());
        plan.Items.Single(i => i.RelativePath == "b.txt").IsSelected = false;

        Assert.Equal(2, plan.ActionableCount);
        Assert.Equal(1, plan.SelectedCount);
        Assert.Equal(100, plan.SelectedWriteBytes);
    }

    [Fact]
    public void CountsByDisplayStatus_AreAggregated()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("same.txt", CompareFixture.Left("same.txt"), CompareFixture.Right("same.txt"), CompareStatus.Same),
            ("new.txt", CompareFixture.Left("new.txt"), null, CompareStatus.LeftOnly),
            ("newer.txt", CompareFixture.Left("newer.txt"), CompareFixture.Right("newer.txt"), CompareStatus.LeftNewer),
            ("conflict.txt", CompareFixture.Left("conflict.txt", 5), CompareFixture.Right("conflict.txt", 6), CompareStatus.Conflict));

        SyncPlan plan = _builder.Build(
            compare,
            SyncMode.BackupToDevice,
            new SyncOptions { MirrorDelete = true });

        Assert.Equal(4, plan.TotalCount);
        Assert.Equal(1, plan.CountOf(CompareStatus.Same));
        Assert.Equal(1, plan.CountOf(CompareStatus.LeftOnly));
        Assert.Equal(1, plan.CountOf(CompareStatus.LeftNewer));
        Assert.Equal(1, plan.CountOf(CompareStatus.Conflict));
    }
}
