using System.Net;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public async Task NewerTag_IsReportedAsUpdate()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.Found,
            "https://github.com/ERSAN-exe/Serva/releases/tag/v0.2.0",
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsNewer);
        Assert.Equal("v0.2.0", result.LatestTag);
        Assert.Equal("0.2.0", result.LatestVersion);
    }

    [Fact]
    public async Task SameTag_IsUpToDate()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.Found,
            "/ERSAN-exe/Serva/releases/tag/v0.1.0",
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsNewer);
        Assert.Equal("0.1.0", result.LatestVersion);
    }

    [Fact]
    public async Task OlderTag_IsUpToDate()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.Found,
            "/ERSAN-exe/Serva/releases/tag/v0.0.9",
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsNewer);
    }

    [Fact]
    public async Task RedirectToReleaseList_MeansNoRelease()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.Found,
            "/ERSAN-exe/Serva/releases",
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.NoRelease, result.Status);
        Assert.Null(result.LatestTag);
        Assert.False(result.IsNewer);
    }

    [Fact]
    public async Task NotFound_MeansNoRelease()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.NotFound,
            location: null,
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.NoRelease, result.Status);
    }

    [Fact]
    public async Task ServerError_IsFailed()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.InternalServerError,
            location: null,
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(result.IsNewer);
    }

    [Fact]
    public async Task UnparsableTag_IsFailedInsteadOfFakeLatest()
    {
        UpdateCheckResult result = await CheckAsync(
            HttpStatusCode.Found,
            "/ERSAN-exe/Serva/releases/tag/nightly",
            currentVersion: "0.1.0");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.False(result.IsNewer);
    }

    [Fact]
    public async Task NetworkFailure_IsFailed()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));

        UpdateCheckResult result = await new UpdateChecker(client).CheckAsync("0.1.0");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Timeout_IsFailed()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new TaskCanceledException("timeout")));

        UpdateCheckResult result = await new UpdateChecker(client).CheckAsync("0.1.0");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RequestsTheLatestReleasePage()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        await new UpdateChecker(client).CheckAsync("0.1.0");

        Assert.Equal(UpdateChecker.LatestReleasePage, handler.LastRequestUri);
        Assert.Equal("https://github.com/ERSAN-exe/Serva/releases/latest", handler.LastRequestUri?.AbsoluteUri);
    }

    private static async Task<UpdateCheckResult> CheckAsync(
        HttpStatusCode status,
        string? location,
        string currentVersion)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status);

            if (location is not null)
            {
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            }

            return response;
        });

        using var client = new HttpClient(handler);

        return await new UpdateChecker(client).CheckAsync(currentVersion);
    }

    /// <summary>返回预置响应的假 Handler（不跟随重定向，与生产实现一致）。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(_respond(request));
        }
    }
}
