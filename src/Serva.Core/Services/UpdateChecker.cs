using System.Net;
using System.Text.RegularExpressions;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 检查 GitHub 上是否有更新的正式发布版本。
/// <para>
/// 不调用 REST API（未认证限流 60 次/小时/IP），而是读取
/// <c>https://github.com/ERSAN-exe/Serva/releases/latest</c> 的 302 跳转地址：
/// GitHub 会把「最新正式发布」重定向到 <c>/releases/tag/{tag}</c>，
/// 仓库还没有发布时则重定向到 Release 列表页（或直接返回 404）。
/// </para>
/// <para>
/// 这与「点击后打开最新 Release 页面」用的是同一个地址，因此不需要 API 凭证，也不会触发限流。
/// </para>
/// </summary>
public sealed partial class UpdateChecker
{
    /// <summary>最新 Release 页面（GitHub 会自动跳转到最新的正式发布）。</summary>
    public static Uri LatestReleasePage { get; } = new("https://github.com/ERSAN-exe/Serva/releases/latest");

    private readonly HttpClient _client;

    /// <summary>使用默认客户端（8 秒超时、不跟随重定向、带 User-Agent）。</summary>
    public UpdateChecker()
        : this(CreateClient())
    {
    }

    /// <summary>注入自定义客户端（单元测试用假的 <see cref="HttpMessageHandler"/>）。</summary>
    public UpdateChecker(HttpClient client) => _client = client;

    /// <summary>
    /// 检查是否有比 <paramref name="currentVersion"/> 更新的正式版本。
    /// <para><strong>永不抛异常</strong>：离线 / 超时 / 限流 / 解析失败都返回 <see cref="UpdateCheckStatus.Failed"/>。</para>
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(LatestReleasePage, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                string? tag = ReadTag(response.Headers.Location);

                // 跳到 /releases（列表页）说明没有正式发布。
                return tag is null ? UpdateCheckResult.NoRelease : Compare(currentVersion, tag);
            }

            // 没有 Release：GitHub 返回 404；个别情况下是无跳转的 200 列表页。
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK)
            {
                return UpdateCheckResult.NoRelease;
            }

            return UpdateCheckResult.Failed;
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.Failed;
        }
        catch (TaskCanceledException)
        {
            // 超时或被取消。
            return UpdateCheckResult.Failed;
        }
        catch (InvalidOperationException)
        {
            return UpdateCheckResult.Failed;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status
        is HttpStatusCode.MovedPermanently      // 301
        or HttpStatusCode.Found                 // 302
        or HttpStatusCode.TemporaryRedirect     // 307
        or HttpStatusCode.PermanentRedirect;    // 308

    /// <summary>
    /// 从 <c>.../releases/tag/v0.2.0</c> 里取出 <c>v0.2.0</c>；
    /// 不是 tag 地址（例如 Release 列表页）时返回 <see langword="null"/>。
    /// </summary>
    private static string? ReadTag(Uri? location)
    {
        if (location is null)
        {
            return null;
        }

        Uri absolute = location.IsAbsoluteUri ? location : new Uri(LatestReleasePage, location);
        Match match = TagPattern().Match(absolute.AbsolutePath);

        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private static UpdateCheckResult Compare(string currentVersion, string tag)
    {
        string latest = tag.Trim().TrimStart('v', 'V');

        if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current)
            || !SemanticVersion.TryParse(latest, out SemanticVersion newest))
        {
            // 版本号格式无法比较：不打扰用户，但也不谎报「已是最新」。
            return new UpdateCheckResult(UpdateCheckStatus.Failed, tag, latest, false);
        }

        bool isNewer = newest.CompareTo(current) > 0;

        return new UpdateCheckResult(
            isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            tag,
            latest,
            isNewer);
    }

    private static HttpClient CreateClient()
    {
        // 必须关闭自动重定向：要读 302 的 Location 才知道最新 tag。
        var handler = new HttpClientHandler { AllowAutoRedirect = false };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
        };

        // GitHub 会拒绝没有 User-Agent 的请求。
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Serva/{AppVersion.Current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        return client;
    }

    /// <summary>GitHub 的 Release 页面地址：<c>/{owner}/{repo}/releases/tag/{tag}</c>。</summary>
    [GeneratedRegex("/releases/tag/([^/?#]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
