using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Core.Workspaces;

public sealed partial class LiveWorkspace
{
    public event EventHandler? NavigationChanged;
    public event EventHandler? UserNavigation;

    internal void CancelRestoration() => CancelLoad();

    internal void RestoreNavigation(WorkspaceSettings settings)
    {
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
                    .Select(p => new SavedProject(p.Location.Directory, p.Location.ProjectId, p.ConfirmedId)).ToArray() ?? []);
        }).ToArray());
}
