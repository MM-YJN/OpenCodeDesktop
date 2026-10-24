using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Persistence;

/// <summary>Immutable credential entries protected by Windows for the current user. No keys are stored by the app.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore(string directory) : ICredentialStore
{
    private const int MaximumPasswordCharacters = 4096;
    private const int MaximumProtectedBytes = 64 * 1024;
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("OpenCodeDesktop.Credentials.v1");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Flush(true) durably commits the encrypted entry before settings can reference it; FlushAsync has no equivalent flag. Protection and I/O run on the thread pool.")]
    public Task<string> SaveAsync(string password, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > MaximumPasswordCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(password));
        }

        byte[] plaintext = Encoding.UTF8.GetBytes(password);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plaintext, s_entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
        string reference = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        string path = EntryPath(reference);
        try
        {
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(true);
            return reference;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }, cancellationToken);

    public Task<string> ReadAsync(string reference, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        using FileStream stream = new(EntryPath(reference), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > MaximumProtectedBytes)
        {
            throw new InvalidDataException("Invalid protected credential.");
        }

        byte[] encrypted = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(encrypted, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, s_entropy, DataProtectionScope.CurrentUser);
        try
        {
            string password = Encoding.UTF8.GetString(plaintext);
            if (password.Length is 0 or > MaximumPasswordCharacters)
            {
                throw new InvalidDataException("Invalid protected credential.");
            }

            return password;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }, cancellationToken);

    public Task DeleteAsync(string reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(EntryPath(reference));
        return Task.CompletedTask;
    }

    private string EntryPath(string reference)
    {
        if (!Guid.TryParseExact(reference, "N", out _))
        {
            throw new InvalidDataException("Invalid credential reference.");
        }

        return Path.Combine(directory, reference + ".bin");
    }
}
