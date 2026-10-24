using System.Net;
using System.Net.Sockets;
using System.Text;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Infrastructure.Connections;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class OpenCodeConnectionServiceTests
{
    private const string Identity = """{"version":"2.1.4","pid":42,"urls":["http://ignored.invalid"],"paths":{"tmp":"not-a-client-path"},"capabilities":null,"extra":true}""";

    [Fact]
    public async Task CorrectRequestMapsIdentityWithoutFollowingReturnedLocations()
    {
        int calls = 0;
        using HttpClient client = new(new Handler((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:4096/api/info", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("opencode:pāss:word", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            Assert.Empty(request.RequestUri.UserInfo);
            return Task.FromResult(Json(Identity));
        }));
        OpenCodeConnectionService service = new(client);
        ConnectionResult result = await service.ProbeAsync(Address(), "pāss:word", CancellationToken.None);
        Assert.True(result.IsReady);
        Assert.Equal("2.1.4", result.Version);
        Assert.Equal(1, calls);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.DoesNotContain("pāss", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":0,\"urls\":[],\"paths\":{}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":-1,\"urls\":[],\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":1.5,\"urls\":[],\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":\"0\",\"urls\":[],\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":0,\"urls\":[5],\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":0,\"urls\":{},\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":0,\"urls\":[],\"paths\":null}")]
    [InlineData("{\"version\":4,\"pid\":0,\"urls\":[],\"paths\":{\"tmp\":\"x\"}}")]
    [InlineData("{\"version\":\"2.1.4\",\"pid\":0,\"urls\":[],\"paths\":{\"tmp\":false}}")]
    public async Task InvalidBodiesNeverProduceReady(string body)
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(body))));
        ConnectionResult result = await new OpenCodeConnectionService(client).ProbeAsync(Address(), "secret", CancellationToken.None);
        Assert.Equal(ConnectionOutcome.Incompatible, result.Outcome);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task UnsupportedVersionAndOversizedBodyAreRejected()
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(Json(Identity.Replace("2.1.4", "2.1.5", StringComparison.Ordinal)))));
        ConnectionResult version = await new OpenCodeConnectionService(client).ProbeAsync(Address(), "secret", CancellationToken.None);
        Assert.Equal(ConnectionOutcome.Incompatible, version.Outcome);
        Assert.Contains("2.1.5", version.Message, StringComparison.Ordinal);
        Assert.Contains("2.1.4", version.Message, StringComparison.Ordinal);
        using HttpClient oversized = new(new Handler((_, _) => Task.FromResult(Json(new string('x', 65537)))));
        Assert.Equal(ConnectionOutcome.Incompatible, (await new OpenCodeConnectionService(oversized).ProbeAsync(Address(), "secret", CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(401, ConnectionOutcome.AuthenticationFailed)]
    [InlineData(503, ConnectionOutcome.Unavailable)]
    [InlineData(500, ConnectionOutcome.InitializationFailed)]
    [InlineData(400, ConnectionOutcome.HttpFailure)]
    [InlineData(404, ConnectionOutcome.HttpFailure)]
    [InlineData(302, ConnectionOutcome.HttpFailure)]
    [InlineData(403, ConnectionOutcome.HttpFailure)]
    public async Task StatusFailuresAreActionableAndBodiesDoNotLeak(int code, ConnectionOutcome expected)
    {
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
        {
            Content = new StringContent("private-secret-server-body"),
        })));
        ConnectionResult result = await new OpenCodeConnectionService(client).ProbeAsync(Address(), "private-secret", CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
        Assert.DoesNotContain("private-secret", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TotalDeadlineCoversHeadersAndBody(bool stallBody)
    {
        using HttpClient client = new(new Handler(async (_, token) =>
        {
            if (stallBody)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            }

            await Task.Delay(Timeout.Infinite, token);
            return Json(Identity);
        }));
        ConnectionResult result = await new OpenCodeConnectionService(client, TimeSpan.FromMilliseconds(150)).ProbeAsync(Address(), "secret", CancellationToken.None);
        Assert.Equal(ConnectionOutcome.TimedOut, result.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCancellationIsNotTimeout(bool stallBody)
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient client = new(new Handler(async (_, token) =>
        {
            entered.SetResult();
            if (stallBody)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            }

            await Task.Delay(Timeout.Infinite, token);
            return Json(Identity);
        }));
        using CancellationTokenSource cancel = new();
        Task<ConnectionResult> attempt = new OpenCodeConnectionService(client).ProbeAsync(Address(), "secret", cancel.Token);
        await entered.Task;
        await cancel.CancelAsync();
        Assert.Equal(ConnectionOutcome.Cancelled, (await attempt).Outcome);
    }

    [Theory]
    [InlineData(true, ConnectionOutcome.Refused)]
    [InlineData(false, ConnectionOutcome.TransportFailure)]
    public async Task TransportExceptionsAreMappedWithoutRawDetails(bool refused, ConnectionOutcome expected)
    {
        using HttpClient client = new(new Handler((_, _) => throw (refused
            ? new HttpRequestException("secret", new SocketException((int)SocketError.ConnectionRefused))
            : new HttpRequestException("secret"))));
        ConnectionResult result = await new OpenCodeConnectionService(client).ProbeAsync(Address(), "secret", CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
        Assert.DoesNotContain("secret", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseStreamIsDisposedOnInvalidBody()
    {
        TrackedStream stream = new();
        using HttpClient client = new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        })));
        ConnectionResult result = await new OpenCodeConnectionService(client).ProbeAsync(Address(), "secret", CancellationToken.None);
        Assert.Equal(ConnectionOutcome.Incompatible, result.Outcome);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ProductionClientDoesNotFollowRedirects()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        var reply = Task.Run(async () =>
        {
            using TcpClient socket = await listener.AcceptTcpClientAsync(timeout.Token);
            using NetworkStream stream = socket.GetStream();
            byte[] buffer = new byte[4096];
            Assert.True(await stream.ReadAsync(buffer, timeout.Token) > 0);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        }, timeout.Token);
        using HttpClient client = OpenCodeConnectionService.CreateHttpClient();
        Assert.True(ServerAddress.TryParse($"http://127.0.0.1:{port}", out ServerAddress? address));
        ConnectionResult result = await new OpenCodeConnectionService(client).ProbeAsync(address!, "secret", timeout.Token);
        await reply;
        Assert.Equal(ConnectionOutcome.HttpFailure, result.Outcome);
        Assert.False(listener.Pending());
    }

    private static ServerAddress Address()
    {
        Assert.True(ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address));
        return address!;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class TrackedStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
