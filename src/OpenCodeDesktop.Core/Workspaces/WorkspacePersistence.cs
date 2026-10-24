using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Workspaces;

/// <summary>Owns startup policy and ordered snapshots on the caller's synchronization context.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "StartAsync and RetryAsync own and dispose each linked startup source in a using scope; window shutdown only cancels it.")]
public sealed class WorkspacePersistence : IDisposable
{
    private readonly ServerConnections _connections;
    private readonly LiveWorkspace _workspace;
    private readonly IWorkspaceSettingsStore _store;
    private readonly ICredentialStore _credentials;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _startup;
    private Task _writes = Task.CompletedTask;
    private WorkspaceSettings? _committed;
    private readonly HashSet<string> _credentialCandidates = new(StringComparer.Ordinal);
    private string? _error;
    private string? _saveError;
    private bool _loaded;
    private bool _disposed;
    private bool _started;

    public WorkspacePersistence(ServerConnections connections, LiveWorkspace workspace, IWorkspaceSettingsStore store, ICredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(credentials);
        _connections = connections;
        _workspace = workspace;
        _store = store;
        _credentials = credentials;
        connections.PersistenceChanged += SaveRequested;
        workspace.NavigationChanged += SaveRequested;
        connections.UserNavigation += UserNavigation;
        workspace.UserNavigation += UserNavigation;
    }

    public event EventHandler? Changed;
    public bool IsRestoring { get; private set; }
    public bool NeedsReset { get; private set; }
    public string? Error { get => _saveError ?? _error; private set => _error = value; }
    public string? Status { get; private set; }
    public bool CanRetry => !IsRestoring && _loaded && _connections.AutomaticConnectionId is not null;
    public bool CanRetrySave => !_disposed && _loaded && _saveError is not null;
    public Task FlushAsync() => _writes;

    public async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _startup = startup;
        IsRestoring = true;
        Status = "Loading saved connections…";
        Notify();
        try
        {
            WorkspaceSettings? settings = await _store.LoadAsync(_lifetime.Token).ConfigureAwait(true);
            // Even cancelled startup must establish whether existing storage can safely be written.
            _loaded = true;
            _committed = settings;
            if (startup.IsCancellationRequested || _disposed)
            {
                // A user action superseded loading. Preserve existing state until an explicit reset.
                if (settings is not null)
                {
                    _loaded = false;
                    NeedsReset = true;
                    Error = "Saved state was not loaded because navigation changed. Restart to restore it, or reset saved settings explicitly.";
                }
                else if (!_disposed)
                {
                    SaveRequested(this, EventArgs.Empty);
                }

                return;
            }
            if (settings is not null)
            {
                _connections.RestoreProfiles(settings);
                _workspace.RestoreNavigation(settings);
            }
            await RestoreAsync(startup.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            NeedsReset = true;
            _loaded = false;
            Error = "Saved settings could not be read. The original data is preserved. Restart to retry, or reset saved settings.";
        }
        finally
        {
            if (ReferenceEquals(_startup, startup))
            {
                _startup = null;
                IsRestoring = false;
                Status = null;
            }
            Notify();
        }
    }

    private async Task RestoreAsync(CancellationToken token)
    {
        if (_connections.AutomaticConnectionId is not { } id || token.IsCancellationRequested)
        {
            return;
        }

        Status = "Reconnecting and restoring the last workspace…";
        Notify();
        await _connections.RestoreConnectionAsync(id, token).ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        await _workspace.ConnectionTransition.ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (_connections.Active?.Profile.Id != id)
        {
            Error = _connections.Message + " Retry or select a connection in Settings > Connections.";
        }
        else if (_workspace.Error is not null)
        {
            Error = _workspace.Error;
        }
        else
        {
            Error = _connections.CredentialError;
        }
    }

    public async Task RetryAsync()
    {
        if (!CanRetry || _disposed)
        {
            return;
        }

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _startup = startup;
        IsRestoring = true;
        Error = null;
        Notify();
        try
        {
            if (_connections.Active is not null)
            {
                await _workspace.RetryAsync().ConfigureAwait(true);
                if (!startup.IsCancellationRequested)
                {
                    Error = _workspace.Error;
                }
            }
            else
            {
                await RestoreAsync(startup.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_startup, startup))
            {
                _startup = null;
                IsRestoring = false;
                Status = null;
            }
            Notify();
        }
    }

    public async Task ResetAsync()
    {
        if (_disposed)
        {
            return;
        }

        UserNavigation(this, EventArgs.Empty);
        await _writes.ConfigureAwait(true);
        try
        {
            await _store.ResetAsync(_lifetime.Token).ConfigureAwait(true);
            _loaded = true;
            NeedsReset = false;
            _committed = null;
            Error = null;
            SaveRequested(this, EventArgs.Empty);
            await _writes.ConfigureAwait(true);
        }
        catch (Exception)
        {
            Error = "Settings could not be reset. Existing files have been preserved where possible. Retry when storage is available.";
        }
        Notify();
    }

    private void UserNavigation(object? sender, EventArgs args)
    {
        if (_startup is not null)
        {
            _startup.Cancel();
            _startup = null;
            IsRestoring = false;
            Status = null;
            _workspace.CancelRestoration();
        }
        if (!NeedsReset)
        {
            Error = null;
        }

        Notify();
    }

    private void SaveRequested(object? sender, EventArgs args)
    {
        if (_disposed || !_loaded)
        {
            return;
        }

        WorkspaceSettings snapshot = _workspace.CaptureSettings();
        _writes = SaveAfterAsync(_writes, snapshot);
    }

    private async Task SaveAfterAsync(Task previous, WorkspaceSettings snapshot)
    {
        await previous.ConfigureAwait(true);
        try
        {
            foreach (string reference in References(_committed).Concat(References(snapshot)))
            {
                _credentialCandidates.Add(reference);
            }

            await _store.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(true);
            var retained = References(snapshot).ToHashSet(StringComparer.Ordinal);
            _committed = snapshot;
            foreach (string reference in _credentialCandidates.Where(r => !retained.Contains(r)).ToArray())
            {
                await _credentials.DeleteAsync(reference, CancellationToken.None).ConfigureAwait(true);
                _credentialCandidates.Remove(reference);
            }
            _saveError = null;
        }
        catch (Exception)
        {
            _saveError = "Settings or credential cleanup could not be saved. The current workspace is usable; check storage access and retry saving.";
        }
        Notify();
    }

    public void RetrySave() => SaveRequested(this, EventArgs.Empty);
    private static IEnumerable<string> References(WorkspaceSettings? settings) => settings?.Connections.Where(c => c.CredentialReference is not null).Select(c => c.CredentialReference!) ?? [];
    private void Notify()
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _startup?.Cancel();
        _workspace.CancelRestoration();
        _connections.PersistenceChanged -= SaveRequested;
        _workspace.NavigationChanged -= SaveRequested;
        _connections.UserNavigation -= UserNavigation;
        _workspace.UserNavigation -= UserNavigation;
        _lifetime.Dispose();
    }
}
