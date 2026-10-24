using System.Collections.ObjectModel;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Connections;

public sealed record ServerProfile(Guid Id, string Name, ServerAddress Address);
public sealed record ActiveServer(ServerProfile Profile, string Version);

/// <summary>In-memory profiles and atomic active-server promotion. Call from one owning context (the UI thread in App).</summary>
public sealed class ServerConnections(IServerConnectionService service) : IDisposable
{
    private readonly List<ServerProfile> _profiles = [];
    private CancellationTokenSource? _attempt;
    private bool _disposed;

    public ReadOnlyCollection<ServerProfile> Profiles => _profiles.AsReadOnly();
    public ActiveServer? Active { get; private set; }
    public IWorkspaceConnection? Context { get; private set; }
    public long Generation { get; private set; }
    public Guid? ConnectingId { get; private set; }
    public bool IsConnecting => _attempt is not null;
    public ConnectionResult? LastResult { get; private set; }
    public string Message { get; private set; } = "Add a server to get started.";
    public event EventHandler? Changed;

    public ServerProfile? Save(Guid? id, string name, string? origin)
    {
        if (_disposed || IsConnecting)
        {
            return null;
        }

        string? error = string.IsNullOrWhiteSpace(name) ? "Enter a server name." : null;
        if (!ServerAddress.TryParse(string.IsNullOrWhiteSpace(origin) ? ServerAddress.DefaultOrigin : origin, out ServerAddress? address))
        {
            error = ServerAddress.FormatMessage;
        }
        else if (_profiles.Any(p => p.Id != id && p.Address.Display == address!.Display))
        {
            error = "A server with this address already exists. Select it from the list.";
        }
        else if (id.HasValue && (Active?.Profile.Id == id || !_profiles.Any(p => p.Id == id)))
        {
            error = "Disconnect the active server before editing it.";
        }

        if (error is not null)
        {
            LastResult = new(ConnectionOutcome.ValidationFailed, error);
            Message = error;
            Notify();
            return null;
        }

        ServerProfile profile = new(id ?? Guid.NewGuid(), name.Trim(), address!);
        int index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0)
        {
            _profiles.Add(profile);
        }
        else
        {
            _profiles[index] = profile;
        }

        LastResult = null;
        Message = "Server saved for this app session. Enter its password to connect.";
        Notify();
        return profile;
    }

    public bool Remove(Guid id)
    {
        if (_disposed || IsConnecting || Active?.Profile.Id == id)
        {
            return false;
        }

        bool removed = _profiles.RemoveAll(p => p.Id == id) != 0;
        if (removed)
        {
            LastResult = null;
            Message = "Server removed from this app session.";
            Notify();
        }

        return removed;
    }

    public Task ConnectAsync(Guid id, string password) => ConnectAsync(id, password, CancellationToken.None);

    public async Task ConnectAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        if (_disposed || IsConnecting || Active?.Profile.Id == id || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ServerProfile? profile = _profiles.Find(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            Message = "Enter the server password before connecting.";
            LastResult = new(ConnectionOutcome.ValidationFailed, Message);
            Notify();
            return;
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _attempt = attempt;
        ConnectingId = id;
        LastResult = null;
        Message = "Connecting to " + profile.Name + "…";
        Notify();
        ConnectionResult result;
        try
        {
            result = await service.ProbeAsync(profile.Address, password, attempt.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            result = ConnectionResult.Cancelled;
        }
        catch (Exception)
        {
            // External implementations must not leak credentials or raw exception text into UI state.
            result = new(ConnectionOutcome.TransportFailure, "Connection failed. Check the server and try again.");
        }

        if (_disposed)
        {
            return;
        }

        result = attempt.IsCancellationRequested ? ConnectionResult.Cancelled : result;
        if (result.IsReady && !ServerVersionPolicy.IsSupported(result.Version ?? ""))
        {
            result = new(ConnectionOutcome.Incompatible, "The server identity is incompatible. Use OpenCode " + ServerVersionPolicy.SupportedVersion + ".");
        }

        if (result.IsReady)
        {
            try
            {
                IWorkspaceConnection? candidate = (service as IWorkspaceConnectionFactory)?.Create(profile.Address, password);
                Context?.Dispose();
                Context = candidate;
                Generation++;
                Active = new(profile, result.Version!);
            }
            catch (Exception)
            {
                result = new(ConnectionOutcome.TransportFailure, "Could not establish the workspace connection. Try connecting again.");
            }
        }

        _attempt = null;
        ConnectingId = null;
        LastResult = result;
        Message = result.Message;
        Notify();
    }

    public void Cancel() => _attempt?.Cancel();

    public void Disconnect()
    {
        if (_disposed || IsConnecting)
        {
            return;
        }

        ReleaseContext();
        LastResult = null;
        Message = "Disconnected. Select a server to connect.";
        Notify();
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public void RejectAuthorization(long generation)
    {
        if (_disposed || generation != Generation || Active is null)
        {
            return;
        }

        ReleaseContext();
        Message = "Authorization was rejected. Reconnect with the server password.";
        LastResult = new(ConnectionOutcome.AuthenticationFailed, Message);
        Notify();
    }

    private void ReleaseContext()
    {
        Context?.Dispose();
        Context = null;
        Generation++;
        Active = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _attempt?.Cancel();
        _attempt?.Dispose();
        _attempt = null;
        ReleaseContext();
        ConnectingId = null;
        LastResult = null;
        _profiles.Clear();
    }
}
