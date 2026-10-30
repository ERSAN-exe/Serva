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
    public void ForceOverwrite_ExtraFilesFollowTheMirrorDeletePolicy()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("device-only.txt", null, CompareFixture.Right("device-only.txt"), CompareStatus.RightOnly),
            ("local-only.txt", CompareFixture.Left("local-only.txt"), null, CompareStatus.LeftOnly));

        // 执行策略没开「删除目标端多余文件」：覆盖模式也只复制，不删两边多出来的文件。
        SyncPlan quietToDevice = _builder.Build(compare, SyncMode.BackupToDevice, new SyncOptions(), forceOverwrite: true);
        Assert.Equal(PlanAction.None, quietToDevice.Items.Single(i => i.RelativePath == "device-only.txt").Action);

        SyncPlan quietToLocal = _builder.Build(compare, SyncMode.DeviceToLocal, new SyncOptions(), forceOverwrite: true);
        Assert.Equal(PlanAction.None, quietToLocal.Items.Single(i => i.RelativePath == "local-only.txt").Action);

        // 打开「删除目标端多余文件」：覆盖模式同样按执行策略镜像删除。
        var mirror = new SyncOptions { MirrorDelete = true };

        SyncPlan mirrorToDevice = _builder.Build(compare, SyncMode.BackupToDevice, mirror, forceOverwrite: true);
        Assert.Equal(PlanAction.DeleteOnDevice, mirrorToDevice.Items.Single(i => i.RelativePath == "device-only.txt").Action);

        SyncPlan mirrorToLocal = _builder.Build(compare, SyncMode.DeviceToLocal, mirror, forceOverwrite: true);
        Assert.Equal(PlanAction.DeleteOnLocal, mirrorToLocal.Items.Single(i => i.RelativePath == "local-only.txt").Action);
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
    public void TwoWay_NewerWinsBothWays_AndIgnoresMirrorDelete()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("local-only.txt", CompareFixture.Left("local-only.txt"), null, CompareStatus.LeftOnly),
            ("device-only.txt", null, CompareFixture.Right("device-only.txt"), CompareStatus.RightOnly),
            ("local-newer.txt", CompareFixture.Left("local-newer.txt"), CompareFixture.Right("local-newer.txt"), CompareStatus.LeftNewer),
            ("device-newer.txt", CompareFixture.Left("device-newer.txt"), CompareFixture.Right("device-newer.txt"), CompareStatus.RightNewer),
            ("both-changed.txt", CompareFixture.Left("both-changed.txt", 5), CompareFixture.Right("both-changed.txt", 6), CompareStatus.Conflict));

        // 执行策略开了「删除目标端多余文件」：合并同样忽略它，只补充、不删除。
        SyncPlan plan = _builder.Build(compare, SyncMode.TwoWay, new SyncOptions { MirrorDelete = true });

        // 比较新旧：谁新就用谁覆盖另一边。
        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "local-newer.txt").Action);
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "device-newer.txt").Action);

        // 互相补充新文件（两侧独有的都拷到对面）。
        Assert.Equal(PlanAction.CopyToDevice, plan.Items.Single(i => i.RelativePath == "local-only.txt").Action);
        Assert.Equal(PlanAction.CopyToLocal, plan.Items.Single(i => i.RelativePath == "device-only.txt").Action);

        // 一条删除都不产生：「删除目标端多余文件」对合并不生效。
        Assert.Equal(0, plan.CountOf(CompareStatus.Delete));
        Assert.DoesNotContain(plan.Items, static i => i.Action is PlanAction.DeleteOnDevice or PlanAction.DeleteOnLocal);

        // 两边都改过的文件：保留较新版本（本机那个比设备新 → 覆盖设备），不再生成「（设备版本）」副本。
        PlanItem conflict = plan.Items.Single(i => i.RelativePath == "both-changed.txt");
        Assert.Equal(PlanAction.CopyToDevice, conflict.Action);
        Assert.Null(conflict.PreserveTargetAs);
    }

    [Fact]
    public void TwoWay_Conflict_KeepsNewerVersion()
    {
        // 本机更新：用本机版本覆盖设备。
        CompareResult localNewer = CompareFixture.BuildCompare(
            ("a.txt",
             CompareFixture.Entry("a.txt", 10, CompareFixture.Base.AddDays(2)),
             CompareFixture.Entry("a.txt", 11, CompareFixture.Base),
             CompareStatus.Conflict));

        PlanItem toDevice = _builder.Build(localNewer, SyncMode.TwoWay, new SyncOptions()).Items.Single();
        Assert.Equal(PlanAction.CopyToDevice, toDevice.Action);
        Assert.Null(toDevice.PreserveTargetAs);

        // 设备更新：用设备版本覆盖本机。
        CompareResult deviceNewer = CompareFixture.BuildCompare(
            ("b.txt",
             CompareFixture.Entry("b.txt", 10, CompareFixture.Base),
             CompareFixture.Entry("b.txt", 11, CompareFixture.Base.AddDays(2)),
             CompareStatus.Conflict));

        PlanItem toLocal = _builder.Build(deviceNewer, SyncMode.TwoWay, new SyncOptions()).Items.Single();
        Assert.Equal(PlanAction.CopyToLocal, toLocal.Action);
        Assert.Null(toLocal.PreserveTargetAs);

        // 时间完全相同时无法判断：退回「两者都保留」（目标端旧版本改名）。
        CompareResult sameTime = CompareFixture.BuildCompare(
            ("c.txt",
             CompareFixture.Entry("c.txt", 10, CompareFixture.Base),
             CompareFixture.Entry("c.txt", 11, CompareFixture.Base),
             CompareStatus.Conflict));

        PlanItem keepBoth = _builder.Build(sameTime, SyncMode.TwoWay, new SyncOptions()).Items.Single();
        Assert.Equal(PlanAction.CopyToDevice, keepBoth.Action);
        Assert.NotNull(keepBoth.PreserveTargetAs);
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
