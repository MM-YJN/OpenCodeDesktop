using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Presentation.Tests;

public sealed class ProjectFilesPresentationTests
{
    [Theory]
    [InlineData("C:\\server 空", "nested\\file.txt", "C:\\server 空\\nested\\file.txt")]
    [InlineData("/server/project", "nested/file.txt", "/server/project/nested/file.txt")]
    [InlineData("/", "file.txt", "/file.txt")]
    [InlineData("/server/project", "/other/file.txt", "/other/file.txt")]
    [InlineData("C:/server", "file.txt", "C:/server/file.txt")]
    [InlineData("/server", "literal\\name.txt", "/server/literal\\name.txt")]
    public void DisplayPathsPreserveServerSemantics(string root, string relative, string expected) =>
        Assert.Equal(expected, ProjectFilesPresentation.FullPath(root, relative));

    [Theory]
    [InlineData(FilePreviewKind.Text, "", false, "Empty text file")]
    [InlineData(FilePreviewKind.Text, "content", false, "Read-only")]
    [InlineData(FilePreviewKind.Text, "prefix", true, "Partial preview")]
    [InlineData(FilePreviewKind.Unsupported, "", false, "Binary or unsupported")]
    public async Task PreviewLabelsDistinguishUnsupportedEmptyAndTruncated(FilePreviewKind kind, string text, bool truncated, string expected)
    {
        using ProjectFilesWorkspace files = new();
        files.SetConnection(Guid.NewGuid(), new Fake(new(kind, text, truncated)));
        ProjectFilesState state = files.Open(new("/server", "global"))!;
        await files.BrowseAsync("", "");
        await files.SelectAsync("test.txt");
        ProjectFilesPresentation presentation = ProjectFilesPresentation.From(state);
        Assert.Contains(expected, presentation.PreviewStatus, StringComparison.Ordinal);
        Assert.Equal("/server/test.txt", presentation.SelectedPath);
        Assert.Equal(text, presentation.Text);
        Assert.Equal("No files found.", presentation.Listing);
    }

    private sealed class Fake(ProjectFilePreview preview) : IProjectFilesConnection
    {
        public Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken) => Task.FromResult(new ProjectFileList([], false));
        public Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken) => ListFilesAsync(directory, query, cancellationToken);
        public Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken) => Task.FromResult(preview);
    }
}
