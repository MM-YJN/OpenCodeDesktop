namespace OpenCodeDesktop.Core.Workspaces;

public enum ReviewMode { Working, Branch, Committed, Turns }
public sealed record ReviewComparison(ReviewMode Mode, string? Base = null, string? From = null, string? To = null);
public sealed record ReviewFile(string Path, string Status, int Additions, int Deletions, string Patch, bool Truncated);
public sealed record ReviewResult(string Basis, IReadOnlyList<ReviewFile> Files, bool Limited);
public static class ReviewLimits
{
    public const int ResponseBytes = 4194304;
    public const int Files = 1000;
    public const int PatchCharacters = 100000;
}
public interface IChangeReviewConnection
{
    Task<ReviewResult> ReviewAsync(ProjectLocation location, string? sessionId, ReviewComparison comparison, CancellationToken cancellationToken);
}
public sealed class ChangeReviewState(Guid connectionId, ProjectLocation location, string? sessionId)
{
    public Guid ConnectionId { get; } = connectionId;
    public ProjectLocation Location { get; } = location;
    public string? SessionId { get; } = sessionId;
    public ReviewComparison Comparison { get; internal set; } = new(ReviewMode.Working);
    public ReviewResult? Result { get; internal set; }
    public string? Error { get; internal set; }
    public bool Loading { get; internal set; }
    public string? SelectedPath { get; set; }
    public double PatchOffset { get; set; }
    public double HorizontalOffset { get; set; }
    public double ListOffset { get; set; }
    public bool ShowChanges { get; set; }
    internal Dictionary<ReviewComparison, (string? Path, double Vertical, double Horizontal, double List)> Positions { get; } = [];
}

/// <summary>Caller-context review state. Each load owns its cancellation source; generations also reject noncooperative late results.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The pending operation owns and disposes its cancellation source.")]
public sealed class ChangeReviewWorkspace : IDisposable
{
    private readonly Dictionary<(Guid, ProjectLocation, string?), ChangeReviewState> _states = [];
    private IChangeReviewConnection? _connection;
    private Guid? _connectionId;
    private CancellationTokenSource? _load;
    private long _generation;
    public ChangeReviewState? Current { get; private set; }
    public event EventHandler? Changed;

    public void SetConnection(Guid? id, IChangeReviewConnection? connection)
    {
        Suspend();
        _connectionId = id;
        _connection = connection;
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ForgetConnection(Guid id)
    {
        foreach (var key in _states.Keys.Where(k => k.Item1 == id).ToArray()) { _states.Remove(key); }
        if (Current?.ConnectionId == id) { Suspend(); Current = null; }
    }

    public void Open(ProjectLocation? location, string? sessionId)
    {
        if (Current is { } current && current.ConnectionId == _connectionId && current.Location == location && current.SessionId == sessionId) { return; }
        Suspend();
        Current = location is not null && _connectionId is { } id && _connection is not null
            ? _states.GetValueOrDefault((id, location, sessionId)) ?? Add(id, location, sessionId) : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ChangeReviewState Add(Guid id, ProjectLocation location, string? sessionId)
    {
        ChangeReviewState state = new(id, location, sessionId);
        _states.Add((id, location, sessionId), state);
        return state;
    }

    public void Suspend()
    {
        _generation++;
        _load?.Cancel();
        _load = null;
        if (Current is { } state) { state.Loading = false; }
    }

    public async Task LoadAsync(ReviewComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (Current is not { } state || _connection is not { } connection) { return; }
        Suspend();
        bool changed = state.Comparison != comparison;
        state.Positions[state.Comparison] = (state.SelectedPath, state.PatchOffset, state.HorizontalOffset, state.ListOffset);
        if (changed)
        {
            var position = state.Positions.GetValueOrDefault(comparison);
            state.SelectedPath = position.Path;
            state.PatchOffset = position.Vertical;
            state.HorizontalOffset = position.Horizontal;
            state.ListOffset = position.List;
        }
        state.Comparison = comparison;
        state.Result = null;
        state.Error = null;
        state.Loading = true;
        long generation = ++_generation;
        using CancellationTokenSource cancel = new();
        _load = cancel;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            if (comparison.Mode == ReviewMode.Turns && (state.SessionId is null || string.IsNullOrEmpty(comparison.From)))
            {
                state.Error = "Choose a recorded user message to review its turn.";
                return;
            }
            ReviewResult result = await connection.ReviewAsync(state.Location, state.SessionId, comparison, cancel.Token).ConfigureAwait(true);
            if (generation != _generation) { return; }
            state.Result = result;
            if (!result.Files.Any(f => f.Path == state.SelectedPath))
            {
                state.SelectedPath = result.Files.FirstOrDefault()?.Path;
                state.PatchOffset = state.HorizontalOffset = 0;
            }
        }
        catch (WorkspaceException error)
        {
            if (generation == _generation)
            {
                state.Error = error.Failure switch
                {
                    WorkspaceFailure.NotFound => "The project, session or turn is missing. Refresh history and select again.",
                    WorkspaceFailure.Size => "Review exceeds the 4 MiB response limit. Choose a narrower comparison.",
                    WorkspaceFailure.Unauthorized => "Review access was denied. Check the server connection.",
                    _ => "Review is unavailable. Check the connection, repository and comparison base, then refresh.",
                };
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _generation)
            {
                state.Loading = false;
                _load = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose() { Suspend(); _connection = null; Current = null; _states.Clear(); }
}
