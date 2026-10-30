using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 计划生成器：把「比较结果 + 同步模式 + 执行策略」翻译成逐项可执行的 <see cref="SyncPlan"/>。
/// </summary>
public sealed class PlanBuilder
{
    public SyncPlan Build(CompareResult compare, SyncMode mode, SyncOptions options, bool forceOverwrite = false)
    {
        var items = new List<PlanItem>(compare.Items.Count);
        foreach (CompareItem item in compare.Items)
        {
            items.Add(Decide(item, mode, options, forceOverwrite));
        }

        return new SyncPlan(mode, items);
    }

    private static PlanItem Decide(CompareItem item, SyncMode mode, SyncOptions options, bool forceOverwrite)
    {
        if (mode == SyncMode.CompareOnly)
        {
            return new PlanItem(item, PlanAction.None, CoreText.Get("Compare only", "仅比较"));
        }

        // 合并（TwoWay）：两边互相补充 —— 谁新就用谁覆盖另一边；
        // 执行策略里的其余规则照常生效，只有「删除目标端多余文件」对合并不适用：
        // 合并只补充、绝不删除（见下面 LeftOnly / RightOnly 的分支）。
        return item.Status switch
        {
            CompareStatus.Same => new PlanItem(item, PlanAction.None, CoreText.Get("Identical", "相同")),

            CompareStatus.LeftOnly => mode switch
            {
                // 合并：本机有、设备没有 —— 补到设备上（不删除对面任何东西）。
                SyncMode.BackupToDevice or SyncMode.TwoWay => new PlanItem(item, PlanAction.CopyToDevice),

                // 目标端（本机）多余的文件删不删只由全局执行策略决定：与 forceOverwrite 无关，
                // 「覆盖本机硬盘」不会额外删除比设备多出来的文件。
                SyncMode.DeviceToLocal when options.MirrorDelete =>
                    new PlanItem(item, PlanAction.DeleteOnLocal, CoreText.Get("Mirror delete", "镜像删除")),
                _ => new PlanItem(item, PlanAction.None, CoreText.Get("No action", "不处理")),
            },

            CompareStatus.RightOnly => mode switch
            {
                // 合并：设备有、本机没有 —— 补到本机（同上，只补充、不删除）。
                SyncMode.DeviceToLocal or SyncMode.TwoWay => new PlanItem(item, PlanAction.CopyToLocal),

                // 同上：设备上的多余文件也只看执行策略里的「删除目标端多余文件」。
                SyncMode.BackupToDevice when options.MirrorDelete =>
                    new PlanItem(item, PlanAction.DeleteOnDevice, CoreText.Get("Mirror delete", "镜像删除")),
                _ => new PlanItem(item, PlanAction.None, CoreText.Get("No action", "不处理")),
            },

            CompareStatus.LeftNewer => mode switch
            {
                SyncMode.BackupToDevice or SyncMode.TwoWay => new PlanItem(item, PlanAction.CopyToDevice),

                // 设备 → 本机：以设备为准强制覆盖，不看时间戳。
                SyncMode.DeviceToLocal =>
                    new PlanItem(item, PlanAction.CopyToLocal, CoreText.Get("Force overwrite (device wins)", "强制覆盖（设备为准）")),
                _ => new PlanItem(item, PlanAction.None, CoreText.Get("Older on device, skipped", "设备端较旧，已跳过")),
            },

            CompareStatus.RightNewer => mode switch
            {
                SyncMode.DeviceToLocal or SyncMode.TwoWay => new PlanItem(item, PlanAction.CopyToLocal),

                // 强制覆盖：设备上较新的版本同样被本机版本覆盖。
                SyncMode.BackupToDevice when forceOverwrite =>
                    new PlanItem(item, PlanAction.CopyToDevice, CoreText.Get("Force overwrite (local wins)", "强制覆盖（本机为准）")),
                _ => new PlanItem(item, PlanAction.None, CoreText.Get("Older locally, skipped", "本机较旧，已跳过")),
            },

            CompareStatus.Conflict => DecideConflict(item, mode, options, forceOverwrite),

            _ => new PlanItem(item, PlanAction.None, CoreText.Get("No action", "不处理")),
        };
    }

    /// <summary>
    /// 冲突：两端都改过。合并时保留较新版本（谁的新就用谁覆盖另一边，时间相同才两者都保留）；
    /// 其他模式按用户选择的冲突策略处理。
    /// </summary>
    private static PlanItem DecideConflict(CompareItem item, SyncMode mode, SyncOptions options, bool forceOverwrite)
    {
        // 强制覆盖：整盘以源端为准，冲突直接覆盖（不重命名、不保留旧版本）。
        if (forceOverwrite || mode == SyncMode.DeviceToLocal)
        {
            return mode == SyncMode.DeviceToLocal
                ? new PlanItem(item, PlanAction.CopyToLocal, CoreText.Get("Conflict: force overwrite (device wins)", "冲突：强制覆盖（设备为准）"))
                : new PlanItem(item, PlanAction.CopyToDevice, CoreText.Get("Conflict: force overwrite (local wins)", "冲突：强制覆盖（本机为准）"));
        }

        // 合并（TwoWay）：两端都改过 —— 保留较新版本，谁新就用谁覆盖另一边；
        // 时间完全相同时无法判断，退回「两者都保留」（目标端旧版本改名保留）。
        if (mode == SyncMode.TwoWay
            && item.Left is { } left
            && item.Right is { } right
            && left.LastWriteTimeUtc != right.LastWriteTimeUtc)
        {
            return left.LastWriteTimeUtc > right.LastWriteTimeUtc
                ? new PlanItem(item, PlanAction.CopyToDevice, CoreText.Get("Conflict: keep newer (local)", "冲突：保留较新版本（本机）"))
                : new PlanItem(item, PlanAction.CopyToLocal, CoreText.Get("Conflict: keep newer (device)", "冲突：保留较新版本（设备）"));
        }

        bool deviceIsTarget = mode is SyncMode.BackupToDevice or SyncMode.TwoWay;
        bool localIsTarget = mode is SyncMode.TwoWay;

        switch (options.ConflictResolution)
        {
            case ConflictResolution.KeepLeft:
                return deviceIsTarget
                    ? new PlanItem(item, PlanAction.CopyToDevice, CoreText.Get("Conflict: keep local", "冲突：保留本机"))
                    : new PlanItem(item, PlanAction.None, CoreText.Get("Conflict: keep local, device version not written", "冲突：保留本机，设备版本不写入"));

            case ConflictResolution.KeepRight:
                return localIsTarget
                    ? new PlanItem(item, PlanAction.CopyToLocal, CoreText.Get("Conflict: keep device", "冲突：保留设备"))
                    : new PlanItem(item, PlanAction.None, CoreText.Get("Conflict: keep device, local version not written", "冲突：保留设备，本机版本不写入"));

            case ConflictResolution.KeepBoth:
                if (deviceIsTarget)
                {
                    return new PlanItem(
                        item,
                        PlanAction.CopyToDevice,
                        CoreText.Get("Conflict: keep both", "冲突：两者都保留"),
                        preserveTargetAs: BuildVariantName(item.RelativePath, DeviceVariantSuffix));
                }

                return localIsTarget
                    ? new PlanItem(
                        item,
                        PlanAction.CopyToLocal,
                        CoreText.Get("Conflict: keep both", "冲突：两者都保留"),
                        preserveTargetAs: BuildVariantName(item.RelativePath, LocalVariantSuffix))
                    : new PlanItem(item, PlanAction.None, CoreText.Get("Conflict: skipped", "冲突：跳过"));

            default:
                return new PlanItem(item, PlanAction.Conflict, CoreText.Get("Conflict: waiting for a choice", "冲突：等待选择"));
        }
    }

    /// <summary>
    /// 「两者都保留」冲突策略下给目标端旧版本添加的文件名后缀。
    /// 它会出现在真实文件名里（如 <c>main（设备版本）.cs</c>），因此<strong>不随界面语言变化</strong> ——
    /// 切换语言不会改变磁盘上已经生成的名字。
    /// </summary>
    private const string DeviceVariantSuffix = "设备版本";

    /// <summary>同上：目标端旧版本来自本机侧时使用。</summary>
    private const string LocalVariantSuffix = "本机版本";

    /// <summary>「main.cs」+「设备版本」→「main（设备版本）.cs」。</summary>
    private static string BuildVariantName(string relativePath, string suffix)
    {
        string fileName = Path.GetFileName(relativePath);
        string directory = relativePath[..^fileName.Length];
        string extension = Path.GetExtension(fileName);
        string stem = string.IsNullOrEmpty(extension) ? fileName : fileName[..^extension.Length];
        return $"{directory}{stem}（{suffix}）{extension}";
    }
}
