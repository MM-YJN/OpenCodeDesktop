using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

/// <summary>One immutable origin and credential lifetime. Parsing and transport execute on the thread pool.</summary>
public sealed partial class OpenCodeWorkspaceConnection(HttpClient client, ServerAddress address, string password, TimeSpan? deadline = null) : IWorkspaceConnection
{
    private string? _password = password;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _deadline = deadline ?? TimeSpan.FromSeconds(15);
    private readonly object _gate = new();
    private volatile bool _disposed;

    public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/location?" + LocationQuery(directory), null, 1048576,
        root => new ProjectLocation(Required(root, "directory"), Required(root.GetProperty("project"), "id")), cancellationToken);

    public async Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => _ = await RequestAsync(
        HttpMethod.Get, "/api/fs/list?" + LocationQuery(directory), null, 1048576, root =>
        {
            if (Required(root.GetProperty("location"), "directory") != directory || root.GetProperty("data").ValueKind != JsonValueKind.Array)
            {
                throw Protocol();
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);

    public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/session?directory=" + Escape(directory) + PageQuery(cursor), null, 1048576,
        root => Page(root, Session), cancellationToken);

    public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Post, "/api/session", JsonSerializer.Serialize(new { location = new { directory } }), 1048576,
        root => Session(root.GetProperty("data")), cancellationToken);

    public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/session/" + Escape(id), null, 1048576, root => Session(root.GetProperty("data")), cancellationToken);

    public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => RequestAsync(
        HttpMethod.Get, "/api/session/" + Escape(id) + "/message?" + PageQuery(cursor).TrimStart('&'), null, 8388608,
        root => Page(root, Message), cancellationToken);

    private static string LocationQuery(string directory) => "location%5Bdirectory%5D=" + Escape(directory);
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string PageQuery(string? cursor) => "&limit=50" + (cursor is null ? "&order=desc" : "&cursor=" + Escape(cursor));

    private Task<T> RequestAsync<T>(HttpMethod method, string path, string? body, int limit, Func<JsonElement, T> map, CancellationToken cancellationToken, HttpStatusCode expected = HttpStatusCode.OK)
    {
        // Capture only the active credential, never expose it in public state or diagnostics.
        string credential;
        CancellationTokenSource timeout;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new WorkspaceException(WorkspaceFailure.Cancelled, "Connection closed. Reconnect to continue.");
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
                    using HttpRequestMessage request = CreateRequest(method, path, body, credential);

                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new WorkspaceException(WorkspaceFailure.Unauthorized, "Authorization was rejected. Reconnect in Settings.");
                    }

                    if (response.StatusCode != expected)
                    {
                        throw new WorkspaceException(response.StatusCode == HttpStatusCode.NotFound ? WorkspaceFailure.NotFound : WorkspaceFailure.Http,
                        $"The server returned HTTP {(int)response.StatusCode}. Check the server directory or session and retry. Redirects are not followed.");
                    }

                    if (expected == HttpStatusCode.NoContent)
                    {
                        return map(default);
                    }

                    if (response.Content.Headers.ContentLength > limit)
                    {
                        throw Size();
                    }

                    using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using MemoryStream buffer = new();
                    byte[] chunk = new byte[16384];
                    while (true)
                    {
                        int read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, limit + 1 - (int)buffer.Length)), timeout.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                        if (buffer.Length > limit)
                        {
                            throw Size();
                        }
                    }
                    timeout.Token.ThrowIfCancellationRequested();
                    using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
                    T result = map(document.RootElement);
                    timeout.Token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw cancellationToken.IsCancellationRequested || _disposed
                        ? new WorkspaceException(WorkspaceFailure.Cancelled, "Loading cancelled. Retry when ready.")
                        : new WorkspaceException(WorkspaceFailure.Timeout, "Loading timed out after 15 seconds. Check the server and retry.");
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    throw Protocol();
                }
                catch (Exception error) when (error is HttpRequestException or IOException)
                {
                    throw new WorkspaceException(WorkspaceFailure.Transport, "The connection was interrupted. Check the server and retry.");
                }
            }
        });
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? body, string credential)
    {
        HttpRequestMessage request = new(method, new Uri(address.Origin, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + credential)));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private static WorkspaceSession Session(JsonElement value) => new(Required(value, "id"), Required(value.GetProperty("location"), "directory"),
        Required(value, "projectID"), Optional(value, "title") is { Length: > 0 } title ? title : "Untitled session")
        { Archived = value.TryGetProperty("time", out JsonElement time) && time.TryGetProperty("archived", out JsonElement archived) && archived.ValueKind == JsonValueKind.Number };

    private static WorkspacePage<T> Page<T>(JsonElement root, Func<JsonElement, T> map)
    {
        JsonElement data = root.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw Protocol();
        }

        JsonElement next = root.GetProperty("cursor").GetProperty("next");
        if (next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
        {
            throw Protocol();
        }

        string? cursor = next.GetString();
        if (cursor is { Length: 0 } || cursor?.Length > 65536)
        {
            throw Protocol();
        }

        return new(data.EnumerateArray().Select(map).ToArray(), cursor);
    }

    private static HistoryMessage Message(JsonElement value)
    {
        string id = Required(value, "id");
        string type = Required(value, "type");
        IReadOnlyList<HistoryContentPart> parts = type switch
        {
            "assistant" => AssistantParts(value),
            "shell" => [ShellPart(value)],
            _ => [],
        };
        string text = type switch
        {
            "user" or "system" or "synthetic" => Required(value, "text", allowEmpty: true),
            "assistant" => string.Join("\n\n", parts.Select(part => part.Text)),
            "shell" => Optional(value, "output") ?? "",
            "idle" => "Recorded idle activity.",
            "compaction" => Optional(value, "status") is { Length: > 0 } status ? "Context compacted · " + status : "Context compacted",
            "skill" => "Used skill: " + (Optional(value, "name") ?? "unknown"),
            "agent-switched" => "Agent changed: " + (Optional(value, "agent") ?? "unknown"),
            "model-switched" => "Model changed",
            "location-switched" => "Directory changed",
            _ => "Unsupported historical entry. This client cannot display this variant.",
        };
        HistoryFailureOutcome? failure = null;
        bool isComplete = type != "assistant" || value.TryGetProperty("time", out JsonElement time) && time.ValueKind == JsonValueKind.Object &&
            time.TryGetProperty("completed", out JsonElement completed) && completed.ValueKind == JsonValueKind.Number;
        if (type == "assistant" && isComplete && Optional(value, "finish") == "error" &&
            value.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            failure = Optional(error, "type") == "aborted" ? HistoryFailureOutcome.Interrupted : HistoryFailureOutcome.Failed;
        }

        string[] changedFiles = type == "assistant" && value.TryGetProperty("snapshot", out JsonElement snapshot) &&
            snapshot.TryGetProperty("files", out JsonElement changed) && changed.ValueKind == JsonValueKind.Array
            ? changed.EnumerateArray().Select(f => f.GetString() ?? throw Protocol()).Take(ReviewLimits.Files).ToArray() : [];
        return new(id, type, text) { CompletedFailure = failure, IsComplete = isComplete, Parts = parts, FileReferences = MessageFileReferences(value, type), ChangedFiles = changedFiles };
    }

    private static IReadOnlyList<HistoryFileReference> MessageFileReferences(JsonElement value, string type)
    {
        List<HistoryFileReference> references = [];
        if (type == "user" && value.TryGetProperty("files", out JsonElement files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (file.TryGetProperty("source", out JsonElement source) && source.ValueKind == JsonValueKind.Object &&
                    Optional(source, "type") == "uri" && Optional(source, "uri") is { } uri)
                {
                    references.Add(new(uri, Optional(file, "name")));
                }
                else
                {
                    // Inline history is a label only: never expose data URLs or render arbitrary content.
                    references.Add(new("", Optional(file, "name") ?? "Inline attachment"));
                }
            }
        }
        if (type == "assistant" && value.TryGetProperty("content", out JsonElement parts))
        {
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (Optional(part, "type") != "tool" || !part.TryGetProperty("state", out JsonElement state) ||
                    !state.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) { continue; }
                foreach (JsonElement item in content.EnumerateArray())
                {
                    if (Optional(item, "type") == "file" && Optional(item, "uri") is { } uri) { references.Add(new(uri, Optional(item, "name"))); }
                }
            }
        }
        return references.Take(100).ToArray();
    }

    private static List<HistoryContentPart> AssistantParts(JsonElement value)
    {
        List<HistoryContentPart> content = [];
        foreach (JsonElement part in value.GetProperty("content").EnumerateArray())
        {
            HistoryContentPart mapped = Required(part, "type") switch
            {
                "text" => new(content.Count, Required(part, "text", allowEmpty: true), HistoryPartKind.Text),
                "reasoning" => new(content.Count, Optional(part, "text") ?? "", HistoryPartKind.Reasoning),
                "tool" => ToolPart(part, content.Count),
                _ => new(content.Count, "A recorded content block is not supported by this client.", HistoryPartKind.Text),
            };
            content.Add(mapped);
        }
        if (value.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null)
        {
            content.Add(new(content.Count, "A recorded assistant error occurred.", HistoryPartKind.Error));
        }

        return content;
    }

    private static HistoryContentPart ShellPart(JsonElement value) => new(0, Optional(value, "output") ?? "", HistoryPartKind.Tool)
    {
        ToolName = "shell",
        ToolInput = Optional(value, "command"),
        ToolStatus = Optional(value, "status"),
        ToolFailed = Optional(value, "status") == "error",
    };

    private static HistoryContentPart ToolPart(JsonElement part, int ordinal)
    {
        JsonElement state = part.GetProperty("state");
        List<string> output = [];
        if (state.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in content.EnumerateArray())
            {
                output.Add(Optional(item, "type") == "text" ? Required(item, "text", allowEmpty: true) : "Recorded file or unsupported tool content.");
            }
        }

        return new(ordinal, string.Join("\n", output), HistoryPartKind.Tool)
        {
            ToolName = Optional(part, "name"),
            ToolInput = ToolInputSummary(state),
            ToolStatus = Optional(state, "status"),
            ToolFailed = state.TryGetProperty("error", out JsonElement toolError) && toolError.ValueKind == JsonValueKind.Object,
        };
    }

    private static string? ToolInputSummary(JsonElement state)
    {
        if (!state.TryGetProperty("input", out JsonElement input))
        {
            return null;
        }

        if (input.ValueKind == JsonValueKind.String)
        {
            return SingleLine(input.GetString());
        }

        if (input.ValueKind == JsonValueKind.Object)
        {
            foreach (string key in new[] { "command", "filePath", "path", "pattern", "query", "url" })
            {
                if (input.TryGetProperty(key, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.String &&
                    SingleLine(candidate.GetString()) is { Length: > 0 } summary)
                {
                    return summary;
                }
            }
        }

        return null;
    }

    private static string? SingleLine(string? value)
    {
        string? line = value?.Split('\n')[0].Trim();
        return line?.Length > 96 ? line[..96] + "…" : line;
    }

    private static string Required(JsonElement value, string name, bool allowEmpty = false)
    {
        string? text = Optional(value, name);
        return text is null || (!allowEmpty && string.IsNullOrWhiteSpace(text)) ? throw Protocol() : text;
    }

    private static string? Optional(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement property) && property.ValueKind != JsonValueKind.Null
        ? property.GetString() : null;
    private static WorkspaceException Protocol() => new(WorkspaceFailure.Protocol, "The server returned an incompatible or malformed response. Retry or check the supported server version.");
    private static WorkspaceException Size() => new(WorkspaceFailure.Size, "The response exceeds this client's size limit. No data was truncated. Retry with a smaller server-side history.");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _password = null;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
