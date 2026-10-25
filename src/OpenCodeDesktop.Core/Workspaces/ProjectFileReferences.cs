namespace OpenCodeDesktop.Core.Workspaces;

/// <summary>Only structured file URIs inside the session's server directory can become candidates.
/// Text resembling a filename, remote URIs and inline attachments remain non-actionable.</summary>
public static class ProjectFileReferences
{
    public static string? Candidate(string directory, string source)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || !uri.IsFile || uri.Host.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return null;
        }
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        bool windows = directory.Length > 1 && directory[1] == ':';
        string root = directory;
        if (windows)
        {
            root = root.Replace('\\', '/');
            if (path.Length > 2 && path[0] == '/' && path[2] == ':')
            {
                path = path[1..];
            }
        }
        root = root.TrimEnd('/') + "/";
        if (!path.StartsWith(root, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return null;
        }
        string relative = path[root.Length..];
        if (relative.Length == 0 || relative.Split('/').Any(segment => segment is ".." or "." or "") || relative.Contains('\0'))
        {
            return null;
        }
        return relative;
    }

    public static async Task<string?> VerifyAsync(IProjectFilesConnection connection, string directory, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        string? candidate = Candidate(directory, source);
        if (candidate is null)
        {
            return null;
        }
        int separator = candidate.LastIndexOf('/');
        ProjectFileList listing = await connection.ListFilesAsync(directory, separator < 0 ? "" : candidate[..separator], cancellationToken).ConfigureAwait(false);
        bool windows = directory.Length > 1 && directory[1] == ':';
        return listing.Entries.FirstOrDefault(entry => !entry.IsDirectory && string.Equals(
            windows ? entry.Path.Replace('\\', '/') : entry.Path, candidate,
            windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))?.Path;
    }
}
