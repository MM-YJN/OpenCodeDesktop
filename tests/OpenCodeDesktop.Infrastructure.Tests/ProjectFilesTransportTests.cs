using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class ProjectFilesTransportTests
{
    [Fact]
    public async Task HistoryRetainsStructuredUrisButDoesNotInventReferencesFromNamesOrText()
    {
        const string body = """{"data":[{"id":"u","type":"user","text":"mentioned.txt","files":[{"name":"inline.txt","source":{"type":"inline"}},{"name":"file.txt","source":{"type":"uri","uri":"file:///server/project/file.txt"}}]}],"cursor":{"next":null}}""";
        using HttpClient client = new(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using OpenCodeWorkspaceConnection api = Api(client);
        HistoryMessage message = Assert.Single((await api.HistoryAsync("session", null, TestContext.Current.CancellationToken)).Data);
        Assert.Equal(new HistoryFileReference("file:///server/project/file.txt", "file.txt"), Assert.Single(message.FileReferences));
    }

    [Fact]
    public async Task ListFindAndReadUseServerLocationAndEncodedOpaquePaths()
    {
        List<string> requests = [];
        using HttpClient client = new(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.NotNull(request.Headers.Authorization);
            requests.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.StartsWith("/api/fs/read/", StringComparison.Ordinal))
            {
                Assert.Equal("bytes=0-262143", request.Headers.Range!.ToString());
                return Bytes(Encoding.UTF8.GetBytes("Hello 世界"));
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"location":{"directory":"C:\\server 空"},"data":[{"path":"nested 空\\hello #%.txt","type":"file"}]}""") };
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        Assert.Equal("nested 空\\hello #%.txt", Assert.Single((await api.ListFilesAsync("C:\\server 空", "nested 空", TestContext.Current.CancellationToken)).Entries).Path);
        await api.FindFilesAsync("C:\\server 空", "hello &?#", TestContext.Current.CancellationToken);
        Assert.Equal("Hello 世界", (await api.ReadFileAsync("C:\\server 空", "nested 空\\hello #%.txt", TestContext.Current.CancellationToken)).Text);
        Assert.All(requests, path => Assert.Contains("location%5Bdirectory%5D=C%3A%5Cserver%20%E7%A9%BA", path, StringComparison.Ordinal));
        Assert.Contains("&path=nested%20%E7%A9%BA", requests[0], StringComparison.Ordinal);
        Assert.Contains("type=file&limit=100&query=hello%20%26%3F%23", requests[1], StringComparison.Ordinal);
        Assert.Contains("/api/fs/read/nested%20%E7%A9%BA%5Chello%20%23%25.txt?", requests[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundedStreamsHandleRangeAndIgnoredRangeWithoutUnboundedBuffering(bool range)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 300000));
        using HttpClient client = new(new Handler(_ =>
        {
            HttpResponseMessage response = Bytes(range ? bytes[..ProjectFileLimits.PreviewBytes] : bytes);
            if (range)
            {
                response.StatusCode = HttpStatusCode.PartialContent;
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, ProjectFileLimits.PreviewBytes - 1, bytes.Length);
            }
            return response;
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectFilePreview result = await api.ReadFileAsync("/project", "large", TestContext.Current.CancellationToken);
        Assert.True(result.Truncated);
        Assert.Equal(ProjectFileLimits.PreviewCharacters, result.Text.Length);
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 2, 255 }, FilePreviewKind.Unsupported, "")]
    [InlineData(new byte[] { 255 }, FilePreviewKind.Unsupported, "")]
    [InlineData(new byte[] { 255, 254, 65, 0 }, FilePreviewKind.Text, "A")]
    [InlineData(new byte[] { 239, 187, 191, 65 }, FilePreviewKind.Text, "A")]
    [InlineData(new byte[] { }, FilePreviewKind.Text, "")]
    public async Task EncodingAndBinaryStatesAreHonest(byte[] bytes, FilePreviewKind kind, string text)
    {
        using HttpClient client = new(new Handler(_ => Bytes(bytes)));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectFilePreview result = await api.ReadFileAsync("/project", "file", TestContext.Current.CancellationToken);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public async Task EmptyRangeAndTruncatedUtf8BoundaryAreHandledWithoutReplacementCharacters()
    {
        int calls = 0;
        using HttpClient client = new(new Handler(_ =>
        {
            if (++calls == 1)
            {
                HttpResponseMessage empty = Bytes([]);
                empty.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable;
                empty.Content.Headers.ContentRange = new ContentRangeHeaderValue(0);
                return empty;
            }
            HttpResponseMessage partial = Bytes([65, 0xE4, 0xB8]);
            partial.StatusCode = HttpStatusCode.PartialContent;
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 4);
            return partial;
        }));
        using OpenCodeWorkspaceConnection api = Api(client);
        Assert.Equal("", (await api.ReadFileAsync("/project", "empty", TestContext.Current.CancellationToken)).Text);
        ProjectFilePreview result = await api.ReadFileAsync("/project", "utf8", TestContext.Current.CancellationToken);
        Assert.Equal("A", result.Text);
        Assert.True(result.Truncated);
    }

    [Theory]
    [InlineData(403, WorkspaceFailure.Unauthorized)]
    [InlineData(404, WorkspaceFailure.NotFound)]
    [InlineData(500, WorkspaceFailure.Http)]
    public async Task ErrorsDoNotExposeBodies(int status, WorkspaceFailure expected)
    {
        using HttpClient client = new(new Handler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret path and credentials") }));
        using OpenCodeWorkspaceConnection api = Api(client);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ReadFileAsync("/project", "missing", TestContext.Current.CancellationToken));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsBoundEntriesAndRejectDifferentLocations()
    {
        string body = JsonSerializer.Serialize(new { location = new { directory = "/project" }, data = Enumerable.Range(0, 2100).Select(i => new { path = "file" + i, type = "file" }) });
        using HttpClient client = new(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectFileList result = await api.ListFilesAsync("/project", "", TestContext.Current.CancellationToken);
        Assert.Equal(2000, result.Entries.Count);
        Assert.True(result.Limited);
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ListFilesAsync("/other", "", TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceFailure.Protocol, error.Failure);
    }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Theory]
    [InlineData(false, WorkspaceFailure.Timeout)]
    [InlineData(true, WorkspaceFailure.Cancelled)]
    public async Task FileReadsRespectDeadlineAndCallerCancellation(bool callerCancels, WorkspaceFailure expected)
    {
        using HttpClient client = new(new DelayedHandler());
        ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address);
        using OpenCodeWorkspaceConnection api = new(client, address!, "test", callerCancels ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(50));
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (callerCancels) { cancel.CancelAfter(50); }
        WorkspaceException error = await Assert.ThrowsAsync<WorkspaceException>(() => api.ReadFileAsync("/project", "slow", cancel.Token));
        Assert.Equal(expected, error.Failure);
    }

    [Fact]
    public async Task AnIgnoredRangeConsumesOnlyTheBoundedPrefixOfAnUnknownLengthStream()
    {
        using GeneratedStream stream = new();
        using HttpClient client = new(new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using OpenCodeWorkspaceConnection api = Api(client);
        ProjectFilePreview preview = await api.ReadFileAsync("/project", "unbounded", TestContext.Current.CancellationToken);
        Assert.True(preview.Truncated);
        Assert.Equal(ProjectFileLimits.PreviewBytes + 1, stream.BytesRead);
        Assert.Equal(ProjectFileLimits.PreviewCharacters, preview.Text.Length);
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class GeneratedStream : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Fill((byte)'x');
            BytesRead += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static OpenCodeWorkspaceConnection Api(HttpClient client)
    {
        ServerAddress.TryParse("http://127.0.0.1:4096", out ServerAddress? address);
        return new(client, address!, "test credential");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
