namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    public void RemoveAttachment(PromptAttachment attachment)
    {
        CurrentDraft?.Remove(attachment);
        Notify();
    }

    public async Task AddLocalAttachmentAsync(ILocalPromptFileReader reader, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        PromptDraft? draft = CurrentDraft;
        long generation = _generation;
        PromptAttachment file = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(true);
        if (generation != _generation || draft != CurrentDraft) { return; }
        draft?.Add(file);
        Notify();
    }

    public async Task AddServerAttachmentAsync(ProjectFilesState files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!IsConnected || files.ConnectionId != Server?.Profile.Id || Location != files.Location || files.SelectedPath is not { } path ||
            _connections.Context is not IProjectFilesConnection connection)
        { throw new ArgumentException("Open a draft in this project before adding a file."); }
        PromptDraft? draft = CurrentDraft;
        long generation = _generation;
        ProjectFilePreview preview = await connection.ReadFileAsync(files.Location.Directory, path, cancellationToken).ConfigureAwait(true);
        if (generation != _generation || draft != CurrentDraft) { return; }
        draft?.Add(PromptAttachment.Server(files.Location.Directory, path, preview));
        Notify();
    }

    private void ConsumeAdmittedDrafts()
    {
        if (Tasks is not { } tasks || _backend is not { } backend) { return; }
        foreach (Project project in backend.Projects.Values)
        {
            foreach (var pending in project.PendingDrafts.ToArray())
            {
                if (tasks.Find(pending.Key)?.AdmittedSubmissionId == pending.Value.SubmissionId)
                {
                    project.GetDraft(pending.Key).Consume(pending.Value.Version);
                    project.PendingDrafts.Remove(pending.Key);
                }
            }
        }
    }

    public async Task<string?> VerifyFileReferenceAsync(string source, CancellationToken cancellationToken)
    {
        if (Location is not { } location || _connections.Context is not IProjectFilesConnection connection)
        {
            return null;
        }
        long generation = _generation;
        long connectionGeneration = _connectionGeneration;
        string? path = await ProjectFileReferences.VerifyAsync(connection, location.Directory, source, cancellationToken).ConfigureAwait(true);
        return generation == _generation && connectionGeneration == _connectionGeneration && Location == location ? path : null;
    }
}
