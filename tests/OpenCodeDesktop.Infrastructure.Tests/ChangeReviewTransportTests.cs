using System.Net;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class ChangeReviewTransportTests
{
    [Fact]
    public async Task ComparisonsEncodeLocationBaseAndTurnIdsAndOnlyRead()
    {
        List<string> paths = [];
        using HttpClient client = new(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            string path = request.RequestUri!.PathAndQuery;
            paths.Add(path);
            return Json(path.StartsWith("/api/session/", StringComparison.Ordinal) && !path.Contains("/diff?", StringComparison.Ordinal)
                ? """{"data":{"id":"s#?","location":{"directory":"/a 空"},"projectID":"p","title":"t"}}"""
                : path.StartsWith("/api/vcs?", StringComparison.Ordinal) ? """{"location":{"directory":"/a 空"},"data":{"branch":{}}}"""
                : """{"location":{"directory":"/a 空"},"data":[{"file":"空 #%.txt","status":"added","additions":1,"deletions":0,"patch":"+one"}]}""");
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectLocation location = new("/a 空", "p");
        ReviewResult repo = await api.ReviewAsync(location, null, new(ReviewMode.Branch, "refs/a&?#"), TestContext.Current.CancellationToken);
        Assert.Equal("空 #%.txt", Assert.Single(repo.Files).Path);
        Assert.Contains("Repository changes", repo.Basis, StringComparison.Ordinal);
        await api.ReviewAsync(location, "s#?", new(ReviewMode.Turns, From: "msg_a&?#", To: "msg_b%"), TestContext.Current.CancellationToken);
        Assert.Contains(paths, path => path.Contains("location%5Bdirectory%5D=%2Fa%20%E7%A9%BA&mode=branch&context=3&base=refs%2Fa%26%3F%23", StringComparison.Ordinal));
        Assert.Contains(paths, path => path == "/api/session/s%23%3F/diff?from=msg_a%26%3F%23&to=msg_b%25&context=3");
    }

    [Fact]
    public async Task InferredBaseIsSentExplicitlyAndIdentified()
    {
        using HttpClient client = new(new Handler(request =>
        {
            string path = request.RequestUri!.PathAndQuery;
            return Json(path.StartsWith("/api/vcs?", StringComparison.Ordinal) ? """{"location":{"directory":"/a"},"data":{"branch":{"current":"feature"}}}"""
                : path.StartsWith("/api/vcs/base?", StringComparison.Ordinal) ? """{"location":{"directory":"/a"},"data":{"name":"main","ref":"refs/heads/main","source":"reflog"}}"""
                : path.Contains("&base=refs%2Fheads%2Fmain", StringComparison.Ordinal) ? """{"location":{"directory":"/a"},"data":[]}""" : throw new InvalidOperationException("Base omitted"));
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ReviewResult result = await api.ReviewAsync(new("/a", "p"), null, new(ReviewMode.Committed), TestContext.Current.CancellationToken);
        Assert.Contains("refs/heads/main", result.Basis, StringComparison.Ordinal);
        Assert.Contains("→ HEAD", result.Basis, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong", "p")]
    [InlineData("/a", "wrong")]
    public async Task SessionOwnershipIsCheckedBeforeSnapshotRequest(string directory, string project)
    {
        int requests = 0;
        using HttpClient client = new(new Handler(_ =>
        {
            requests++;
            return Json(JsonSerializer.Serialize(new { data = new { id = "s", location = new { directory }, projectID = project, title = "t" } }));
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ReviewAsync(new("/a", "p"), "s", new(ReviewMode.Turns, From: "u"), TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Protocol, error.Failure);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task PatchAndFileCountsAreBoundedAndEmptyBinaryPatchesRemainHonest()
    {
        var files = Enumerable.Range(0, 1002).Select(i => new { file = i.ToString(System.Globalization.CultureInfo.InvariantCulture), status = "modified", additions = 0, deletions = 0, patch = i == 0 ? new string('x', 100010) : "" });
        string body = JsonSerializer.Serialize(new { data = files, location = new { directory = "/a" } });
        using HttpClient client = new(new Handler(request => Json(request.RequestUri!.AbsolutePath == "/api/vcs" ? """{"location":{"directory":"/a"},"data":{"branch":{}}}""" : body)));
        using OpenCodeWorkspaceConnection api = Api(client);
        ReviewResult result = await api.ReviewAsync(new("/a", "p"), null, new(ReviewMode.Working), TestContext.Current.CancellationToken);
        Assert.True(result.Limited);
        Assert.Equal(1000, result.Files.Count);
        Assert.True(result.Files[0].Truncated);
        Assert.Equal(100000, result.Files[0].Patch.Length);
        Assert.Equal("", result.Files[1].Patch);
    }

    [Fact]
    public async Task OversizedAndWrongLocationResponsesAreRejected()
    {
        foreach (bool oversized in new[] { false, true })
        {
            using HttpClient client = new(new Handler(request => Json(request.RequestUri!.AbsolutePath == "/api/vcs" ? """{"location":{"directory":"/a"},"data":{"branch":{}}}"""
                : oversized ? new string('x', ReviewLimits.ResponseBytes + 1) : """{"location":{"directory":"/wrong"},"data":[]}""")));
            using OpenCodeWorkspaceConnection api = Api(client);
            WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ReviewAsync(new("/a", "p"), null, new(ReviewMode.Working), TestContext.Current.CancellationToken));
            Assert.Equal(oversized ? WorkspaceFailure.Size : WorkspaceFailure.Protocol, error.Failure);
        }
    }

    [Fact]
    public async Task ConversationSummariesOnlyUseRecordedSnapshotFiles()
    {
        using HttpClient client = new(new Handler(_ => Json("""{"data":[{"id":"a","type":"assistant","content":[{"type":"text","text":"changed guessed.txt"}],"snapshot":{"files":["actual.txt"]}}],"cursor":{"next":null}}""")));
        using OpenCodeWorkspaceConnection api = Api(client);
        Assert.Equal("actual.txt", Assert.Single(Assert.Single((await api.HistoryAsync("s", null, TestContext.Current.CancellationToken)).Data).ChangedFiles));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static OpenCodeWorkspaceConnection Api(HttpClient client)
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:3000", out ServerAddress? address));
        return new(client, address!, "fixture");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
