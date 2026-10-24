using System.Net;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

public sealed partial class OpenCodeWorkspaceConnection : ITaskConnection
{
    public async Task<IReadOnlyList<string>> AttentionSessionsAsync(string directory, CancellationToken cancellationToken)
    {
        string[] permissions = await RequestAsync(HttpMethod.Get, "/api/permission/request?" + LocationQuery(directory), null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Select(p => Required(p, "sessionID")).ToArray(), cancellationToken).ConfigureAwait(false);
        string[] forms = await RequestAsync(HttpMethod.Get, "/api/form?" + LocationQuery(directory), null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Select(p => Required(p, "sessionID")).ToArray(), cancellationToken).ConfigureAwait(false);
        return permissions.Concat(forms).Distinct(StringComparer.Ordinal).ToArray();
    }
    public async Task<TaskCatalog> CatalogAsync(string directory, CancellationToken cancellationToken)
    {
        TaskModel[] models = await RequestAsync(HttpMethod.Get, "/api/model?" + LocationQuery(directory), null, 8388608,
            root => root.GetProperty("data").EnumerateArray().Where(m => m.GetProperty("enabled").GetBoolean() && Optional(m, "status") == "active")
                .Select(m => new TaskModel(Required(m, "id"), Required(m, "providerID"), Required(m, "name"))).ToArray(), cancellationToken).ConfigureAwait(false);
        TaskAgent[] agents = await RequestAsync(HttpMethod.Get, "/api/agent?" + LocationQuery(directory), null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Where(a => !a.GetProperty("hidden").GetBoolean() && Optional(a, "mode") is "primary" or "all")
                .Select(a => new TaskAgent(Required(a, "id"), Required(a, "name"))).ToArray(), cancellationToken).ConfigureAwait(false);
        return new(models, agents);
    }

    public async Task<TaskSnapshot> TaskSnapshotAsync(string id, CancellationToken cancellationToken)
    {
        (WorkspaceSession Session, string? Outcome, double? Idle, TaskModel? Model, string? Agent) metadata = await RequestAsync(HttpMethod.Get, Path(id), null, 1048576, root =>
        {
            JsonElement s = root.GetProperty("data");
            TaskModel? model = s.TryGetProperty("model", out JsonElement m) ? new(Required(m, "id"), Required(m, "providerID"), Required(m, "id")) : null;
            double? idle = s.TryGetProperty("time", out JsonElement time) && time.TryGetProperty("idle", out JsonElement i) ? i.GetDouble() : null;
            return (Session: Session(s), Outcome: Optional(s, "outcome"), Idle: idle, Model: model, Agent: Optional(s, "agent"));
        }, cancellationToken).ConfigureAwait(false);
        bool active = await RequestAsync(HttpMethod.Get, "/api/session/active", null, 1048576,
            root => root.GetProperty("data").TryGetProperty(id, out _), cancellationToken).ConfigureAwait(false);
        string[] inbox = await RequestAsync(HttpMethod.Get, Path(id) + "/inbox", null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Select(i => Required(i, "id")).ToArray(), cancellationToken).ConfigureAwait(false);
        TaskPermission[] permissions = await RequestAsync(HttpMethod.Get, Path(id) + "/permission", null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Select(p => new TaskPermission(Required(p, "id"), Required(p, "sessionID"), Required(p, "action"), Strings(p, "resources"), Strings(p, "save"))).ToArray(), cancellationToken).ConfigureAwait(false);
        TaskForm[] forms = await RequestAsync(HttpMethod.Get, Path(id) + "/form", null, 1048576,
            root => root.GetProperty("data").EnumerateArray().Select(Form).ToArray(), cancellationToken).ConfigureAwait(false);
        return new(metadata.Session, active, metadata.Outcome, metadata.Idle, metadata.Model, metadata.Agent, inbox, permissions, forms);
    }

    public Task SelectModelAsync(string id, TaskModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Mutation(HttpMethod.Post, Path(id) + "/model", new { model = new { id = model.Id, providerID = model.ProviderId } }, cancellationToken);
    }
    public Task SelectAgentAsync(string id, string agent, CancellationToken cancellationToken) => Mutation(HttpMethod.Post, Path(id) + "/agent", new { agent }, cancellationToken);
    public Task SubmitAsync(string id, TaskSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Task.Run(async () => _ = await RequestAsync(HttpMethod.Post, Path(id) + "/prompt",
        JsonSerializer.Serialize(new { id = submission.Id, text = submission.Text, delivery = "steer" }), 1048576, root =>
        {
            JsonElement data = root.GetProperty("data");
            if (Required(data, "id") != submission.Id || Required(data, "sessionID") != id)
            {
                throw Protocol();
            }

            return true;
        }, cancellationToken).ConfigureAwait(false), CancellationToken.None);
    }
    public async Task InterruptAsync(string id, CancellationToken cancellationToken) => _ = await RequestAsync(HttpMethod.Post, Path(id) + "/interrupt?resume=false", null, 1048576, root => root.GetProperty("interrupted").GetBoolean(), cancellationToken).ConfigureAwait(false);
    public Task ReplyPermissionAsync(string id, string requestId, string decision, CancellationToken cancellationToken) => Mutation(HttpMethod.Post, Path(id) + "/permission/" + Escape(requestId) + "/reply", new { decision }, cancellationToken);
    public Task<TaskForm> FormAsync(string id, string formId, CancellationToken cancellationToken) => RequestAsync(HttpMethod.Get, Path(id) + "/form/" + Escape(formId), null, 1048576, root => Form(root.GetProperty("data")), cancellationToken);
    public Task ReplyFormAsync(string id, string formId, IReadOnlyDictionary<string, object> answer, CancellationToken cancellationToken) => Mutation(HttpMethod.Post, Path(id) + "/form/" + Escape(formId) + "/reply", new { answer }, cancellationToken);
    public Task CancelFormAsync(string id, string formId, CancellationToken cancellationToken) => Mutation(HttpMethod.Delete, Path(id) + "/form/" + Escape(formId), null, cancellationToken);
    private Task<bool> Mutation(HttpMethod method, string path, object? body, CancellationToken token) => Task.Run(() => RequestAsync(method, path, body is null ? null : JsonSerializer.Serialize(body), 1048576, _ => true, token, HttpStatusCode.NoContent), CancellationToken.None);
    private static string Path(string id) => "/api/session/" + Escape(id);
    private static string[] Strings(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement a) ? a.EnumerateArray().Select(v => v.GetString() ?? throw Protocol()).ToArray() : [];
    private static object? FieldValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(v => v.GetString() ?? throw Protocol()).ToArray(),
        _ => null,
    };
    private static double? Number(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : null;
    private static int? Integer(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement n) ? n.GetInt32() : null;
    private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement b) && b.GetBoolean();
    private static TaskForm Form(JsonElement value) => new(Required(value, "id"), Required(value, "sessionID"), Required(value, "title", true),
        value.GetProperty("fields").EnumerateArray().Select(f => new TaskField(Required(f, "key"), Required(f, "type"))
        {
            Title = Optional(f, "title"),
            Description = Optional(f, "description"),
            Placeholder = Optional(f, "placeholder"),
            Required = Flag(f, "required"),
            Hidden = Flag(f, "hidden"),
            Custom = Flag(f, "custom"),
            Default = f.TryGetProperty("default", out JsonElement d) ? FieldValue(d) : null,
            Minimum = Number(f, "minimum"),
            Maximum = Number(f, "maximum"),
            MinLength = Integer(f, "minLength"),
            MaxLength = Integer(f, "maxLength"),
            MinItems = Integer(f, "minItems"),
            MaxItems = Integer(f, "maxItems"),
            Pattern = Optional(f, "pattern"),
            Format = Optional(f, "format"),
            Url = Optional(f, "url"),
            Options = f.TryGetProperty("options", out JsonElement options) ? options.EnumerateArray().Select(o => new TaskOption(Required(o, "value"), Required(o, "label"))).ToArray() : [],
            When = f.TryGetProperty("when", out JsonElement when) ? when.EnumerateArray().Select(c => new TaskCondition(Required(c, "key"), Required(c, "op"), FieldValue(c.GetProperty("value")) ?? throw Protocol())).ToArray() : [],
        }).ToArray(), value.TryGetProperty("state", out JsonElement state) ? Required(state, "status") : "pending");

    public Task WatchAsync(Func<TaskEvent, Task> receive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receive);
        string credential;
        CancellationTokenSource lifetime;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            credential = _password!;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        return Task.Run(async () =>
        {
            using (lifetime)
            using (HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/api/event", null, credential))
            {
                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("text/event-stream");
                using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                headers.CancelAfter(_deadline);
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new WorkspaceException(WorkspaceFailure.Unauthorized, "Authorization was rejected. Reconnect in Settings.");
                }

                if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/event-stream")
                {
                    throw Protocol();
                }

                using Stream stream = await response.Content.ReadAsStreamAsync(lifetime.Token).ConfigureAwait(false);
                await ReadEventsAsync(stream, receive, lifetime.Token).ConfigureAwait(false);
                throw new WorkspaceException(WorkspaceFailure.Transport, "Live updates disconnected. Refresh explicitly to reconcile; no input was retried.");
            }
        }, CancellationToken.None);
    }

    // Read characters into a bounded line/frame rather than ReadLineAsync, which can allocate an unbounded line.
    internal static async Task ReadEventsAsync(Stream stream, Func<TaskEvent, Task> receive, CancellationToken token)
    {
        using StreamReader reader = new(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true);
        char[] buffer = new char[4096];
        StringBuilder line = new(), data = new();
        int frameSize = 0;
        bool carriageReturn = false;
        async Task FinishLine()
        {
            string text = line.ToString();
            line.Clear();
            if (text.Length == 0)
            {
                frameSize = 0;
                if (data.Length == 0)
                {
                    return;
                }

                using var json = JsonDocument.Parse(data.ToString());
                data.Clear();
                JsonElement root = json.RootElement;
                string type = Required(root, "type");
                if (type is "server.error" or "stream.error" || type.EndsWith("overflow", StringComparison.Ordinal))
                {
                    throw Protocol();
                }

                JsonElement d = root.TryGetProperty("data", out JsonElement payload) ? payload : default;
                string? session = EventString(d, "sessionID");
                if (session is null && root.TryGetProperty("durable", out JsonElement durable))
                {
                    session = EventString(durable, "aggregateID");
                }

                await receive(new(EventString(root, "id") ?? "", type, session, EventString(d, "assistantMessageID"),
                    d.ValueKind == JsonValueKind.Object && d.TryGetProperty("ordinal", out JsonElement ordinal) && ordinal.ValueKind == JsonValueKind.Number && ordinal.TryGetInt32(out int index) ? index : 0,
                    EventString(d, "delta"), EventString(d, "id"), EventString(d, "name"))).ConfigureAwait(false);
            }
            else if (text.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(text.AsSpan(text.Length > 5 && text[5] == ' ' ? 6 : 5));
            }
        }
        while (true)
        {
            using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            readDeadline.CancelAfter(TimeSpan.FromSeconds(45));
            int count = await reader.ReadAsync(buffer.AsMemory(), readDeadline.Token).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                char ch = buffer[i];
                if (ch == '\n' && carriageReturn)
                {
                    carriageReturn = false;
                    continue;
                }
                carriageReturn = ch == '\r';
                if (++frameSize > 1048576)
                {
                    throw Size();
                }

                if (ch is '\r' or '\n')
                {
                    await FinishLine().ConfigureAwait(false);
                }
                else
                {
                    line.Append(ch);
                }
            }
        }
    }

    private static string? EventString(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out JsonElement property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
