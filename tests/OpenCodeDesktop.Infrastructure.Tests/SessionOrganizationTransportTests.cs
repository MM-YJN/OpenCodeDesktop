using System.Net;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class SessionOrganizationTransportTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "opaque/# %?")]
    public async Task SearchEncodesFilterDirectoryAndCursorAndMapsArchiveTimestamp(bool archived, string? cursor)
    {
        using HttpClient client = new(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            string path = request.RequestUri!.PathAndQuery;
            Assert.Contains("directory=%2F%E7%A9%BA%20%23%25", path, StringComparison.Ordinal);
            Assert.Contains("search=%23%25%20%E4%B8%96%E7%95%8C%26%3F", path, StringComparison.Ordinal);
            Assert.Contains("archived=" + (archived ? "true" : "false"), path, StringComparison.Ordinal);
            Assert.Contains(cursor is null ? "order=desc" : "cursor=" + Uri.EscapeDataString(cursor), path, StringComparison.Ordinal);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            await Task.CompletedTask;
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"s","projectID":"p","location":{"directory":"/空 #%"},"title":"Title","time":{"archived":123}}],"cursor":{"next":"next"}}""") };
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        WorkspacePage<WorkspaceSession> page = await api.SearchSessionsAsync("/空 #%", new("#% 世界&?", archived), cursor, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(page.Data).Archived);
        Assert.Equal("next", page.Next);
    }

    [Fact]
    public async Task MutationsUseNarrowPatchBodiesAndNeverRetryLostAcknowledgement()
    {
        List<string> bodies = [];
        using HttpClient client = new(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/session/s%2F%23%25", request.RequestUri!.AbsolutePath);
            bodies.Add(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            if (bodies.Count == 3) { throw new HttpRequestException("lost"); }
            return new(HttpStatusCode.NoContent);
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        await api.RenameSessionAsync("s/#%", "Title 世界", TestContext.Current.CancellationToken);
        await api.ArchiveSessionAsync("s/#%", true, TestContext.Current.CancellationToken);
        WorkspaceException failure = await Assert.ThrowsAsync<WorkspaceException>(() => api.ArchiveSessionAsync("s/#%", false, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Transport, failure.Failure);
        Assert.Equal(3, bodies.Count);
        using JsonDocument rename = JsonDocument.Parse(bodies[0]);
        Assert.Single(rename.RootElement.EnumerateObject());
        Assert.Equal("Title 世界", rename.RootElement.GetProperty("title").GetString());
        Assert.Equal("{\"archived\":true}", bodies[1]);
        Assert.Equal("{\"archived\":false}", bodies[2]);
    }

    private static OpenCodeWorkspaceConnection Create(HttpClient client)
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address));
        return new(client, address!, "secret");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
