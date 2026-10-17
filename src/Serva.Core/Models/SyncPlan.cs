using Serva.Core.Localization;

namespace Serva.Core.Models;

/// <summary>执行计划中的一行 = 比较结果 + 将要执行的动作。</summary>
public sealed class PlanItem
{
    public PlanItem(CompareItem item, PlanAction action, string? note = null, string? targetFileName = null, string? preserveTargetAs = null)
    {
        Item = item;
        Action = action;
        Note = note;
        TargetFileName = targetFileName;
        PreserveTargetAs = preserveTargetAs;
    }

    public CompareItem Item { get; }

    public PlanAction Action { get; }

    /// <summary>给用户看的说明（例如「冲突：保留本机」「设备端较旧，已跳过」）。</summary>
    public string? Note { get; }

    /// <summary>写入目标端时使用的文件名（「两者都保留」冲突策略会在目标端重命名）。</summary>
    public string? TargetFileName { get; }

    /// <summary>
    /// 「两者都保留」冲突策略：写入前先把目标端已存在的文件改名为该名称，
    /// 从而两个版本都不丢失。为 <c>null</c> 时表示目标端会被覆盖。
    /// </summary>
    public string? PreserveTargetAs { get; }

    /// <summary>表格中的勾选状态（默认全选）。</summary>
    public bool IsSelected { get; set; } = true;

    public string RelativePath => Item.RelativePath;

    public long Size => Item.Size;

    public DateTime? LastWriteTimeUtc => Item.LastWriteTimeUtc;

    /// <summary>
    /// 是否需要真正执行。冲突项在用户做出选择前不会执行，因此也不算「可执行」。
    /// </summary>
    public bool IsActionable => Action is not (PlanAction.None or PlanAction.Conflict);

    /// <summary>状态列 / 筛选 chip 使用的状态（删除类动作用「将删除」表示）。</summary>
    public CompareStatus DisplayStatus => Action is PlanAction.DeleteOnDevice or PlanAction.DeleteOnLocal
        ? CompareStatus.Delete
        : Item.Status;

    /// <summary>「将执行」列显示的文案。</summary>
    public string ActionText => Action switch
    {
        PlanAction.CopyToDevice => TargetFileName is null
            ? CoreText.Get("Copy → device", "复制 → 设备")
            : CoreText.Format("Copy → device (renamed to {0})", "复制 → 设备（重命名为 {0}）", TargetFileName),
        PlanAction.CopyToLocal => TargetFileName is null
            ? CoreText.Get("Copy → local", "复制 → 本机")
            : CoreText.Format("Copy → local (renamed to {0})", "复制 → 本机（重命名为 {0}）", TargetFileName),
        PlanAction.DeleteOnDevice => CoreText.Get("Delete extra files on device", "删除设备端多余"),
        PlanAction.DeleteOnLocal => CoreText.Get("Delete extra files on local", "删除本机端多余"),
        PlanAction.Conflict => CoreText.Get("Needs a choice", "需要选择"),
        _ => Note ?? CoreText.Get("No action", "不执行"),
    };
}

/// <summary>一次同步的完整执行计划（用于显示「将执行 N 项 · X GB」）。</summary>
public sealed class SyncPlan
{
    private readonly Dictionary<CompareStatus, int> _counts;

    public SyncPlan(SyncMode mode, IReadOnlyList<PlanItem> items)
    {
        Mode = mode;
        Items = items;

        _counts = new Dictionary<CompareStatus, int>();
        foreach (CompareStatus status in Enum.GetValues<CompareStatus>())
        {
            _counts[status] = 0;
        }

        foreach (PlanItem item in items)
        {
            _counts[item.DisplayStatus]++;
        }
    }

    public SyncMode Mode { get; }

    public IReadOnlyList<PlanItem> Items { get; }

    public int TotalCount => Items.Count;

    /// <summary>全部需要执行的动作数（含未勾选）。</summary>
    public int ActionableCount => Items.Count(static i => i.IsActionable);

    /// <summary>已勾选且需要执行的动作数 —— 对应「将执行 N 项」。</summary>
    public int SelectedCount => Items.Count(static i => i.IsSelected && i.IsActionable);

    /// <summary>已勾选且需要写入数据的字节数 —— 对应「X GB」。</summary>
    public long SelectedWriteBytes =>
        Items.Where(static i => i.IsSelected && IsWrite(i.Action)).Sum(static i => i.Size);

    public int CountOf(CompareStatus status) => _counts.TryGetValue(status, out int value) ? value : 0;

    public IReadOnlyDictionary<CompareStatus, int> CountByStatus => _counts;

    public static bool IsWrite(PlanAction action) =>
        action is PlanAction.CopyToDevice or PlanAction.CopyToLocal;

    public static bool IsDelete(PlanAction action) =>
        action is PlanAction.DeleteOnDevice or PlanAction.DeleteOnLocal;
}
