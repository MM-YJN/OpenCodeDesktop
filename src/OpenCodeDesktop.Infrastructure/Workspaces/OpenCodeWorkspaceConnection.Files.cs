using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

public sealed partial class OpenCodeWorkspaceConnection : IProjectFilesConnection
{
    public Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/fs/list?" + LocationQuery(directory) + "&path=" + Escape(path), null, ProjectFileLimits.ListBytes,
        root => FileList(root, directory, ProjectFileLimits.Entries), cancellationToken);

    public Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/fs/find?" + LocationQuery(directory) + "&type=file&limit=100&query=" + Escape(query), null, ProjectFileLimits.ListBytes,
        root => FileList(root, directory, ProjectFileLimits.SearchResults), cancellationToken);

    private static ProjectFileList FileList(JsonElement root, string directory, int limit)
    {
        if (Required(root.GetProperty("location"), "directory") != directory)
        {
            throw Protocol();
        }
        JsonElement data = root.GetProperty("data");
        List<ProjectFileEntry> entries = [];
        foreach (JsonElement entry in data.EnumerateArray().Take(limit))
        {
            string type = Required(entry, "type");
            if (type is not ("file" or "directory"))
            {
                throw Protocol();
            }
            entries.Add(new(Required(entry, "path"), type == "directory"));
        }
        return new(entries, data.GetArrayLength() >= limit);
    }

    public Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken)
    {
        string credential;
        CancellationTokenSource timeout;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new WorkspaceException(WorkspaceFailure.Cancelled, "Connection closed.");
            }
            credential = _password!;
            timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        timeout.CancelAfter(_deadline);
        return Task.Run(async () =>
        {
            using (timeout)
            {
                try
                {
                    // Encode the entire opaque wildcard, including Windows separators. Never resolve locally.
                    using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/api/fs/read/" + Escape(path) + "?" + LocationQuery(directory), null, credential);
                    request.Headers.Range = new RangeHeaderValue(0, ProjectFileLimits.PreviewBytes - 1);
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && range is { Length: 0, HasRange: false })
                    {
                        return new ProjectFilePreview(FilePreviewKind.Text, "", false);
                    }
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        throw new WorkspaceException(WorkspaceFailure.Unauthorized, "Access denied.");
                    }
                    if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                    {
                        throw new WorkspaceException(response.StatusCode == HttpStatusCode.NotFound ? WorkspaceFailure.NotFound : WorkspaceFailure.Http, "Unable to read the server file.");
                    }
                    bool partial = response.StatusCode == HttpStatusCode.PartialContent;
                    if (partial && (range is not { From: 0, To: >= 0, Length: > 0 } || range.To >= range.Length || range.To >= ProjectFileLimits.PreviewBytes))
                    {
                        throw Protocol();
                    }
                    using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using MemoryStream buffer = new();
                    byte[] chunk = new byte[16384];
                    while (buffer.Length <= ProjectFileLimits.PreviewBytes)
                    {
                        int read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ProjectFileLimits.PreviewBytes + 1 - (int)buffer.Length)), timeout.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        buffer.Write(chunk, 0, read);
                    }
                    timeout.Token.ThrowIfCancellationRequested();
                    if (partial && buffer.Length != range!.To + 1)
                    {
                        throw Protocol();
                    }
                    bool truncated = buffer.Length > ProjectFileLimits.PreviewBytes || partial && range!.Length > buffer.Length;
                    int count = Math.Min((int)buffer.Length, ProjectFileLimits.PreviewBytes);
                    string? media = response.Content.Headers.ContentType?.MediaType;
                    if (media is not null && (media.StartsWith("image/", StringComparison.Ordinal) || media.StartsWith("audio/", StringComparison.Ordinal) ||
                        media.StartsWith("video/", StringComparison.Ordinal) || media is "application/pdf" or "application/zip"))
                    {
                        return new ProjectFilePreview(FilePreviewKind.Unsupported, "", truncated);
                    }
                    return DecodePreview(buffer.GetBuffer().AsSpan(0, count), truncated);
                }
                catch (OperationCanceledException)
                {
                    throw new WorkspaceException(cancellationToken.IsCancellationRequested || _disposed ? WorkspaceFailure.Cancelled : WorkspaceFailure.Timeout, "File loading cancelled or timed out.");
                }
                catch (Exception error) when (error is HttpRequestException or IOException)
                {
                    throw new WorkspaceException(WorkspaceFailure.Transport, "The connection was interrupted.");
                }
            }
        });
    }

    private static ProjectFilePreview DecodePreview(ReadOnlySpan<byte> bytes, bool truncated)
    {
        Encoding encoding = new UTF8Encoding(false, true);
        int skip = 0;
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            skip = 3;
        }
        else if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = new UnicodeEncoding(false, true, true);
            skip = 2;
        }
        else if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = new UnicodeEncoding(true, true, true);
            skip = 2;
        }
        try
        {
            Decoder decoder = encoding.GetDecoder();
            char[] characters = new char[encoding.GetMaxCharCount(bytes.Length)];
            decoder.Convert(bytes[skip..], characters, !truncated, out _, out int used, out _);
            string text = new(characters, 0, used);
            if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t' or '\f')))
            {
                return new(FilePreviewKind.Unsupported, "", truncated);
            }
            if (text.Length > ProjectFileLimits.PreviewCharacters)
            {
                int length = ProjectFileLimits.PreviewCharacters;
                if (char.IsHighSurrogate(text[length - 1]))
                {
                    length--;
                }
                text = text[..length];
                truncated = true;
            }
            return new(FilePreviewKind.Text, text, truncated);
        }
        catch (DecoderFallbackException)
        {
            return new(FilePreviewKind.Unsupported, "", truncated);
        }
    }
}
