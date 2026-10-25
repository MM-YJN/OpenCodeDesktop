namespace OpenCodeDesktop.Core.Workspaces;

public sealed record ProjectFileEntry(string Path, bool IsDirectory);
public sealed record ProjectFileList(IReadOnlyList<ProjectFileEntry> Entries, bool Limited);
public enum FilePreviewKind { Text, Unsupported }
public sealed record ProjectFilePreview(FilePreviewKind Kind, string Text, bool Truncated);

public static class ProjectFileLimits
{
    public const int PreviewBytes = 262144;
    public const int PreviewCharacters = 100000;
    public const int ListBytes = 2097152;
    public const int Entries = 2000;
    public const int SearchResults = 100;
    public const int SearchCharacters = 512;
}

/// <summary>Paths are opaque server paths, never client filesystem paths.</summary>
public interface IProjectFilesConnection
{
    Task<ProjectFileList> ListFilesAsync(string directory, string path, CancellationToken cancellationToken);
    Task<ProjectFileList> FindFilesAsync(string directory, string query, CancellationToken cancellationToken);
    Task<ProjectFilePreview> ReadFileAsync(string directory, string path, CancellationToken cancellationToken);
}

public readonly record struct WorkspaceTabKey(Guid ConnectionId, string Directory, string? SessionId, bool IsFiles);
public sealed record WorkspaceTab(WorkspaceTabKey Key, string Title);

/// <summary>Closing affects views only; retained drafts and file state have separate lifetimes.</summary>
public sealed class WorkspaceTabs
{
    private readonly List<WorkspaceTab> _tabs = [];
    public Guid? ConnectionId { get; private set; }
    public WorkspaceTabKey? Active { get; private set; }
    public IReadOnlyList<WorkspaceTab> OpenTabs => _tabs.Where(t => t.Key.ConnectionId == ConnectionId).ToArray();

    public void SetConnection(Guid? id)
    {
        if (ConnectionId == id)
        {
            return;
        }
        ConnectionId = id;
        Active = null;
    }

    public void ForgetConnection(Guid id)
    {
        _tabs.RemoveAll(tab => tab.Key.ConnectionId == id);
        if (Active?.ConnectionId == id)
        {
            Active = null;
        }
    }

    public void Open(WorkspaceTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Key.ConnectionId != ConnectionId)
        {
            return;
        }
        int index = _tabs.FindIndex(t => t.Key == tab.Key);
        if (index < 0)
        {
            _tabs.Add(tab);
        }
        else
        {
            _tabs[index] = tab;
        }
        Active = tab.Key;
    }

    public void Activate(WorkspaceTabKey key)
    {
        if (key.ConnectionId == ConnectionId && _tabs.Any(t => t.Key == key))
        {
            Active = key;
        }
    }

    public void Close(WorkspaceTabKey key)
    {
        WorkspaceTab[] visible = OpenTabs.ToArray();
        int index = Array.FindIndex(visible, t => t.Key == key);
        if (index < 0)
        {
            return;
        }
        _tabs.RemoveAll(t => t.Key == key);
        if (Active == key)
        {
            WorkspaceTab[] remaining = OpenTabs.ToArray();
            Active = remaining.Length == 0 ? null : remaining[Math.Min(index, remaining.Length - 1)].Key;
        }
    }
}

public sealed class ProjectFilesState(Guid connectionId, ProjectLocation location)
{
    public Guid ConnectionId { get; } = connectionId;
    public ProjectLocation Location { get; } = location;
    public string DirectoryPath { get; internal set; } = "";
    public string Query { get; internal set; } = "";
    public IReadOnlyList<ProjectFileEntry> Entries { get; internal set; } = [];
    public bool Limited { get; internal set; }
    public bool Loaded { get; internal set; }
    public bool Loading { get; internal set; }
    public string? Error { get; internal set; }
    public string? SelectedPath { get; internal set; }
    public ProjectFilePreview? Preview { get; internal set; }
    public bool Reading { get; internal set; }
    public string? PreviewError { get; internal set; }
    public double PreviewOffset { get; set; }
    public double PreviewHorizontalOffset { get; set; }
    public double ListOffset { get; set; }
}

/// <summary>Caller-context state, with independent listing/read generations and connection invalidation.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Each operation disposes its own token source when its asynchronous work exits.")]
public sealed class ProjectFilesWorkspace : IDisposable
{
    private readonly Dictionary<(Guid, string), ProjectFilesState> _states = [];
    private Guid? _connectionId;
    private IProjectFilesConnection? _connection;
    private CancellationTokenSource? _listing;
    private CancellationTokenSource? _reading;
    private long _listGeneration;
    private long _readGeneration;
    public ProjectFilesState? Current { get; private set; }
    public event EventHandler? Changed;

    public void SetConnection(Guid? id, IProjectFilesConnection? connection)
    {
        Suspend();
        _connectionId = id;
        _connection = connection;
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ForgetConnection(Guid id)
    {
        if (Current?.ConnectionId == id)
        {
            Suspend();
            Current = null;
        }
        foreach (var key in _states.Keys.Where(key => key.Item1 == id).ToArray())
        {
            _states.Remove(key);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ProjectFilesState? Open(ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (_connectionId is not { } id || _connection is null)
        {
            return null;
        }
        Suspend();
        if (!_states.TryGetValue((id, location.Directory), out ProjectFilesState? state))
        {
            state = new(id, location);
            _states.Add((id, location.Directory), state);
        }
        // Re-resolved project identity changes invalidate old content.
        if (state.Location != location)
        {
            state = new(id, location);
            _states[(id, location.Directory)] = state;
        }
        Current = state;
        Changed?.Invoke(this, EventArgs.Empty);
        return state;
    }

    public void Suspend()
    {
        _listGeneration++;
        _readGeneration++;
        _listing?.Cancel();
        _reading?.Cancel();
        _listing = null;
        _reading = null;
        if (Current is { } state)
        {
            state.Loading = false;
            state.Reading = false;
        }
    }

    public void EditQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Current is not { } state)
        {
            return;
        }
        _listGeneration++;
        _listing?.Cancel();
        _listing = null;
        state.Loading = false;
        state.Loaded = false;
        state.Query = query;
        state.Entries = [];
        state.Error = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task BrowseAsync(string path, string query)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        if (Current is not { } state || _connection is not { } connection)
        {
            return;
        }
        _listing?.Cancel();
        using CancellationTokenSource cancel = new();
        _listing = cancel;
        long generation = ++_listGeneration;
        state.DirectoryPath = path;
        state.ListOffset = 0;
        state.Query = query;
        state.Entries = [];
        state.Error = null;
        state.Loading = true;
        state.Loaded = false;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            if (query.Length > ProjectFileLimits.SearchCharacters)
            {
                throw new WorkspaceException(WorkspaceFailure.Size, "Search is limited to 512 characters.");
            }
            ProjectFileList result = query.Length == 0
                ? await connection.ListFilesAsync(state.Location.Directory, path, cancel.Token).ConfigureAwait(true)
                : await connection.FindFilesAsync(state.Location.Directory, query, cancel.Token).ConfigureAwait(true);
            if (generation != _listGeneration)
            {
                return;
            }
            state.Entries = result.Entries;
            state.Limited = result.Limited;
            state.Loaded = true;
        }
        catch (WorkspaceException error)
        {
            if (generation == _listGeneration)
            {
                state.Error = SafeError(error);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _listGeneration)
            {
                _listing = null;
                state.Loading = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public async Task SelectAsync(string path)
    {
        if (Current is not { } state || _connection is not { } connection)
        {
            return;
        }
        _reading?.Cancel();
        using CancellationTokenSource cancel = new();
        _reading = cancel;
        long generation = ++_readGeneration;
        state.SelectedPath = path;
        state.Preview = null;
        state.PreviewError = null;
        state.PreviewOffset = 0;
        state.PreviewHorizontalOffset = 0;
        state.Reading = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            ProjectFilePreview preview = await connection.ReadFileAsync(state.Location.Directory, path, cancel.Token).ConfigureAwait(true);
            if (generation == _readGeneration)
            {
                state.Preview = preview;
            }
        }
        catch (WorkspaceException error)
        {
            if (generation == _readGeneration)
            {
                state.PreviewError = SafeError(error);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _readGeneration)
            {
                _reading = null;
                state.Reading = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static string SafeError(WorkspaceException error) => error.Failure switch
    {
        WorkspaceFailure.NotFound => "The server path is missing or unavailable. Refresh or select another path.",
        WorkspaceFailure.Unauthorized => "Access was denied. Check the connection and server permissions.",
        WorkspaceFailure.Size => "The response is too large. Narrow the search or choose a smaller directory or file.",
        WorkspaceFailure.Cancelled => "Loading cancelled. Retry when ready.",
        _ => "Unable to load from the server. Check the connection or permissions and retry.",
    };

    public void Dispose()
    {
        Suspend();
        _connection = null;
        Current = null;
        _states.Clear();
    }
}
