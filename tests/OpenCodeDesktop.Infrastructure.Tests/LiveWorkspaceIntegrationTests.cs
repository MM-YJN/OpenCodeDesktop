using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Connections;

namespace OpenCodeDesktop.Infrastructure.Tests;

public sealed class LiveWorkspaceIntegrationTests
{
    [Fact]
    public async Task PinnedServersSupportRealDirectorySessionHistoryAndBackendIsolation()
    {
        string? executable = Environment.GetEnvironmentVariable("OPENCODE_CONNECTION_TEST_EXECUTABLE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(executable), "Opt-in: set OPENCODE_CONNECTION_TEST_EXECUTABLE to the pinned 2.1.4 CLI.");
        using (FileStream file = File.OpenRead(executable!))
        {
            Assert.Equal("B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399",
                Convert.ToHexString(await SHA256.HashDataAsync(file, TestContext.Current.CancellationToken)));
        }
        await using LiveConnectionTests.IsolatedServer first = await LiveConnectionTests.IsolatedServer.StartAsync(executable!);
        await using LiveConnectionTests.IsolatedServer second = await LiveConnectionTests.IsolatedServer.StartAsync(executable!);
        using HttpClient client = OpenCodeConnectionService.CreateHttpClient();
        using ServerConnections connections = new(new OpenCodeConnectionService(client));
        using LiveWorkspace workspace = new(connections);
        ServerProfile one = connections.Save(null, "One", first.Origin)!;
        ServerProfile two = connections.Save(null, "Two", second.Origin)!;
        await LiveConnectionTests.WaitForReadyAsync(connections, one.Id, first.Password);
        string directory = Path.Combine(first.WorkspaceDirectory, "空 project");
        string other = Path.Combine(first.WorkspaceDirectory, "other empty");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(other);
        await InitializeGitAsync(directory);
        await InitializeGitAsync(other);
        string filePath = Path.Combine(first.WorkspaceDirectory, "ordinary.txt");
        await File.WriteAllTextAsync(filePath, "not a directory", TestContext.Current.CancellationToken);
        await workspace.OpenProjectAsync(directory);
        Assert.Null(workspace.Error);
        Assert.NotNull(workspace.Location);
        Assert.Empty(workspace.Sessions);
        string projectId = workspace.Location.ProjectId;
        string resolved = workspace.Location.Directory;
        workspace.Draft = "new real draft";
        await workspace.CreateSessionAsync();
        Assert.Null(workspace.Error);
        Assert.NotNull(workspace.Selected);
        Assert.Equal("new real draft", workspace.Draft);
        Assert.True(workspace.HistoryLoaded);
        Assert.Empty(workspace.History);
        string created = workspace.Selected.Id;

        // Seed projected history through the verifier-only import endpoint. Production exposes no import action.
        using HttpRequestMessage get = Authorized(HttpMethod.Get, first.Origin + "/api/session/" + Uri.EscapeDataString(created), first.Password);
        using HttpResponseMessage metadata = await client.SendAsync(get, TestContext.Current.CancellationToken);
        metadata.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Dictionary<string, JsonElement> session = document.RootElement.GetProperty("data").Deserialize<Dictionary<string, JsonElement>>()!;
        string imported = "ses_wi006" + Guid.NewGuid().ToString("N");
        session["id"] = JsonSerializer.SerializeToElement(imported);
        var messages = Enumerable.Range(1, 55).Select(i => new
        {
            id = "msg_wi006" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            type = "synthetic",
            time = new { created = 1700000000000L + i },
            text = "Recorded message " + i,
        }).ToArray();
        using HttpRequestMessage import = Authorized(HttpMethod.Post, first.Origin + "/api/experimental/session/import", first.Password);
        import.Content = new StringContent(JsonSerializer.Serialize(new { info = session, messages, location = new { directory = resolved } }), Encoding.UTF8, "application/json");
        using HttpResponseMessage importedResponse = await client.SendAsync(import, TestContext.Current.CancellationToken);
        importedResponse.EnsureSuccessStatusCode();
        await workspace.RefreshSessionsAsync();
        await workspace.SelectSessionAsync(imported);
        Assert.Null(workspace.Error);
        Assert.Equal(50, workspace.History.Count);
        await workspace.LoadOlderHistoryAsync();
        Assert.Null(workspace.Error);
        Assert.Equal(55, workspace.History.Count);
        Assert.True(workspace.CanLoadHistory);
        await workspace.LoadOlderHistoryAsync();
        Assert.False(workspace.CanLoadHistory);
        Assert.Equal("Recorded message 1", workspace.History[0].Text);
        Assert.Equal("Recorded message 55", workspace.History[^1].Text);
        workspace.Draft = "imported draft";
        await workspace.OpenProjectAsync(other);
        Assert.Null(workspace.Error);
        Assert.Equal(projectId, workspace.Location!.ProjectId);
        Assert.Empty(workspace.Sessions);
        workspace.Draft = "other draft";
        await workspace.OpenProjectAsync(filePath);
        Assert.NotNull(workspace.Error);
        Assert.Equal("other draft", workspace.Draft);
        await workspace.OpenProjectAsync(Path.Combine(first.WorkspaceDirectory, "missing"));
        Assert.NotNull(workspace.Error);
        Assert.Equal("other draft", workspace.Draft);
        await workspace.OpenProjectAsync(resolved);
        Assert.Null(workspace.Error);
        Assert.Equal(imported, workspace.Selected!.Id);
        Assert.Equal("imported draft", workspace.Draft);
        await connections.ConnectAsync(two.Id, "incorrect");
        Assert.Equal(one.Id, connections.Active!.Profile.Id);
        Assert.Equal(imported, workspace.Selected.Id);
        await LiveConnectionTests.WaitForReadyAsync(connections, two.Id, second.Password);
        Assert.Null(workspace.Location);
        Assert.Empty(workspace.Sessions);
        Assert.Empty(workspace.History);
        await workspace.OpenProjectAsync(second.WorkspaceDirectory);
        Assert.Null(workspace.Error);
        Assert.Empty(workspace.Sessions);
        connections.Disconnect();
        Assert.False(workspace.IsConnected);
        Assert.Null(workspace.Location);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string password)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static async Task InitializeGitAsync(string directory)
    {
        ProcessStartInfo start = new("git") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("init");
        start.ArgumentList.Add("--quiet");
        start.ArgumentList.Add(directory);
        using Process process = Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
    }
}
