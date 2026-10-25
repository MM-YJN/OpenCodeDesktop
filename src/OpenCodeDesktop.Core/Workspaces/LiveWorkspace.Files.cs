namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    public async Task<string?> VerifyFileReferenceAsync(string source, CancellationToken cancellationToken)
    {
        if (Location is not { } location || _connections.Context is not IProjectFilesConnection connection)
        {
            return null;
        }
        long generation = _generation;
        long connectionGeneration = _connectionGeneration;
        string? path = await ProjectFileReferences.VerifyAsync(connection, location.Directory, source, cancellationToken).ConfigureAwait(true);
        return generation == _generation && connectionGeneration == _connectionGeneration && Location == location ? path : null;
    }
}
