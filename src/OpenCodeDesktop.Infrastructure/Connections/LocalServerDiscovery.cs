using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Infrastructure.Connections;

/// <summary>Fixed read-only CLI commands; stdout can contain a password and must never be logged.</summary>
public interface ILocalOpenCodeCommands
{
    IEnumerable<string> FindExecutables();
    Task<string?> ExecuteAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class LocalServerDiscovery(IServerConnectionService service, ILocalOpenCodeCommands commands, TimeSpan? deadline = null) : ILocalServerDiscovery
{
    public Task<LocalServerInfo?> DiscoverAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        (LocalServerInfo Info, string Password)? match = await FindAsync(null, cancellationToken).ConfigureAwait(false);
        return match?.Info;
    }, cancellationToken);

    public Task<string?> GetPasswordAsync(ServerAddress expectedAddress, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        (LocalServerInfo Info, string Password)? match = await FindAsync(expectedAddress, cancellationToken).ConfigureAwait(false);
        return match?.Password;
    }, cancellationToken);

    private async Task<(LocalServerInfo Info, string Password)?> FindAsync(ServerAddress? expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline ?? TimeSpan.FromSeconds(10));
        foreach (string executable in commands.FindExecutables().Distinct(StringComparer.OrdinalIgnoreCase).Take(8))
        {
            try
            {
                const string versionPrefix = "opencode v";
                string? reported = (await commands.ExecuteAsync(executable, ["--version"], timeout.Token).ConfigureAwait(false))?.Trim();
                if (reported is null || !reported.StartsWith(versionPrefix, StringComparison.Ordinal)
                    || !ServerVersionPolicy.IsSupported(reported[versionPrefix.Length..]))
                {
                    continue;
                }

                string? status = await commands.ExecuteAsync(executable, ["service", "status"], timeout.Token).ConfigureAwait(false);
                if (!ServerAddress.TryParse(status?.Trim(), out ServerAddress? address) || (expected is not null && address!.Display != expected.Display))
                {
                    continue;
                }

                string? password = (await commands.ExecuteAsync(executable, ["service", "get", "password"], timeout.Token).ConfigureAwait(false))?.TrimEnd('\r', '\n');
                if (string.IsNullOrEmpty(password) || password.Length > 4096)
                {
                    continue;
                }

                ConnectionResult probe = await service.ProbeAsync(address!, password, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (probe.IsReady && ServerVersionPolicy.IsSupported(probe.Version ?? ""))
                {
                    return (new(address!, probe.Version!), password);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Never surface command output, credentials, paths or raw errors as discovery diagnostics.
            }
        }
        return null;
    }
}
