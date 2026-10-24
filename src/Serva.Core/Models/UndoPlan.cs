using Serva.Core.Localization;

namespace Serva.Core.Models;

/// <summary>撤销一步操作时要做的事。</summary>
public enum UndoActionKind
{
    /// <summary>撤销复制：删除本次新建的文件。</summary>
    DeleteCreatedFile,

    /// <summary>撤销「移入回收」：把文件从回收目录移回原位。</summary>
    RestoreFromTrash,

    /// <summary>撤销重命名：把文件改回原名。</summary>
    RenameBack,

    /// <summary>无法撤销（原始内容已被覆盖 / 直接删除 / 状态未知）。</summary>
    NotPossible,
}

/// <summary>撤销计划中的一步。</summary>
public sealed record UndoStep(
    UndoActionKind Action,
    string RelativePath,
    string SourcePath,
    string TargetPath,
    long Bytes,
    string? Note,
    IReadOnlyList<string>? CreatedDirectories = null)
{
    public bool IsExecutable => Action != UndoActionKind.NotPossible;

    public string ActionText => Action switch
    {
        UndoActionKind.DeleteCreatedFile => CoreText.Get("Delete files copied in this run", "删除本次复制的文件"),
        UndoActionKind.RestoreFromTrash => CoreText.Get("Move back from the recycle folder", "从回收目录移回"),
        UndoActionKind.RenameBack => CoreText.Get("Rename back", "改回原名"),
        _ => Note ?? CoreText.Get("Cannot undo", "无法撤销"),
    };
}

/// <summary>一次运行的撤销计划（按原始操作的<strong>相反顺序</strong>排列）。</summary>
public sealed class UndoPlan
{
    public UndoPlan(IReadOnlyList<UndoStep> steps)
    {
        Steps = steps;
        ExecutableCount = steps.Count(static s => s.IsExecutable);
    }

    public IReadOnlyList<UndoStep> Steps { get; }

    public int ExecutableCount { get; }

    public int TotalCount => Steps.Count;

    public int NotPossibleCount => Steps.Count - ExecutableCount;

    /// <summary>是否值得执行（至少一步可撤销）。</summary>
    public bool CanUndo => ExecutableCount > 0;

    public string Summary => TotalCount == 0
        ? CoreText.Get("Nothing can be undone from this run.", "这次运行没有可撤销的操作。")
        : CoreText.Format(
            "{0} item(s) can be undone; {1} cannot be restored automatically.",
            "可撤销 {0} 项，另有 {1} 项无法自动还原。",
            ExecutableCount,
            NotPossibleCount);
}

/// <summary>撤销执行结果。</summary>
public sealed class UndoResult
{
    public int Deleted { get; init; }

    public int Restored { get; init; }

    public int Renamed { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];

    public int Total => Deleted + Restored + Renamed;

    public bool AnyFailure => Failed > 0;
}
