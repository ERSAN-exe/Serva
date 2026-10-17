using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>跑通「扫描 → 比较 → 计划 → 执行」整条链路的测试工具。</summary>
internal static class SyncFixture
{
    public static string NewTempDirectory(string prefix) =>
        Path.Combine(Path.GetTempPath(), "serva-tests", $"{prefix}-{Guid.NewGuid():N}");

    public static SyncOptions Options() => new()
    {
        Concurrency = 2,
        VerifyAfterCopy = true,
        MirrorDelete = false,
        UseTrashFolder = true,
    };

    public static async Task<SyncRunResult> RunAsync(
        string leftRoot,
        string rightRoot,
        SyncMode mode,
        SyncOptions options,
        string journalDirectory,
        CancellationToken cancellationToken = default)
    {
        var scanner = new FileScanner(new ExcludeMatcher());
        ScanResult left = scanner.Scan(leftRoot);
        ScanResult right = scanner.Scan(rightRoot);
        CompareResult compare = new CompareEngine().Compare(left, right);
        SyncPlan plan = new PlanBuilder().Build(compare, mode, options);

        return await new SyncEngine().ExecuteAsync(
            plan,
            options,
            leftRoot,
            rightRoot,
            progress: null,
            journalDirectory: journalDirectory,
            cancellationToken: cancellationToken);
    }
}
