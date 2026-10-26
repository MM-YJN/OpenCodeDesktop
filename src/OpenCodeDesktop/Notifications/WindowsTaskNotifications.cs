using System;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Notifications;

/// <summary>Local Windows delivery only. A successful Show is submission to Windows, not proof of display.</summary>
internal sealed class WindowsTaskNotifications : IDisposable
{
    private bool _registered;
    private string? _submissionFailure;
    internal event EventHandler<Guid>? Invoked;
    internal string Status { get; private set; } = "Windows notifications are unavailable. Tasks continue in the workspace.";

    internal void Register()
    {
        try
        {
            if (!AppNotificationManager.IsSupported()) { return; }
            AppNotificationManager.Default.NotificationInvoked += NotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
            RefreshStatus();
        }
        catch (Exception)
        {
            Status = "Windows notification registration failed. Review tasks in the workspace.";
        }
    }

    internal void RefreshStatus()
    {
        if (!_registered) { return; }
        try
        {
            Status = AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled
                ? _submissionFailure ?? "Windows notifications are enabled. Windows may silence banners with Do not disturb. Delivery is not guaranteed."
                : "Windows notifications are disabled or unavailable. Check Windows Settings > System > Notifications. Tasks continue in the workspace.";
        }
        catch (Exception) { Status = "Windows notification settings could not be read. Review tasks in the workspace."; }
    }

    internal void Show(TaskNotificationTarget target)
    {
        if (!_registered) { return; }
        try
        {
            RefreshStatus();
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled) { return; }
            AppNotification notification = new AppNotificationBuilder()
                .AddArgument("target", target.Token.ToString("N"))
                .AddText(target.Kind == TaskNotificationKind.Completion ? "Task completed" : "Task needs your attention")
                .AddText(target.Kind == TaskNotificationKind.Completion ? "Open the session to review the result." : "Open the session to review a pending request.")
                .BuildNotification();
            notification.Expiration = target.Created.AddDays(7);
            AppNotificationManager.Default.Show(notification);
            _submissionFailure = notification.Id == 0 ? "Windows did not accept the notification. Review the session in the workspace." : null;
            RefreshStatus();
        }
        catch (Exception) { Status = _submissionFailure = "Windows notification submission failed. Review tasks in the workspace."; }
    }

    private void NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Invalid input still opens guidance; never interpret arguments as paths or commands.
        Guid token = args.Arguments.TryGetValue("target", out string? value) && Guid.TryParseExact(value, "N", out Guid parsed) ? parsed : Guid.Empty;
        Invoked?.Invoke(this, token);
    }

    public void Dispose()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= NotificationInvoked;
            if (_registered) { AppNotificationManager.Default.Unregister(); }
        }
        catch (Exception) { /* Shutdown cannot affect task execution. */ }
    }
}
