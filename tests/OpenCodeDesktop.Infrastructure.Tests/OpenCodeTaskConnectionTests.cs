using System.Net;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed partial class OpenCodeTaskConnectionTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RejectedAndStaleDecisionsAreNotRetriedAndRawErrorsAreNotExposed(HttpStatusCode status)
    {
        int calls = 0;
        using HttpClient client = new(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("secret-provider-response") });
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ReplyFormAsync("s", "frm", new Dictionary<string, object>(), TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Equal(status == HttpStatusCode.Unauthorized ? WorkspaceFailure.Unauthorized : status == HttpStatusCode.NotFound ? WorkspaceFailure.NotFound : WorkspaceFailure.Http, error.Failure);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task SseParsesSplitCoalescedFramesCommentsCrLfUnknownAndLocationlessEvents(int chunk)
    {
        const string data = ": heartbeat\r\n\r\ndata: {\"id\":\"one\",\"type\":\"session.text.delta\",\"data\":{\"sessionID\":\"s\",\"assistantMessageID\":\"m\",\"ordinal\":2,\"delta\":\"你好\"}}\r\n\r\ndata: {\"type\":\"future.event\",\"data\":{},\"durable\":{\"aggregateID\":\"s\",\"seq\":7}}\n\ndata: {\"type\":\"session.instructions.updated\",\"data\":{\"delta\":{\"unknown\":\"object\"}}}\n\n";
        using var stream = new ChunkStream(Encoding.UTF8.GetBytes(data), chunk);
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Sse(stream))));
        using OpenCodeWorkspaceConnection api = Create(client);
        List<TaskEvent> events = [];
        await Assert.ThrowsAsync<WorkspaceException>(() => api.WatchAsync(e =>
        {
            events.Add(e);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken));
        Assert.Equal(3, events.Count);
        Assert.Equal("你好", events[0].Delta);
        Assert.Equal(2, events[0].Ordinal);
        Assert.Equal("s", events[1].SessionId);
        Assert.Null(events[2].Delta);
    }

    [Theory]
    [InlineData("data: bad-json\n\n")]
    [InlineData("data: {\"type\":\"stream.error\",\"data\":{}}\n\n")]
    public async Task FailedAndMalformedStreamStopsWithoutExposingResponse(string frame)
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Sse(new MemoryStream(Encoding.UTF8.GetBytes(frame))))));
        using OpenCodeWorkspaceConnection api = Create(client);
        await Assert.ThrowsAnyAsync<Exception>(() => api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OversizedCommentIsBoundedAndDisposed()
    {
        using var stream = new ChunkStream(Encoding.UTF8.GetBytes(":" + new string('x', 1048577)), 4096);
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Sse(stream))));
        using OpenCodeWorkspaceConnection api = Create(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Size, error.Failure);
        Assert.True(stream.Closed);
    }

    [Fact]
    public async Task StreamHeaderDeadlineAndConnectionDisposalCancelPendingReads()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient client = new(new Handler(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        Task watch = api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        api.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
        using var deadline = new OpenCodeWorkspaceConnection(client, Address(), "secret", TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deadline.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MutationsUseExactContractAndNeverRetry()
    {
        List<string> paths = [];
        using HttpClient client = new(new Handler(async (request, token) =>
        {
            Assert.NotNull(request.Headers.Authorization);
            string path = request.RequestUri!.PathAndQuery;
            paths.Add(path);
            if (path.EndsWith("/prompt", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("msg_stable", body.RootElement.GetProperty("id").GetString());
                Assert.Equal("steer", body.RootElement.GetProperty("delivery").GetString());
                return Json("{\"data\":{\"id\":\"msg_stable\",\"sessionID\":\"s\"}}");
            }
            if (path.EndsWith("/interrupt?resume=false", StringComparison.Ordinal))
            {
                return Json("{\"interrupted\":false}");
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        CancellationToken token = TestContext.Current.CancellationToken;
        await api.SelectModelAsync("s", new("m", "p", "M"), token);
        await api.SelectAgentAsync("s", "build", token);
        await api.SubmitAsync("s", new("msg_stable", "text"), token);
        await api.InterruptAsync("s", token);
        await api.ReplyPermissionAsync("s", "perm/one", "once", token);
        await api.ReplyFormAsync("s", "frm_one", new Dictionary<string, object> { ["q"] = false }, token);
        await api.CancelFormAsync("s", "frm_one", token);
        Assert.Equal(7, paths.Count);
        Assert.Contains("/api/session/s/interrupt?resume=false", paths);
        Assert.Contains("/api/session/s/permission/perm%2Fone/reply", paths);
    }

    [Fact]
    public async Task CatalogOmitsSecretsDisabledModelsAndHiddenSubagentsAndRunsHandlerOffCallerThread()
    {
        int caller = 0;
        List<int> threads = [];
        using HttpClient client = new(new Handler((request, _) =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/api/model"
                ? """{"data":[{"id":"m","providerID":"p","name":"Model","enabled":true,"status":"active","settings":{"apiKey":"secret"}},{"id":"off","enabled":false,"status":"active"}]}"""
                : """{"data":[{"id":"build","name":"Build","mode":"primary","hidden":false},{"id":"sub","mode":"subagent","hidden":false},{"id":"hidden","mode":"primary","hidden":true}]}"""));
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        // A dedicated caller represents the UI thread. A pool thread can legitimately be reused
        // by the second handler after the first await, so comparing against a test pool thread is racy.
        TaskCompletionSource<Task<TaskCatalog>> invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = TestContext.Current.CancellationToken;
        Thread callerThread = new(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            try
            {
                invoked.SetResult(api.CatalogAsync("/dir with spaces", token));
            }
            catch (Exception error)
            {
                invoked.SetException(error);
            }
        })
        { IsBackground = true };
        callerThread.Start();
        Task<TaskCatalog> pending = await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        TaskCatalog catalog = await pending;
        Assert.Equal("m", Assert.Single(catalog.Models).Id);
        Assert.Equal("build", Assert.Single(catalog.Agents).Id);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(catalog), StringComparison.Ordinal);
        // Task.Run isolates even a synchronous transport handler and parsing from the initiating UI context.
        Assert.DoesNotContain(caller, threads);
    }

    [Fact]
    public async Task FormDetailMapsTypedDefaultsConditionsAndUnknownControls()
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json("""{"data":{"id":"frm_one","sessionID":"s","title":"Question","state":{"status":"pending"},"fields":[{"key":"x","type":"integer","default":2,"minimum":1,"maximum":3},{"key":"b","type":"boolean","default":false},{"key":"future","type":"future","hidden":true,"required":true,"when":[{"key":"b","op":"eq","value":false}]}]}}"""))));
        using OpenCodeWorkspaceConnection api = Create(client);
        TaskForm form = await api.FormAsync("s", "frm_one", TestContext.Current.CancellationToken);
        Assert.Equal(2d, form.Fields[0].Default);
        Assert.Equal(false, form.Fields[1].Default);
        Assert.Equal("future", form.Fields[2].Type);
        Assert.Equal(false, Assert.Single(form.Fields[2].When).Value);
    }

    private static OpenCodeWorkspaceConnection Create(HttpClient client) => new(client, Address(), "secret");
    private static ServerAddress Address()
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address));
        return address!;
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Sse(Stream stream)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new("text/event-stream");
        return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class ChunkStream(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        public bool Closed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
