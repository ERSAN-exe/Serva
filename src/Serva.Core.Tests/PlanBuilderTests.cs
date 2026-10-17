using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class PlanBuilderTests
{
    private readonly PlanBuilder _builder = new();

    [Fact]
    public void BackupToDevice_CopiesNewAndNewerAndSkipsDeviceNewer()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("new.txt", CompareFixture.Left("new.txt"), null, CompareStatus.LeftOnly),
            ("newer.txt", CompareFixture.Left("newer.txt"), CompareFixture.Right("newer.txt"), CompareStatus.LeftNewer),
            ("older.txt", CompareFixture.Left("older.txt"), CompareFixture.Right("older.txt"), CompareStatus.RightNewer),
            ("device-only.txt", null, CompareFixture.Right("device-only.txt"), CompareStatus.RightOnly),
            ("same.txt", CompareFixture.Left("same.txt"), CompareFixture.Right("same.txt"), CompareStatus.Same));

        SyncPlan plan = _builder.Build(compare, SyncMode.BackupToDevice, new SyncOptions());

        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "new.txt").Action);
        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "newer.txt").Action);
        Assert.Equal(PlanAction.None, plan.Items.Single(i => i.RelativePath == "older.txt").Action);
        Assert.Equal(PlanAction.None, plan.Items.Single(i => i.RelativePath == "device-only.txt").Action);
        Assert.Equal(PlanAction.None, plan.Items.Single(i => i.RelativePath == "same.txt").Action);
        Assert.Equal(2, plan.ActionableCount);
        Assert.Equal(20, plan.SelectedWriteBytes);
    }

    [Fact]
    public void BackupToDevice_WithMirrorDelete_MarksDeviceExtrasAsDelete()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("extra.txt", null, CompareFixture.Right("extra.txt"), CompareStatus.RightOnly));

        SyncPlan plan = _builder.Build(
            compare,
            SyncMode.BackupToDevice,
            new SyncOptions { MirrorDelete = true });

        PlanItem item = plan.Items.Single();
        Assert.Equal(PlanAction.DeleteOnDevice, item.Action);
        Assert.Equal(CompareStatus.Delete, item.DisplayStatus);
        Assert.Equal(1, plan.CountOf(CompareStatus.Delete));
    }

    [Fact]
    public void DeviceToLocal_ForceOverwritesEverything()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("a.txt", CompareFixture.Left("a.txt"), CompareFixture.Right("a.txt"), CompareStatus.RightNewer),
            ("b.txt", CompareFixture.Left("b.txt"), CompareFixture.Right("b.txt"), CompareStatus.LeftNewer),
            ("c.txt", null, CompareFixture.Right("c.txt"), CompareStatus.RightOnly));

        SyncPlan plan = _builder.Build(compare, SyncMode.DeviceToLocal, new SyncOptions());

        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "a.txt").Action);

        // 设备 → 本机不看时间戳：本机较新的文件同样被设备版本强制覆盖。
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "b.txt").Action);
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "c.txt").Action);
    }

    [Fact]
    public void ForceOverwrite_SendsNewerDeviceFilesBackToo()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("device-newer.txt", CompareFixture.Left("device-newer.txt"), CompareFixture.Right("device-newer.txt"), CompareStatus.RightNewer),
            ("both-changed.txt", CompareFixture.Left("both-changed.txt", 5), CompareFixture.Right("both-changed.txt", 6), CompareStatus.Conflict),
            ("same.txt", CompareFixture.Left("same.txt"), CompareFixture.Right("same.txt"), CompareStatus.Same));

        SyncPlan forced = _builder.Build(compare, SyncMode.BackupToDevice, new SyncOptions(), forceOverwrite: true);

        Assert.Equal(PlanAction.CopyToDevice, forced.Items.Single(i => i.RelativePath == "device-newer.txt").Action);
        Assert.Equal(PlanAction.CopyToDevice, forced.Items.Single(i => i.RelativePath == "both-changed.txt").Action);
        Assert.Equal(PlanAction.None, forced.Items.Single(i => i.RelativePath == "same.txt").Action);

        // 不加 force 时，设备端较新的文件与冲突都不会被覆盖。
        SyncPlan merged = _builder.Build(compare, SyncMode.BackupToDevice, new SyncOptions());
        Assert.Equal(PlanAction.None, merged.Items.Single(i => i.RelativePath == "device-newer.txt").Action);
        Assert.Equal(PlanAction.CopyToDevice, merged.Items.Single(i => i.RelativePath == "both-changed.txt").Action);
        // 合并路径（默认冲突策略：两者都保留）会给目标端一个变体名；强制覆盖不会。
        Assert.NotNull(merged.Items.Single(i => i.RelativePath == "both-changed.txt").PreserveTargetAs);
        Assert.Null(forced.Items.Single(i => i.RelativePath == "both-changed.txt").PreserveTargetAs);
    }

    [Fact]
    public void TwoWay_CopiesInBothDirections()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("up.txt", CompareFixture.Left("up.txt"), CompareFixture.Right("up.txt"), CompareStatus.LeftNewer),
            ("down.txt", CompareFixture.Left("down.txt"), CompareFixture.Right("down.txt"), CompareStatus.RightNewer),
            ("new-local.txt", CompareFixture.Left("new-local.txt"), null, CompareStatus.LeftOnly),
            ("new-device.txt", null, CompareFixture.Right("new-device.txt"), CompareStatus.RightOnly));

        SyncPlan plan = _builder.Build(compare, SyncMode.TwoWay, new SyncOptions());

        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "up.txt").Action);
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "down.txt").Action);
        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "new-local.txt").Action);
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "new-device.txt").Action);
    }
}
