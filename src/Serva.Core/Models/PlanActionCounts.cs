namespace Serva.Core.Models;

/// <summary>
/// 执行计划的动作统计（「开始执行」前的确认弹窗用）。
/// <para>
/// <see cref="Total"/> 是全部比较项；其余各项只统计<strong>已勾选且可执行</strong>的项。
/// 未勾选项、相同项与「等待选择」的冲突项都归入 <see cref="Skip"/>，
/// 因此后六项相加恰好等于 <see cref="Total"/>。
/// </para>
/// </summary>
public sealed record PlanActionCounts(
    int Total,
    int CopyToLocal,
    int CopyToDevice,
    int OverwriteOnDevice,
    int OverwriteOnLocal,
    int Delete,
    int Skip)
{
    public static PlanActionCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// 真正会被操作的文件数（= 总数 − 「不会执行操作」）。
    /// 执行前确认里的「将对 N 个文件进行操作」用的是它：未勾选 / 相同 / 等待选择的文件不该算进去。
    /// </summary>
    public int Operated => Math.Max(0, Total - Skip);

    public static PlanActionCounts From(SyncPlan? plan)
    {
        if (plan is null)
        {
            return Empty;
        }

        int toLocal = 0;
        int toDevice = 0;
        int overwriteOnDevice = 0;
        int overwriteOnLocal = 0;
        int delete = 0;

        foreach (PlanItem item in plan.Items)
        {
            if (!item.IsSelected || !item.IsActionable)
            {
                continue;
            }

            switch (item.Action)
            {
                // 目标端本来没有这个文件 = 复制；已存在 = 覆盖。
                case PlanAction.CopyToDevice when item.Item.Status is CompareStatus.LeftOnly:
                    toDevice++;
                    break;

                case PlanAction.CopyToDevice:
                    overwriteOnDevice++;
                    break;

                case PlanAction.CopyToLocal when item.Item.Status is CompareStatus.RightOnly:
                    toLocal++;
                    break;

                case PlanAction.CopyToLocal:
                    overwriteOnLocal++;
                    break;

                case PlanAction.DeleteOnDevice:
                case PlanAction.DeleteOnLocal:
                    delete++;
                    break;
            }
        }

        int handled = toLocal + toDevice + overwriteOnDevice + overwriteOnLocal + delete;

        return new PlanActionCounts(
            plan.TotalCount,
            toLocal,
            toDevice,
            overwriteOnDevice,
            overwriteOnLocal,
            delete,
            Math.Max(0, plan.TotalCount - handled));
    }
}
