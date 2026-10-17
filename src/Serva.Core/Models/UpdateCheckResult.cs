namespace Serva.Core.Models;

/// <summary>一次「检查更新」的结论。</summary>
public enum UpdateCheckStatus
{
    /// <summary>已是最新版本。</summary>
    UpToDate,

    /// <summary>发现更新的版本。</summary>
    UpdateAvailable,

    /// <summary>仓库还没有发布任何正式版本。</summary>
    NoRelease,

    /// <summary>网络 / 解析失败（离线、超时、被拦截、版本号无法识别等）。</summary>
    Failed,
}

/// <summary>检查更新的结果。</summary>
/// <param name="Status">结论。</param>
/// <param name="LatestTag">远程 tag（原样，可能带 <c>v</c> 前缀），没有时为 <see langword="null"/>。</param>
/// <param name="LatestVersion">规范化后的远程版本号（如 <c>0.2.0</c>），没有时为 <see langword="null"/>。</param>
/// <param name="IsNewer">远程版本是否比当前版本更新。</param>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string? LatestTag,
    string? LatestVersion,
    bool IsNewer)
{
    /// <summary>网络 / 解析失败。</summary>
    public static UpdateCheckResult Failed { get; } = new(UpdateCheckStatus.Failed, null, null, false);

    /// <summary>仓库还没有发布任何 Release。</summary>
    public static UpdateCheckResult NoRelease { get; } = new(UpdateCheckStatus.NoRelease, null, null, false);
}
