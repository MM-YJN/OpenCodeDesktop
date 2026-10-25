using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class ProjectFilesTests
{
    [Fact]
    public void TabsUseConnectionDirectoryAndSessionIdentityAndCloseAdjacentViewsOnly()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        WorkspaceTabs tabs = new();
        tabs.SetConnection(a);
        WorkspaceTab first = new(new(a, "/one", "same", false), "First");
        WorkspaceTab files = new(new(a, "/one", null, true), "Files");
        WorkspaceTab second = new(new(a, "/two", "same", false), "Second");
        tabs.Open(first);
        tabs.Open(files);
        tabs.Open(second);
        tabs.Open(files with { Title = "Updated" });
        Assert.Equal(3, tabs.OpenTabs.Count);
        Assert.Equal(files.Key, tabs.Active);
        tabs.Close(files.Key);
        Assert.Equal(second.Key, tabs.Active);
        tabs.SetConnection(b);
        Assert.Empty(tabs.OpenTabs);
        Assert.Null(tabs.Active);
        tabs.Open(new(new(b, "/one", null, true), "Other server files"));
        tabs.Activate(first.Key);
        Assert.Equal(b, tabs.Active!.Value.ConnectionId);
        tabs.SetConnection(a);
        Assert.Equal(2, tabs.OpenTabs.Count);
        tabs.Activate(second.Key);
        tabs.Close(second.Key);
        Assert.Equal(first.Key, tabs.Active);
        tabs.Close(first.Key);
        Assert.Null(tabs.Active);
        tabs.Open(files);
        Assert.Equal(files.Key, Assert.Single(tabs.OpenTabs).Key);
        tabs.ForgetConnection(a);
        Assert.Null(tabs.Active);
        Assert.Empty(tabs.OpenTabs);
    }

    [Fact]
    public async Task FilesRetainStateWithoutSessionsAndDoNotCollideOnSharedProjectId()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        using ProjectFilesWorkspace files = new();
        Fake api = new();
        files.SetConnection(a, api);
        ProjectFilesState one = files.Open(new("/one", "global"))!;
        await files.BrowseAsync("nested 空", "hello");
        await files.SelectAsync("nested 空/hello.txt");
        one.PreviewOffset = 123;
        one.ListOffset = 45;
        files.Suspend();
        ProjectFilesState two = files.Open(new("/two", "global"))!;
        Assert.Empty(two.Entries);
        Assert.Null(two.SelectedPath);
        Assert.Same(one, files.Open(new("/one", "global")));
        Assert.Equal("hello", one.Query);
        Assert.Equal("nested 空", one.DirectoryPath);
        Assert.Equal("nested 空/hello.txt", one.SelectedPath);
        Assert.Equal("preview", one.Preview!.Text);
        Assert.Equal(123, one.PreviewOffset);
        Assert.Equal(45, one.ListOffset);
        files.SetConnection(b, api);
        Assert.Null(files.Current);
        Assert.NotSame(one, files.Open(new("/one", "global")));
        files.SetConnection(a, api);
        Assert.Same(one, files.Open(new("/one", "global")));
        Assert.NotSame(one, files.Open(new("/one", "changed-project")));
        files.ForgetConnection(a);
        Assert.Null(files.Current);
        Assert.Empty(files.Open(new("/one", "global"))!.Entries);
    }

    [Fact]
    public async Task DebouncedQueryIsRetainedAndInvalidatesOnlyTheObsoleteListing()
    {
        using ProjectFilesWorkspace files = new();
        Fake api = new();
        Guid id = Guid.NewGuid();
        files.SetConnection(id, api);
        ProjectFilesState state = files.Open(new("/one", "global"))!;
        TaskCompletionSource<ProjectFileList> listing = new();
        api.List = (_, _, _) => listing.Task;
        Task old = files.BrowseAsync("nested", "old");
        files.EditQuery("typed before switching");
        files.Suspend();
        files.Open(new("/two", "global"));
        files.Open(new("/one", "global"));
        listing.SetResult(new([new("old", false)], false));
        await old;
        Assert.Equal("typed before switching", state.Query);
        Assert.False(state.Loaded);
        Assert.Empty(state.Entries);
    }

    [Fact]
    public async Task SupersededSearchReadAndConnectionCompletionsCannotReplaceCurrentContent()
    {
        using ProjectFilesWorkspace files = new();
        Fake api = new();
        files.SetConnection(Guid.NewGuid(), api);
        ProjectFilesState first = files.Open(new("/one", "global"))!;
        TaskCompletionSource<ProjectFileList> delayedList = new();
        CancellationToken oldListToken = default;
        api.List = (_, _, token) =>
        {
            oldListToken = token;
            return delayedList.Task;
        };
        Task oldList = files.BrowseAsync("old", "");
        api.List = (_, _, _) => Task.FromResult(new ProjectFileList([new("new", false)], false));
        await files.BrowseAsync("new", "");
        Assert.True(oldListToken.IsCancellationRequested);
        delayedList.SetResult(new([new("old", false)], false));
        await oldList;
        Assert.Equal("new", Assert.Single(first.Entries).Path);

        TaskCompletionSource<ProjectFilePreview> delayedRead = new();
        api.Read = (_, _, _) => delayedRead.Task;
        Task oldRead = files.SelectAsync("old");
        api.Read = (_, _, _) => Task.FromResult(new ProjectFilePreview(FilePreviewKind.Text, "new", false));
        await files.SelectAsync("new");
        delayedRead.SetResult(new(FilePreviewKind.Text, "old", false));
        await oldRead;
        Assert.Equal("new", first.Preview!.Text);

        TaskCompletionSource<ProjectFilePreview> otherBackend = new();
        api.Read = (_, _, _) => otherBackend.Task;
        Task obsolete = files.SelectAsync("pending");
        files.SetConnection(Guid.NewGuid(), new Fake());
        ProjectFilesState second = files.Open(new("/one", "global"))!;
        await files.SelectAsync("own");
        otherBackend.SetException(new WorkspaceException(WorkspaceFailure.Transport, "private transport data"));
        await obsolete;
        Assert.Equal("preview", second.Preview!.Text);
        Assert.Null(second.PreviewError);
        Assert.False(first.Reading);
    }

    [Theory]
    [InlineData(WorkspaceFailure.NotFound)]
    [InlineData(WorkspaceFailure.Unauthorized)]
    [InlineData(WorkspaceFailure.Size)]
    [InlineData(WorkspaceFailure.Transport)]
    public async Task FileFailuresAreSafeAndRetriable(WorkspaceFailure failure)
    {
        using ProjectFilesWorkspace files = new();
        Fake api = new() { Read = (_, _, _) => throw new WorkspaceException(failure, "secret raw body") };
        files.SetConnection(Guid.NewGuid(), api);
        ProjectFilesState state = files.Open(new("/one", "global"))!;
        await files.SelectAsync("test");
        Assert.NotNull(state.PreviewError);
        Assert.DoesNotContain("secret", state.PreviewError, StringComparison.Ordinal);
        Assert.False(state.Reading);
        api.Read = (_, _, _) => Task.FromResult(new ProjectFilePreview(FilePreviewKind.Text, "retry", false));
        await files.SelectAsync("test");
        Assert.Null(state.PreviewError);
        Assert.Equal("retry", state.Preview!.Text);
    }

    private sealed class Fake : IProjectFilesConnection
    {
        public Func<string, string, CancellationToken, Task<ProjectFileList>> List { get; set; } = (_, _, _) => Task.FromResult(new ProjectFileList([new("hello", false)], false));
        public Func<string, string, CancellationToken, Task<ProjectFilePreview>> Read { get; set; } = (_, _, _) => Task.FromResult(new ProjectFilePreview(FilePreviewKind.Text, "preview", false));
        public Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken) => List(directory, path, cancellationToken);
        public Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken) => List(directory, query, cancellationToken);
        public Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken) => Read(directory, path, cancellationToken);
    }
}
