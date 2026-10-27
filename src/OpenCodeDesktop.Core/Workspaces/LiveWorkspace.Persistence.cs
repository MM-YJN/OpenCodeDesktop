using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    public event EventHandler? NavigationChanged;
    public event EventHandler? UserNavigation;

    internal void CancelRestoration() => CancelLoad();

    internal void RestoreNavigation(WorkspaceSettings settings)
    {
        NotificationPreferences = settings.Notifications;
        _notificationTargets.AddRange(settings.NotificationTargets);
        foreach (SavedConnection saved in settings.Connections)
        {
            ServerProfile profile = _connections.Profiles.Single(p => p.Id == saved.Id);
            Backend backend = new(profile) { Directory = saved.SelectedDirectory };
            foreach (SavedProject project in saved.Projects)
            {
                backend.Projects.Add(project.Directory, new(new(project.Directory, project.ProjectId))
                {
                    SelectedId = project.SessionId,
                    ConfirmedId = project.SessionId,
                    PreferredModel = project.TaskSelection is { ModelId: { Length: > 0 } model, ModelProviderId: { Length: > 0 } provider }
                        ? new(model, provider, model)
                        : null,
                    PreferredAgent = project.TaskSelection is { AgentId: { Length: > 0 } agent } ? agent : null,
                });
            }
            _backends.Add(profile.Id, backend);
        }
    }

    internal WorkspaceSettings CaptureSettings() => new(WorkspaceSettings.CurrentVersion, _connections.AutomaticConnectionId,
        _connections.Profiles.Select(profile =>
        {
            _backends.TryGetValue(profile.Id, out Backend? backend);
            if (backend?.Profile.Address.Display != profile.Address.Display)
            {
                backend = null;
            }

            return new SavedConnection(profile.Id, profile.Name, profile.Address.Display, profile.RememberCredentials,
                profile.CredentialReference, backend?.Directory, backend?.Projects.Values.Where(p => p.Listed)
                    .Select(p => new SavedProject(p.Location.Directory, p.Location.ProjectId, p.ConfirmedId)
                    {
                        TaskSelection = p.PreferredModel is not null || p.PreferredAgent is not null
                            ? new(p.PreferredModel?.ProviderId, p.PreferredModel?.Id, p.PreferredAgent)
                            : null,
                    }).ToArray() ?? []);
        }).ToArray()) { Notifications = NotificationPreferences, NotificationTargets = _notificationTargets.ToArray() };
}
