using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ProjectTreePresentationTests
{
    [Theory]
    [InlineData(false, "needle", "No matching sessions.")]
    [InlineData(true, "", "No archived sessions.")]
    public void FilteredEmptyHistoryNamesItsScope(bool archived, string search, string empty)
    {
        WorkspaceProject project = new(new("/project", "p"), false, null, [], true, false, null, false)
        { Filter = new(search, archived) };
        ProjectTreeNodePresentation row = ProjectTreeNodePresentation.Project(project, null, false);
        Assert.Contains(empty, row.Detail, StringComparison.Ordinal);
        Assert.Contains(archived ? "Archived history" : "Active history", row.Detail, StringComparison.Ordinal);
        Assert.Empty(row.Children);
    }

    [Fact]
    public async Task TreeShowsProjectsSessionsActiveSelectionAndFullPaths()
    {
        Service service = new();
        using ServerConnections connections = new(service);
        using LiveWorkspace workspace = new(connections);
        ServerProfile profile = connections.Save(null, "Tree server", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        service.Api.List = (directory, _, _) => Task.FromResult(new WorkspacePage<WorkspaceSession>(
            directory == "/server/two"
                ? [new("c", directory, "global", "Session c")]
                : [new("a", directory, "global", "Session a"), new("b", directory, "global", "Session b")], null));
        await workspace.OpenProjectAsync("/server/one");
        await workspace.OpenProjectAsync("/server/two");
        await workspace.OpenProjectAsync("/server/one");
        await workspace.SelectSessionAsync("a");

        var tree = ProjectTreePresentation.From(workspace);

        Assert.Equal(2, tree.Projects.Count);
        ProjectTreeNodePresentation active = Assert.Single(tree.Projects, project => project.IsActive);
        Assert.Equal("/server/one", active.Directory);
        Assert.Equal("/server/one", active.Path);
        Assert.Equal(ProjectTreeNodePresentation.SessionKey("/server/one", "a"), tree.SelectedKey);
        Assert.Collection(
            active.Children,
            session =>
            {
                Assert.Equal(ProjectTreeItemKind.Session, session.Kind);
                Assert.Equal("Session a", session.Label);
                Assert.True(session.IsSelected);
            },
            session =>
            {
                Assert.Equal(ProjectTreeItemKind.Session, session.Kind);
                Assert.Equal("Session b", session.Label);
                Assert.False(session.IsSelected);
            });
        ProjectTreeNodePresentation inactive = Assert.Single(tree.Projects, project => !project.IsActive);
        Assert.Equal("/server/two", inactive.Path);
        Assert.Equal("Session c", Assert.Single(inactive.Children).Label);
        Assert.DoesNotContain("ago", string.Join('|', tree.Projects.Select(project => project.Label)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TreeShowsDraftEmptyAndLoadMoreNodesWithoutSynthesizingCountsOrTimes()
    {
        Service service = new();
        using ServerConnections connections = new(service);
        using LiveWorkspace workspace = new(connections);
        ServerProfile profile = connections.Save(null, "Tree server", "http://127.0.0.1:4096")!;
        await connections.ConnectAsync(profile.Id, "secret", TestContext.Current.CancellationToken);
        service.Api.List = (directory, cursor, _) => directory switch
        {
            "/empty" => Task.FromResult(new WorkspacePage<WorkspaceSession>([], null)),
            _ => Task.FromResult(new WorkspacePage<WorkspaceSession>(
                cursor is null ? [new("a", directory, "global", "Session a")] : [], cursor is null ? "next" : null)),
        };

        await workspace.OpenProjectAsync("/empty");
        var empty = ProjectTreePresentation.From(workspace);
        ProjectTreeNodePresentation emptyProject = Assert.Single(empty.Projects);
        Assert.Equal("No sessions yet.", emptyProject.Detail);
        Assert.Equal(ProjectTreeNodePresentation.DraftKey("/empty"), empty.SelectedKey);
        Assert.Equal(ProjectTreeItemKind.Draft, Assert.Single(emptyProject.Children).Kind);

        await workspace.OpenProjectAsync("/server/project");
        var paged = ProjectTreePresentation.From(workspace);
        ProjectTreeNodePresentation pagedProject = Assert.Single(paged.Projects, project => project.Directory == "/server/project");
        Assert.Equal(ProjectTreeItemKind.LoadMore, pagedProject.Children[^1].Kind);
        Assert.Equal("Load more sessions", pagedProject.Children[^1].Label);
        Assert.DoesNotContain("remaining", pagedProject.Children[^1].Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ago", pagedProject.Children[^1].Label, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Service : IServerConnectionService, IWorkspaceConnectionFactory
    {
        public Api Api { get; } = new();
        public IWorkspaceConnection Create(ServerAddress address, string password) => Api;
        public Task<ConnectionResult> ProbeAsync(ServerAddress address, string password, CancellationToken cancellationToken) => Task.FromResult(new ConnectionResult(ConnectionOutcome.Ready, "Ready", "2.1.4"));
    }

    private sealed class Api : IWorkspaceConnection
    {
        private string _directory = "";
        public Func<string, string?, CancellationToken, Task<WorkspacePage<WorkspaceSession>>>? List { get; set; }
        public Task<ProjectLocation> ResolveAsync(string directory, CancellationToken cancellationToken)
        {
            _directory = directory;
            return Task.FromResult(new ProjectLocation(directory, "global"));
        }

        public Task ValidateDirectoryAsync(string directory, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkspacePage<WorkspaceSession>> SessionsAsync(string directory, string? cursor, CancellationToken cancellationToken) => List?.Invoke(directory, cursor, cancellationToken)
            ?? Task.FromResult(new WorkspacePage<WorkspaceSession>(
                cursor is null ? [new("a", directory, "global", "Session a"), new("b", directory, "global", "Session b"), new("c", directory, "global", "Session c")] : [], null));
        public Task<WorkspaceSession> CreateAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(new WorkspaceSession("created", directory, "global", "Created"));
        public Task<WorkspaceSession> SessionAsync(string id, CancellationToken cancellationToken) => Task.FromResult(new WorkspaceSession(id, _directory, "global", "Session " + id));
        public Task<WorkspacePage<HistoryMessage>> HistoryAsync(string id, string? cursor, CancellationToken cancellationToken) => Task.FromResult(new WorkspacePage<HistoryMessage>([new(id, "user", "history " + id)], null));
        public void Dispose() { }
    }
}
