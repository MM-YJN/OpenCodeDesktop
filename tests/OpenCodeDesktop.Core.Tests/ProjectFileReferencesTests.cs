using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class ProjectFileReferencesTests
{
    [Theory]
    [InlineData("/server/project", "file:///server/project/nested/hello%20%23.txt", "nested/hello #.txt")]
    [InlineData("C:\\server", "file:///C:/server/nested/file.txt", "nested/file.txt")]
    [InlineData("/server/project", "file:///server/project-other/file.txt", null)]
    [InlineData("/server/project", "file:///server/project/../other/file.txt", null)]
    [InlineData("/server/project", "https://example.com/file.txt", null)]
    [InlineData("/server/project", "file://other/server/project/file.txt", null)]
    [InlineData("/server/project", "file:///server/project/file.txt#fragment", null)]
    [InlineData("/server/project", "nested/file.txt", null)]
    [InlineData("/server/project", "file:///Server/project/file.txt", null)]
    public void OnlyUnambiguousStructuredProjectUrisAreCandidates(string directory, string source, string? expected) =>
        Assert.Equal(expected, ProjectFileReferences.Candidate(directory, source));

    [Fact]
    public async Task ReferenceMustMatchAnActualServerFileAndReturnsServerPathSpelling()
    {
        Fake connection = new();
        Assert.Equal("nested\\file.txt", await ProjectFileReferences.VerifyAsync(connection, "C:\\server", "file:///C:/server/nested/file.txt", TestContext.Current.CancellationToken));
        Assert.Null(await ProjectFileReferences.VerifyAsync(connection, "C:\\server", "file:///C:/server/nested/missing.txt", TestContext.Current.CancellationToken));
        Assert.Null(await ProjectFileReferences.VerifyAsync(connection, "C:\\server", "file:///C:/server/nested/directory", TestContext.Current.CancellationToken));
        Assert.All(connection.Parents, parent => Assert.Equal("nested", parent));
    }

    private sealed class Fake : IProjectFilesConnection
    {
        public List<string> Parents { get; } = [];
        public Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken)
        {
            Parents.Add(path);
            return Task.FromResult(new ProjectFileList([new("nested\\file.txt", false), new("nested\\directory", true)], false));
        }
        public Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
