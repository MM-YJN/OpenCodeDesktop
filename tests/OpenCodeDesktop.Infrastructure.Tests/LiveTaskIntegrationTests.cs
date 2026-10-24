using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Connections;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class LiveTaskIntegrationTests
{
    [Fact]
    public async Task ProductionClientRunsFixtureTaskAnswersQuestionAndInterruptsPinnedService()
    {
        string? executable = Environment.GetEnvironmentVariable("OPENCODE_TASK_TEST_EXECUTABLE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(executable), "Opt-in: OPENCODE_TASK_TEST_EXECUTABLE must identify the pinned 2.1.4 CLI; Node is required. Uses a deterministic local provider only.");
        using (FileStream binary = File.OpenRead(executable!))
        {
            Assert.Equal("B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399", Convert.ToHexString(await SHA256.HashDataAsync(binary, TestContext.Current.CancellationToken)));
        }

        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "docs/integration/task-provider-fixture.mjs")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        ProcessStartInfo start = new("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root, "docs/integration/task-provider-fixture.mjs"));
        using Process provider = Process.Start(start)!;
        _ = provider.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            string? providerUrl = await provider.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.StartsWith("http://127.0.0.1:", providerUrl!, StringComparison.Ordinal);
            string configuration = JsonSerializer.Serialize(new
            {
                model = "wi007/fixture",
                providers = new Dictionary<string, object>
                {
                    ["wi007"] = new
                    {
                        name = "WI008 deterministic local fixture",
                        package = "@opencode/ai/providers/openai-compatible",
                        settings = new { baseURL = providerUrl + "/v1", apiKey = "fixture-only", encoding = "none" },
                        models = new Dictionary<string, object> { ["fixture"] = new { capabilities = new { tools = true }, limit = new { context = 32000, output = 1000 } } },
                    }
                },
            });
            await using LiveConnectionTests.IsolatedServer server = await LiveConnectionTests.IsolatedServer.StartAsync(executable!, configuration);
            using HttpClient client = OpenCodeConnectionService.CreateHttpClient();
            using ServerConnections connections = new(new OpenCodeConnectionService(client));
            ServerProfile profile = connections.Save(null, "Fixture", server.Origin)!;
            await LiveConnectionTests.WaitForReadyAsync(connections, profile.Id, server.Password);
            OpenCodeWorkspaceConnection api = Assert.IsType<OpenCodeWorkspaceConnection>(connections.Context);
            CancellationToken token = TestContext.Current.CancellationToken;
            ProjectLocation location = await api.ResolveAsync(server.WorkspaceDirectory, token);
            TaskCatalog catalog = await api.CatalogAsync(location.Directory, token);
            TaskModel model = Assert.Single(catalog.Models, m => m.ProviderId == "wi007");
            Assert.Contains(catalog.Agents, a => a.Id == "build");
            WorkspaceSession session = await api.CreateAsync(location.Directory, token);
            await api.SelectModelAsync(session.Id, model, token);
            await api.SelectAgentAsync(session.Id, "build", token);
            TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Collections.Concurrent.ConcurrentQueue<TaskEvent> events = new();
            using var watchLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task watch = api.WatchAsync(e =>
            {
                events.Enqueue(e);
                if (e.Type == "server.connected")
                {
                    connected.TrySetResult();
                }

                return Task.CompletedTask;
            }, watchLifetime.Token);
            try
            {
                await connected.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                string inputId = "msg_wi008" + Guid.NewGuid().ToString("N");
                await api.SubmitAsync(session.Id, new(inputId, "WI007:complete"), token);
                TaskSnapshot completed = await WaitAsync(api, session.Id, s => !s.Active && s.Outcome == "succeeded", token);
                Assert.NotNull(completed.Idle);
                WorkspacePage<HistoryMessage> history = await api.HistoryAsync(session.Id, null, token);
                Assert.Equal(1, history.Data.Count(m => m.Id == inputId));
                Assert.Contains(history.Data, m => m.Kind == "assistant" && m.Text.Contains("verified fixture", StringComparison.Ordinal));
                await api.SubmitAsync(session.Id, new("msg_wi008" + Guid.NewGuid().ToString("N"), "WI007:question"), token);
                TaskSnapshot question = await WaitAsync(api, session.Id, s => s.Forms.Count > 0, token);
                TaskForm form = await api.FormAsync(session.Id, Assert.Single(question.Forms).Id, token);
                Assert.Equal(session.Id, form.SessionId);
                TaskField field = Assert.Single(form.Fields);
                await api.ReplyFormAsync(session.Id, form.Id, new Dictionary<string, object> { [field.Key] = field.Options[0].Value }, token);
                await WaitAsync(api, session.Id, s => !s.Active && s.Forms.Count == 0 && s.Outcome == "succeeded", token);
                await api.SubmitAsync(session.Id, new("msg_wi008" + Guid.NewGuid().ToString("N"), "WI007:stall"), token);
                await WaitAsync(api, session.Id, s => s.Active, token);
                await api.InterruptAsync(session.Id, token);
                await WaitAsync(api, session.Id, s => !s.Active && s.Outcome == "interrupted", token);
                Assert.Contains(events, e => e.Type == "session.text.delta" && e.SessionId == session.Id);
                Assert.Contains(events, e => e.Type == "form.created");

                using LiveTaskWorkflow recovering = new(api, api);
                await recovering.OpenDirectoryAsync(location);
                await recovering.OpenAsync(session);
                TaskCompletionSource interruptedUpdates = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                recovering.Changed += (_, _) =>
                {
                    if (recovering.Recovery == WorkspaceRecovery.Recovering)
                    {
                        interruptedUpdates.TrySetResult();
                    }
                    if (interruptedUpdates.Task.IsCompleted && recovering.Recovery == WorkspaceRecovery.Current)
                    {
                        recovered.TrySetResult();
                    }
                };
                await server.RestartAsync(token);
                await interruptedUpdates.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                await recovered.Task.WaitAsync(TimeSpan.FromSeconds(45), token);
                Assert.False(recovering.Find(session.Id)!.Stale);
                Assert.Single(recovering.Find(session.Id)!.Conversation, m => m.Id == inputId);

                // A fresh client has no profile or selection until explicitly supplied.
                using ServerConnections reopenedConnections = new(new OpenCodeConnectionService(client));
                using LiveWorkspace reopened = new(reopenedConnections);
                Assert.Empty(reopenedConnections.Profiles);
                Assert.Null(reopened.Selected);
                ServerProfile reopenedProfile = reopenedConnections.Save(null, "Explicit reconnect", server.Origin)!;
                await reopenedConnections.ConnectAsync(reopenedProfile.Id, server.Password, token);
                await reopened.OpenProjectAsync(location.Directory);
                await reopened.SelectSessionAsync(session.Id);
                Assert.False(reopened.Tasks!.Find(session.Id)!.Stale);
                Assert.Single(reopened.Tasks.Find(session.Id)!.Conversation, m => m.Id == inputId);
            }
            finally
            {
                await watchLifetime.CancelAsync();
                try
                {
                    await watch;
                }
                catch (OperationCanceledException) { }
                catch (WorkspaceException error) when (error.Failure == WorkspaceFailure.Transport) { /* The old subscriber ended at server restart. */ }
            }
        }
        finally
        {
            if (!provider.HasExited)
            {
                provider.Kill(true);
            }

            await provider.WaitForExitAsync(CancellationToken.None);
        }
    }
    private static async Task<TaskSnapshot> WaitAsync(OpenCodeWorkspaceConnection api, string id, Func<TaskSnapshot, bool> predicate, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            TaskSnapshot snapshot = await api.TaskSnapshotAsync(id, deadline.Token);
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(50, deadline.Token);
        }
    }
}
