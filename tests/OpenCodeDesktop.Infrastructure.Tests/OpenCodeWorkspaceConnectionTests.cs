using System.Net;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class OpenCodeWorkspaceConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryRetainsPartOrdinalsAndExplicitCompletionForStreaming(bool completed)
    {
        string body = JsonSerializer.Serialize(new
        {
            data = new[] { new { id = "stream", type = "assistant",
                content = new[] { new { type = "reasoning", text = "context" }, new { type = "text", text = "prefix" } },
                time = completed ? new Dictionary<string, int> { ["completed"] = 2 } : [] } },
            cursor = new { next = (string?)null },
        });
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(body))));
        using OpenCodeWorkspaceConnection api = Api(client);
        HistoryMessage message = Assert.Single((await api.HistoryAsync("s", null, TestContext.Current.CancellationToken)).Data);
        Assert.Equal(completed, message.IsComplete);
        Assert.Equal("Recorded reasoning: context\n\nprefix", message.Text);
        Assert.Equal(new HistoryContentPart(0, "Recorded reasoning: context", false), message.Parts[0]);
        Assert.Equal(new HistoryContentPart(1, "prefix", true), message.Parts[1]);
    }

    [Theory]
    [InlineData("aborted", true, "error", HistoryFailureOutcome.Interrupted)]
    [InlineData("provider", true, "error", HistoryFailureOutcome.Failed)]
    [InlineData("aborted", false, "error", null)]
    [InlineData("aborted", true, "stop", null)]
    public async Task HistoryMapsOnlyExplicitCompletedAssistantFailures(string error, bool completed, string finish, HistoryFailureOutcome? expected)
    {
        string body = JsonSerializer.Serialize(new
        {
            data = new[] { new { id = "failure", type = "assistant", content = Array.Empty<object>(), finish,
                error = new { type = error }, time = completed ? new Dictionary<string, int> { ["completed"] = 2 } : [] } },
            cursor = new { next = (string?)null },
        });
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(body))));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspacePage<HistoryMessage> page = await api.HistoryAsync("s", null, TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(page.Data).CompletedFailure);
    }

    private const string Session = """{"id":"ses_1","projectID":"global","location":{"directory":"/空 project"},"unknown":true}""";
    private const string Empty = """{"data":[],"cursor":{"previous":null,"next":null}}""";

    [Fact]
    public async Task EveryOperationUsesEncodedVerifiedContractAndPerRequestAuthorization()
    {
        List<string> requests = [];
        using HttpClient client = new(new Handler(async (request, token) =>
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal("opencode:pāss:word", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            string path = request.RequestUri.PathAndQuery;
            requests.Add(request.Method + " " + path);
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("/空 project", body.RootElement.GetProperty("location").GetProperty("directory").GetString());
                Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
                return Json("{\"data\":" + Session + "}");
            }
            if (path.StartsWith("/api/location", StringComparison.Ordinal))
            {
                return Json("""{"directory":"/空 project","project":{"id":"global"}}""");
            }

            if (path.StartsWith("/api/fs/list", StringComparison.Ordinal))
            {
                return Json("""{"location":{"directory":"/空 project"},"data":[]}""");
            }

            if (path == "/api/session/ses%2F1")
            {
                return Json("{\"data\":" + Session + "}");
            }

            return Json(Empty);
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectLocation location = await api.ResolveAsync("/空 project", CancellationToken.None);
        Assert.Equal("/空 project", location.Directory);
        await api.ValidateDirectoryAsync(location.Directory, CancellationToken.None);
        await api.SessionsAsync(location.Directory, null, CancellationToken.None);
        await api.SessionsAsync(location.Directory, "opaque/+?=", CancellationToken.None);
        Assert.Equal("Untitled session", (await api.CreateAsync(location.Directory, CancellationToken.None)).Title);
        await api.SessionAsync("ses/1", CancellationToken.None);
        await api.HistoryAsync("ses/1", null, CancellationToken.None);
        await api.HistoryAsync("ses/1", "opaque/+?=", CancellationToken.None);
        Assert.Equal(new[]
        {
            "GET /api/location?location%5Bdirectory%5D=%2F%E7%A9%BA%20project",
            "GET /api/fs/list?location%5Bdirectory%5D=%2F%E7%A9%BA%20project",
            "GET /api/session?directory=%2F%E7%A9%BA%20project&limit=50&order=desc",
            "GET /api/session?directory=%2F%E7%A9%BA%20project&limit=50&cursor=opaque%2F%2B%3F%3D",
            "POST /api/session", "GET /api/session/ses%2F1",
            "GET /api/session/ses%2F1/message?limit=50&order=desc",
            "GET /api/session/ses%2F1/message?limit=50&cursor=opaque%2F%2B%3F%3D",
        }, requests);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData(400, WorkspaceFailure.Http)]
    [InlineData(401, WorkspaceFailure.Unauthorized)]
    [InlineData(404, WorkspaceFailure.NotFound)]
    [InlineData(500, WorkspaceFailure.Http)]
    [InlineData(503, WorkspaceFailure.Http)]
    [InlineData(302, WorkspaceFailure.Http)]
    public async Task FailuresAreSafeAndMutationsAreNeverRetried(int status, WorkspaceFailure expected)
    {
        int calls = 0;
        using HttpClient client = new(new Handler((_, _) =>
        {
            calls++;
            HttpResponseMessage response = Json("secret provider settings");
            response.StatusCode = (HttpStatusCode)status;
            response.Headers.Location = new Uri("http://untrusted.invalid");
            return Task.FromResult(response);
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.CreateAsync("/directory", CancellationToken.None));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("provider settings", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"data\":[],\"cursor\":{\"next\":5}}")]
    [InlineData("{\"data\":[],\"cursor\":{\"next\":\"\"}}")]
    [InlineData("{\"data\":[{\"id\":\"x\",\"type\":\"user\"}],\"cursor\":{\"next\":null}}")]
    public async Task MalformedHistoryIsAnActionableProtocolFailure(string body)
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(body))));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.HistoryAsync("s", null, CancellationToken.None));
        Assert.Equal(WorkspaceFailure.Protocol, error.Failure);
    }

    [Fact]
    public async Task HistoryMapsTextToolErrorsAndUnknownVariantsAsRecordedContent()
    {
        const string history = """
            {"data":[
              {"id":"1","type":"user","text":"hello"},
              {"id":"2","type":"assistant","content":[{"type":"text","text":"answer"},{"type":"tool","name":"build","state":{"status":"completed","content":[{"type":"text","text":"passed"}]}}],"error":{}},
              {"id":"3","type":"future-variant","providerSettings":{"secret":"hidden"}},
              {"id":"4","type":"shell","command":"build","status":"running","output":"recording"},
              {"id":"5","type":"assistant","content":[{"type":"future-part","secret":"hidden"}]}
            ],"cursor":{"next":"even-final-pages-can-have-cursors"}}
            """;
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(history))));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspacePage<HistoryMessage> page = await api.HistoryAsync("s", null, CancellationToken.None);
        Assert.Equal(5, page.Data.Count);
        Assert.Equal("hello", page.Data[0].Text);
        Assert.Contains("passed", page.Data[1].Text, StringComparison.Ordinal);
        Assert.Contains("recorded assistant error", page.Data[1].Text, StringComparison.Ordinal);
        Assert.Contains("Unsupported historical", page.Data[2].Text, StringComparison.Ordinal);
        Assert.Contains("Recorded status: running", page.Data[3].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", string.Join("", page.Data.Select(m => m.Text)), StringComparison.Ordinal);
        Assert.NotNull(page.Next);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SizeLimitsAreEnforcedWithAndWithoutDeclaredLength(bool history, bool declaredLength)
    {
        int size = history ? 8388608 : 1048576;
        TrackingStream stream = new(Encoding.UTF8.GetBytes(new string('x', size + 1)));
        using HttpClient client = new(new Handler((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            if (declaredLength)
            {
                response.Content.Headers.ContentLength = size + 1;
            }
            return Task.FromResult(response);
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(async () =>
        {
            if (history)
            {
                await api.HistoryAsync("s", null, CancellationToken.None);
            }
            else
            {
                await api.SessionsAsync("/dir", null, CancellationToken.None);
            }
        });
        Assert.Equal(WorkspaceFailure.Size, error.Failure);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task BodyDeadlineCancellationTransportAndDisposalAreBoundedAndReleaseResources()
    {
        StallStream stream = new();
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using OpenCodeWorkspaceConnection api = Api(client, TimeSpan.FromMilliseconds(50));
        WorkspaceException timeout = await Assert.ThrowsAsync<WorkspaceException>(() => api.SessionsAsync("/dir", null, CancellationToken.None));
        Assert.Equal(WorkspaceFailure.Timeout, timeout.Failure);
        Assert.True(stream.Disposed);

        using HttpClient stalled = new(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json(Empty);
        }));
        using OpenCodeWorkspaceConnection cancelled = Api(stalled);
        using CancellationTokenSource cancel = new(50);
        WorkspaceException cancellation = await Assert.ThrowsAsync<WorkspaceException>(() => cancelled.SessionsAsync("/dir", null, cancel.Token));
        Assert.Equal(WorkspaceFailure.Cancelled, cancellation.Failure);
        Task<WorkspacePage<WorkspaceSession>> pending = cancelled.SessionsAsync("/dir", null, CancellationToken.None);
        cancelled.Dispose();
        Assert.Equal(WorkspaceFailure.Cancelled, (await Assert.ThrowsAsync<WorkspaceException>(() => pending)).Failure);
        await Assert.ThrowsAsync<WorkspaceException>(() => cancelled.SessionsAsync("/dir", null, CancellationToken.None));

        using HttpClient broken = new(new Handler((_, _) => throw new HttpRequestException("password secret")));
        using OpenCodeWorkspaceConnection transport = Api(broken);
        WorkspaceException failure = await Assert.ThrowsAsync<WorkspaceException>(() => transport.SessionAsync("s", CancellationToken.None));
        Assert.Equal(WorkspaceFailure.Transport, failure.Failure);
        Assert.DoesNotContain("password secret", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynchronousBackendAndParsingNeverRunOnCallingUiThread()
    {
        int caller = Environment.CurrentManagedThreadId;
        int backend = caller;
        using HttpClient client = new(new Handler((_, _) =>
        {
            backend = Environment.CurrentManagedThreadId;
            return Task.FromResult(Json(Empty));
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        // A blocking wait here deliberately simulates a single-thread caller: worker progress is independent.
        Task<WorkspacePage<WorkspaceSession>> work = api.SessionsAsync("/dir", null, CancellationToken.None);
        Assert.True(work.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.NotEqual(caller, backend);
        await work;
    }

    private static OpenCodeWorkspaceConnection Api(HttpClient client, TimeSpan? deadline = null)
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address));
        return new(client, address!, "pāss:word", deadline);
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
        public override bool CanSeek => false;
    }
    private sealed class StallStream : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
