using System.Collections.ObjectModel;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Connections;

public sealed record ServerProfile(Guid Id, string Name, ServerAddress Address, bool RememberCredentials = true, string? CredentialReference = null);
public sealed record ActiveServer(ServerProfile Profile, string Version);

/// <summary>Profile catalog and atomic active-server promotion. Call from one owning context (the UI thread in App).</summary>
public sealed class ServerConnections(IServerConnectionService service, ICredentialStore? credentials = null) : IDisposable
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
    public event EventHandler? PersistenceChanged;
    public event EventHandler? UserNavigation;
    public Guid? AutomaticConnectionId { get; private set; }
    public string? CredentialError { get; private set; }

    internal void RestoreProfiles(WorkspaceSettings settings)
    {
        _profiles.Clear();
        foreach (SavedConnection saved in settings.Connections)
        {
            if (!ServerAddress.TryParse(saved.Address, out ServerAddress? address))
            {
                throw new InvalidOperationException("Invalid saved address.");
            }

            _profiles.Add(new(saved.Id, saved.Name, address!, saved.RememberCredentials, saved.CredentialReference));
        }
        AutomaticConnectionId = settings.ActiveConnectionId;
        Notify();
    }

    internal Task RestoreConnectionAsync(Guid id, CancellationToken token) => ConnectCoreAsync(id, "", token);
    private void Persist() => PersistenceChanged?.Invoke(this, EventArgs.Empty);

    public ServerProfile? Save(Guid? id, string name, string? origin, bool? rememberCredentials = null)
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

        UserNavigation?.Invoke(this, EventArgs.Empty);
        ServerProfile? previous = _profiles.Find(p => p.Id == id);
        bool remember = rememberCredentials ?? previous?.RememberCredentials ?? true;
        string? reference = remember && previous?.Address.Display == address!.Display ? previous.CredentialReference : null;
        ServerProfile profile = new(id ?? Guid.NewGuid(), name.Trim(), address!, remember, reference);
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
        Message = "Server saved. Connect with its saved credential or enter a password.";
        Notify();
        Persist();
        return profile;
    }

    public bool Remove(Guid id)
    {
        if (_disposed || IsConnecting || Active?.Profile.Id == id)
        {
            return false;
        }

        UserNavigation?.Invoke(this, EventArgs.Empty);
        bool removed = _profiles.RemoveAll(p => p.Id == id) != 0;
        if (removed)
        {
            LastResult = null;
            Message = "Server removed.";
            if (AutomaticConnectionId == id)
            {
                AutomaticConnectionId = null;
            }

            Notify();
            Persist();
        }

        return removed;
    }

    public Task ConnectAsync(Guid id, string password) => ConnectAsync(id, password, CancellationToken.None);

    public Task ConnectAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        if (IsConnecting)
        {
            return Task.CompletedTask;
        }

        UserNavigation?.Invoke(this, EventArgs.Empty);
        Cancel();
        return ConnectCoreAsync(id, password, cancellationToken);
    }

    private async Task ConnectCoreAsync(Guid id, string password, CancellationToken cancellationToken)
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

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _attempt = attempt;
        ConnectingId = id;
        LastResult = null;
        Message = "Connecting to " + profile.Name + "…";
        Notify();
        CredentialError = null;
        ConnectionResult result;
        try
        {
            if (string.IsNullOrEmpty(password) && credentials is not null && profile.CredentialReference is not null)
            {
                password = await credentials.ReadAsync(profile.CredentialReference, attempt.Token).ConfigureAwait(true);
            }

            if (_disposed || attempt.IsCancellationRequested || !ReferenceEquals(_attempt, attempt))
            {
                FinishCancelled(attempt);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                _attempt = null;
                ConnectingId = null;
                Message = "Enter the current server password to reconnect.";
                LastResult = new(ConnectionOutcome.ValidationFailed, Message);
                Notify();
                return;
            }
            attempt.Token.ThrowIfCancellationRequested();
            result = await service.ProbeAsync(profile.Address, password, attempt.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            result = ConnectionResult.Cancelled;
        }
        catch (Exception)
        {
            // External implementations must not leak credentials or raw exception text into UI state.
            result = new(ConnectionOutcome.TransportFailure, "Connection or credential retrieval failed. Enter the current password and reconnect.");
        }

        if (_disposed || !ReferenceEquals(_attempt, attempt))
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
                ServerProfile? saved = await ProtectCredentialAsync(profile, password, attempt).ConfigureAwait(true);
                if (saved is null)
                {
                    return;
                }

                profile = saved;
                IWorkspaceConnection? candidate = (service as IWorkspaceConnectionFactory)?.Create(profile.Address, password);
                _profiles[_profiles.FindIndex(p => p.Id == id)] = profile;
                Context?.Dispose();
                Context = candidate;
                Generation++;
                Active = new(profile, result.Version!);
                AutomaticConnectionId = id;
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
        if (result.IsReady)
        {
            Persist();
        }
    }

    private async Task<ServerProfile?> ProtectCredentialAsync(ServerProfile profile, string password, CancellationTokenSource attempt)
    {
        if (profile.RememberCredentials && credentials is not null)
        {
            try
            {
                string reference = await credentials.SaveAsync(password, attempt.Token).ConfigureAwait(true);
                if (_disposed || attempt.IsCancellationRequested || !ReferenceEquals(_attempt, attempt))
                {
                    await credentials.DeleteAsync(reference, CancellationToken.None).ConfigureAwait(true);
                    FinishCancelled(attempt);
                    return null;
                }
                profile = profile with { CredentialReference = reference };
            }
            catch (Exception)
            {
                if (!_disposed && ReferenceEquals(_attempt, attempt))
                {
                    CredentialError = "The password could not be saved securely. Reconnect to save it again; the current connection remains usable.";
                }
            }
        }
        if (_disposed || attempt.IsCancellationRequested || !ReferenceEquals(_attempt, attempt))
        {
            FinishCancelled(attempt);
            return null;
        }
        return profile;
    }

    public void Cancel()
    {
        if (_attempt is not null)
        {
            LastResult = ConnectionResult.Cancelled;
            Message = LastResult.Message;
        }
        _attempt?.Cancel();
        _attempt = null;
        ConnectingId = null;
        Notify();
    }

    private void FinishCancelled(CancellationTokenSource attempt)
    {
        if (!_disposed && ReferenceEquals(_attempt, attempt))
        {
            _attempt = null;
            ConnectingId = null;
            LastResult = ConnectionResult.Cancelled;
            Message = LastResult.Message;
            Notify();
        }
    }

    public void Disconnect()
    {
        if (_disposed)
        {
            return;
        }

        UserNavigation?.Invoke(this, EventArgs.Empty);
        Cancel();
        AutomaticConnectionId = null;
        ReleaseContext();
        LastResult = null;
        Message = "Disconnected. Select a server to connect.";
        Notify();
        Persist();
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
