using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Workspaces;

public sealed class LocalPromptFileReader : ILocalPromptFileReader
{
    public async Task<PromptAttachment> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (stream.Length > PromptAttachment.MaximumBytes) { throw new ArgumentException("Choose a file no larger than 256 KiB."); }
            byte[] bytes = new byte[PromptAttachment.MaximumBytes + 1];
            int count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken).ConfigureAwait(false);
            return PromptAttachment.Local(Path.GetFileName(path), path, bytes.AsSpan(0, count));
        }
        catch (IOException) { throw new ArgumentException("The local file could not be read. Check that it exists and is available, then attach it again."); }
        catch (UnauthorizedAccessException) { throw new ArgumentException("Access to the local file was denied. Choose a readable file."); }
    }
}
