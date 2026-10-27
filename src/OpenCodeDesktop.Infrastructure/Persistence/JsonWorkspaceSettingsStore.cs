using System.Text.Json;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Infrastructure.Persistence;

/// <summary>Bounded, versioned snapshots. An unreadable store remains read-only until explicit reset.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The app-lifetime managed semaphore never creates a WaitHandle; pending cancelled I/O can still release the gate after window shutdown.")]
public sealed class JsonWorkspaceSettingsStore(string directory) : IWorkspaceSettingsStore
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16 };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(directory, "workspace.v1.json");
    private bool _writable;

    public Task<WorkspaceSettings?> LoadAsync(CancellationToken cancellationToken) => Task.Run(() => LoadCoreAsync(cancellationToken), cancellationToken);

    private async Task<WorkspaceSettings?> LoadCoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _writable = false;
            WorkspaceSettings? settings;
            try
            {
                using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                if (stream.Length > MaximumBytes)
                {
                    throw new InvalidDataException("Settings exceed the supported size.");
                }
                // Limit the read itself as well as checking the reported length.
                byte[] bytes = new byte[MaximumBytes + 1];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    count += read;
                }
                if (count > MaximumBytes)
                {
                    throw new InvalidDataException("Settings exceed the supported size.");
                }

                settings = JsonSerializer.Deserialize<WorkspaceSettings>(bytes.AsSpan(0, count), s_json)
                    ?? throw new InvalidDataException("Invalid settings.");
                Validate(settings);
            }
            catch (FileNotFoundException)
            {
                settings = null;
            }
            catch (DirectoryNotFoundException)
            {
                settings = null;
            }
            _writable = true;
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveAsync(WorkspaceSettings settings, CancellationToken cancellationToken) => Task.Run(() => SaveCoreAsync(settings, cancellationToken), cancellationToken);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Flush(true) requests a durable disk flush before replacement; FlushAsync has no equivalent flag. This method runs on the thread pool.")]
    private async Task SaveCoreAsync(WorkspaceSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, s_json);
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("Settings exceed the supported size.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!_writable)
            {
                throw new InvalidOperationException("Read or explicitly reset settings before writing.");
            }

            Directory.CreateDirectory(directory);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, null);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Move(_path, _path + ".recovery-" + Guid.NewGuid().ToString("N"));
            }

            _writable = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Validate(WorkspaceSettings settings)
    {
        if (settings.Version != WorkspaceSettings.CurrentVersion || settings.Connections is null || settings.Connections.Count > 128 ||
            settings.Notifications is null || settings.NotificationTargets is null || settings.NotificationTargets.Count > 128)
        {
            throw new InvalidDataException("Unsupported settings format.");
        }

        HashSet<Guid> ids = [];
        HashSet<Guid> tokens = [];
        foreach (TaskNotificationTarget target in settings.NotificationTargets)
        {
            if (target is null || target.Token == Guid.Empty || !tokens.Add(target.Token) || target.ConnectionId == Guid.Empty ||
                !ServerAddress.TryParse(target.Address, out _) || !Text(target.Directory, 32768) || !Text(target.ProjectId, 4096) ||
                !Text(target.SessionId, 4096) || !Enum.IsDefined(target.Kind) ||
                (target.Kind == TaskNotificationKind.Completion ? target.RequestId is not null : !Text(target.RequestId, 4096)))
            {
                throw new InvalidDataException("Invalid notification destination.");
            }
        }
        HashSet<string> addresses = new(StringComparer.Ordinal);
        foreach (SavedConnection connection in settings.Connections)
        {
            if (connection is null || connection.Id == Guid.Empty || !ids.Add(connection.Id) || !Text(connection.Name, 256)
                || !ServerAddress.TryParse(connection.Address, out ServerAddress? address) || !addresses.Add(address!.Display)
                || connection.Projects is null || connection.Projects.Count > 256
                || (connection.CredentialReference is not null && (!connection.RememberCredentials || !Guid.TryParseExact(connection.CredentialReference, "N", out _))))
            {
                throw new InvalidDataException("Invalid saved connection.");
            }

            ValidateProjects(connection);
        }
        if (settings.ActiveConnectionId is { } active && !ids.Contains(active))
        {
            throw new InvalidDataException("Invalid active connection.");
        }
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Contains('\0', StringComparison.Ordinal);

    private static void ValidateProjects(SavedConnection connection)
    {
        HashSet<string> directories = new(StringComparer.Ordinal);
        foreach (SavedProject project in connection.Projects)
        {
            if (project is null || !Text(project.Directory, 32768) || !directories.Add(project.Directory)
                || !Text(project.ProjectId, 4096) || (project.SessionId is not null && !Text(project.SessionId, 4096))
                || project.TaskSelection is { } selection &&
                ((selection.ModelId is not null && !Text(selection.ModelId, 4096)) ||
                 (selection.ModelProviderId is not null && !Text(selection.ModelProviderId, 4096)) ||
                 (selection.AgentId is not null && !Text(selection.AgentId, 4096))))
            {
                throw new InvalidDataException("Invalid saved project.");
            }
        }
        if (connection.SelectedDirectory is not null && !directories.Contains(connection.SelectedDirectory))
        {
            throw new InvalidDataException("Invalid selected directory.");
        }
    }
}
