namespace OpenCodeDesktop.Core.Connections;

/// <summary>Public discovery metadata never includes credentials or raw command output.</summary>
public sealed record LocalServerInfo(ServerAddress Address, string Version);

public interface ILocalServerDiscovery
{
    Task<LocalServerInfo?> DiscoverAsync(CancellationToken cancellationToken);
    /// <summary>Revalidates the expected local origin and returns a temporary secret for immediate connection only.</summary>
    Task<string?> GetPasswordAsync(ServerAddress expectedAddress, CancellationToken cancellationToken);
}

/// <summary>Page-owned detection and explicit add/connect workflow, on the caller's synchronization context.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The pending async operation owns its cancellation source in a using scope; navigation cancels it.")]
public sealed class LocalEnvironment : IDisposable
{
    private readonly ServerConnections _connections;
    private readonly ILocalServerDiscovery _discovery;
    private CancellationTokenSource? _operation;
    private long _generation;
    private bool _disposed;

    public LocalEnvironment(ServerConnections connections, ILocalServerDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(discovery);
        _connections = connections;
        _discovery = discovery;
        connections.Changed += ConnectionsChanged;
    }

    public event EventHandler? Changed;
    public LocalServerInfo? Detected { get; private set; }
    public bool IsBusy => _operation is not null;
    public bool IsAdding { get; private set; }
    public string Message { get; private set; } = "Check for a running local OpenCode environment.";
    public bool IsAlreadyConnected => Detected is { } info && _connections.Active?.Profile.Address.Display == info.Address.Display;
    public bool CanAdd => !_disposed && !IsBusy && !_connections.IsConnecting && Detected is not null && !IsAlreadyConnected;

    public Task DetectAsync() => RunAsync(false);
    public Task AddAsync() => CanAdd ? RunAsync(true) : Task.CompletedTask;

    private async Task RunAsync(bool add)
    {
        if (_disposed || IsBusy || _connections.IsConnecting)
        {
            return;
        }

        LocalServerInfo? target = Detected;
        using CancellationTokenSource operation = new(TimeSpan.FromSeconds(15));
        _operation = operation;
        long generation = ++_generation;
        bool Current() => IsCurrent(generation, operation.Token);
        IsAdding = add;
        if (!add)
        {
            Detected = null;
        }

        Message = add ? "Verifying local environment and connecting…" : "Detecting a running local OpenCode server…";
        Notify();
        try
        {
            if (!add)
            {
                LocalServerInfo? result = await _discovery.DiscoverAsync(operation.Token).ConfigureAwait(true);
                if (!Current())
                {
                    return;
                }

                Detected = result;
                Message = result is null
                    ? "No running compatible local environment with an accessible password was found. Retry detection or enter a server manually."
                    : "Detected OpenCode " + result.Version + " at " + result.Address.Display + ". Its password was verified and will be retrieved again when connecting.";
            }
            else if (target is not null)
            {
                string? password = await _discovery.GetPasswordAsync(target.Address, operation.Token).ConfigureAwait(true);
                try
                {
                    if (!Current())
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(password))
                    {
                        Detected = null;
                        Message = "The local environment changed or its password could not be verified. Detect again or enter a server manually.";
                        return;
                    }
                    ServerProfile? profile = _connections.Profiles.FirstOrDefault(p => p.Address.Display == target.Address.Display)
                        ?? _connections.Save(null, "Local OpenCode", target.Address.Display);
                    if (profile is null)
                    {
                        Message = "The local profile could not be added. Finish the current connection and retry.";
                        return;
                    }
                    await _connections.ConnectAsync(profile.Id, password, operation.Token).ConfigureAwait(true);
                    if (!Current())
                    {
                        return;
                    }

                    Message = _connections.Active?.Profile.Id == profile.Id ? "Local environment connected. Open a server-side directory in Projects." : _connections.Message;
                }
                finally
                {
                    password = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation && !_disposed)
            {
                Message = "Local detection timed out or was cancelled. Retry detection or enter a server manually.";
            }
        }
        catch (Exception)
        {
            if (generation == _generation && !_disposed)
            {
                Message = "Could not detect or connect to the local environment. Retry detection or enter a server manually.";
            }
        }
        finally
        {
            if (!_disposed && generation == _generation)
            {
                if (operation.IsCancellationRequested)
                {
                    Detected = null;
                    Message = "Local detection timed out or was cancelled. Retry detection or enter a server manually.";
                }
                _operation = null;
                IsAdding = false;
                Notify();
            }
        }
    }

    private bool IsCurrent(long generation, CancellationToken token) => !_disposed && generation == _generation && !token.IsCancellationRequested;

    public void Deactivate()
    {
        _generation++;
        _operation?.Cancel();
        _operation = null;
        Detected = null;
        IsAdding = false;
        Message = "Check for a running local OpenCode environment.";
        if (!_disposed)
        {
            Notify();
        }
    }

    private void ConnectionsChanged(object? sender, EventArgs args) => Notify();
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connections.Changed -= ConnectionsChanged;
        Deactivate();
    }
}
