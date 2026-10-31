using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>「执行前确认」弹窗里的动作统计（<see cref="PlanActionCounts"/>）。</summary>
public class PlanActionCountsTests
{
    private readonly PlanBuilder _builder = new();

    [Fact]
    public void Merge_SplitsNewCopiesFromOverwritesAndCountsUnchangedAsSkip()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("new-on-device.txt", CompareFixture.Left("new-on-device.txt"), null, CompareStatus.LeftOnly),
            ("new-on-local.txt", null, CompareFixture.Right("new-on-local.txt"), CompareStatus.RightOnly),
            ("overwrite-device.txt", CompareFixture.Left("overwrite-device.txt"), CompareFixture.Right("overwrite-device.txt"), CompareStatus.LeftNewer),
            ("overwrite-local.txt", CompareFixture.Left("overwrite-local.txt"), CompareFixture.Right("overwrite-local.txt"), CompareStatus.RightNewer),
            ("same.txt", CompareFixture.Left("same.txt"), CompareFixture.Right("same.txt"), CompareStatus.Same));

        SyncPlan plan = _builder.Build(compare, SyncMode.TwoWay, new SyncOptions());

        PlanActionCounts counts = PlanActionCounts.From(plan);

        Assert.Equal(5, counts.Total);

        // 目标端原本没有的文件算「复制」，两端都有的算「覆盖」。
        Assert.Equal(1, counts.CopyToDevice);
        Assert.Equal(1, counts.CopyToLocal);
        Assert.Equal(1, counts.OverwriteOnDevice);
        Assert.Equal(1, counts.OverwriteOnLocal);
        Assert.Equal(0, counts.Delete);

        // 相同的文件不会执行操作。
        Assert.Equal(1, counts.Skip);
        Assert.Equal(counts.Total, counts.CopyToDevice + counts.CopyToLocal + counts.OverwriteOnDevice + counts.OverwriteOnLocal + counts.Delete + counts.Skip);
    }

    [Fact]
    public void MirrorDelete_CountsDeletesAndUnselectedItemsAreSkipped()
    {
        CompareResult compare = CompareFixture.BuildCompare(
            ("extra-on-device.txt", null, CompareFixture.Right("extra-on-device.txt"), CompareStatus.RightOnly),
            ("new-on-device.txt", CompareFixture.Left("new-on-device.txt"), null, CompareStatus.LeftOnly));

        SyncPlan plan = _builder.Build(
            compare,
            SyncMode.BackupToDevice,
            new SyncOptions { MirrorDelete = true });

        // 取消勾选的项目不执行 —— 计入「不会执行操作」。
        plan.Items.Single(i => i.RelativePath == "new-on-device.txt").IsSelected = false;

        PlanActionCounts counts = PlanActionCounts.From(plan);

        Assert.Equal(2, counts.Total);
        Assert.Equal(1, counts.Delete);
        Assert.Equal(0, counts.CopyToDevice);
        Assert.Equal(1, counts.Skip);
    }

    [Fact]
    public void NoPlan_CountsNothing()
    {
        Assert.Equal(PlanActionCounts.Empty, PlanActionCounts.From(null));
    }
}
