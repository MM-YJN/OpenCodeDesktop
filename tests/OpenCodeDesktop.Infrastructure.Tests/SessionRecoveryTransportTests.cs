using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed partial class OpenCodeTaskConnectionTests
{
    [Fact]
    public async Task WireLevelLostPromptResponseRetainsDeliveryAndRecoveryNeverPostsAgain()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? admittedId = null;
        bool revealHistory = false;
        int posts = 0;
        Task server = DropResponseAsync();
        using HttpClient wire = new(new SocketsHttpHandler { UseProxy = false });
        using HttpClient client = new(new Handler(async (request, cancellation) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/prompt", StringComparison.Ordinal))
            {
                posts++;
                using HttpRequestMessage forwarded = new(HttpMethod.Post, $"http://127.0.0.1:{port}/api/session/s/prompt")
                {
                    Content = new StringContent(await request.Content!.ReadAsStringAsync(cancellation), Encoding.UTF8, "application/json"),
                };
                return await wire.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellation);
            }
            if (path == "/api/event")
            {
                return Sse(new ConnectedStream());
            }
            if (request.Method != HttpMethod.Get)
            {
                return new(HttpStatusCode.NoContent);
            }
            return path switch
            {
                "/api/model" => Json("""{"data":[{"id":"m","providerID":"p","name":"Model","enabled":true,"status":"active"}]}"""),
                "/api/agent" => Json("""{"data":[{"id":"build","name":"Build","hidden":false,"mode":"primary"}]}"""),
                "/api/session/s" => Json("""{"data":{"id":"s","projectID":"project","location":{"directory":"/dir"},"model":{"id":"m","providerID":"p"},"agent":"build"}}"""),
                "/api/session/active" => Json("""{"data":{}}"""),
                "/api/session/s/message" => Json(revealHistory
                    ? JsonSerializer.Serialize(new { data = new[] { new { id = admittedId, type = "user", text = "original" } }, cursor = new { next = (string?)null } })
                    : """{"data":[],"cursor":{"next":null}}"""),
                _ => Json("""{"data":[]}"""),
            };
        }));
        using OpenCodeWorkspaceConnection api = Create(client);
        using LiveTaskWorkflow workflow = new(api, api);
        await workflow.OpenDirectoryAsync(new("/dir", "project"));
        await workflow.OpenAsync(new("s", "/dir", "project", "Task"));
        SessionTaskState state = workflow.Find("s")!;
        Assert.True(state.CanSend);
        await workflow.SendAsync(state, "original");
        await server.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.True(state.DeliveryUncertain);
        Assert.Equal(admittedId, state.Submission!.Id);
        Assert.False(state.CanSend);
        revealHistory = true;
        await workflow.RetryRecoveryAsync();
        Assert.Equal(WorkspaceRecovery.Current, workflow.Recovery);
        Assert.False(state.DeliveryUncertain);
        Assert.Single(state.Conversation, m => m.Id == admittedId);
        Assert.Equal(1, posts);

        async Task DropResponseAsync()
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(token);
            using NetworkStream stream = connection.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            Assert.StartsWith("POST /api/session/s/prompt ", (await reader.ReadLineAsync(token))!, StringComparison.Ordinal);
            int length = 0;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    length = int.Parse(line[15..], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            Assert.InRange(length, 1, 4096);
            char[] body = new char[length];
            Assert.Equal(length, await reader.ReadBlockAsync(body, token));
            using var document = JsonDocument.Parse(new string(body));
            admittedId = document.RootElement.GetProperty("id").GetString();
            // Admission is recorded, but the HTTP body is truncated on the actual socket.
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 256\r\nConnection: close\r\n\r\n{"), token);
        }
    }

    [Theory]
    [InlineData("stream.overflow")]
    [InlineData("stream.error")]
    public async Task EventOverflowIsRecoverableTransportFailure(string type)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"data: {{\"type\":\"{type}\",\"data\":{{}}}}\n\n"));
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Sse(stream))));
        using OpenCodeWorkspaceConnection api = Create(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Transport, error.Failure);
    }

    [Fact]
    public async Task ReconnectedStreamRechecksVersionBeforeAcceptingEvents()
    {
        int subscriptions = 0;
        using HttpClient client = new(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/info"
            ? Json("""{"version":"99.0.0","pid":1,"urls":[],"paths":{"tmp":"/tmp"}}""")
            : StreamResponse())));
        using OpenCodeWorkspaceConnection api = Create(client);
        await Assert.ThrowsAsync<WorkspaceException>(() => api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.WatchAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Protocol, error.Failure);
        Assert.Equal(1, subscriptions);

        HttpResponseMessage StreamResponse()
        {
            subscriptions++;
            return Sse(new MemoryStream());
        }
    }

    private sealed class ConnectedStream() : MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"server.connected\"}\n\n"))
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length)
            {
                return await base.ReadAsync(buffer, cancellationToken);
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
