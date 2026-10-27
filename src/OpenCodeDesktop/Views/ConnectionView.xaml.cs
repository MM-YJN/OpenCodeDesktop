using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The owning window invokes Shutdown to cancel, unbind and dispose the page's local environment workflow.")]
public sealed partial class ConnectionView : UserControl
{
    private readonly ServerConnections _connections;
    private readonly LocalEnvironment _local;
    private bool _showFeedback;
    private ServerEditorDialog? _dialog;
    private CancellationTokenSource? _dialogSession;

    public ConnectionView(ServerConnections connections, ILocalServerDiscovery localDiscovery)
    {
        _connections = connections;
        _local = new LocalEnvironment(connections, localDiscovery);
        InitializeComponent();
        _connections.Changed += ConnectionsChanged;
        _local.Changed += LocalChanged;
        ActualThemeChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Activate() => _ = _local.DetectAsync();
    public void Deactivate()
    {
        CloseDialog();
        _local.Deactivate();
    }

    public void Shutdown()
    {
        CloseDialog();
        _local.Changed -= LocalChanged;
        _local.Dispose();
        _connections.Changed -= ConnectionsChanged;
    }

    private void ConnectionsChanged(object? sender, EventArgs args) => Refresh();
    private void LocalChanged(object? sender, EventArgs args) => Refresh();

    private async void DetectLocal_ClickAsync(object sender, RoutedEventArgs args) => await _local.DetectAsync().ConfigureAwait(true);

    private async void AddLocal_ClickAsync(object sender, RoutedEventArgs args) => await _local.AddAsync().ConfigureAwait(true);

    /// <summary>Connects to the profile with its saved credential when possible; otherwise opens the connect dialog to ask for the password.</summary>
    public void ConnectOrPrompt(ServerProfile? profile)
    {
        if (profile is null || _connections.IsConnecting)
        {
            return;
        }

        ServerProfile current = _connections.Profiles.FirstOrDefault(candidate => candidate.Id == profile.Id) ?? profile;
        if (_connections.Active?.Profile.Id == current.Id)
        {
            ShowDialog(current, ServerEditorIntent.Edit);
            return;
        }

        if (current.CredentialReference is not null)
        {
            _showFeedback = true;
            _ = _connections.ConnectAsync(current.Id, "");
            return;
        }

        ShowDialog(current, ServerEditorIntent.Connect);
    }

    private void ShowDialog(ServerProfile? profile, ServerEditorIntent intent)
    {
        if (_dialog is not null || XamlRoot is null)
        {
            return;
        }

        if (profile is not null)
        {
            profile = _connections.Profiles.FirstOrDefault(candidate => candidate.Id == profile.Id) ?? profile;
        }

        bool isActive = profile is not null && _connections.Active?.Profile.Id == profile.Id;
        ServerEditorDialog dialog = new(profile, intent, isActive) { XamlRoot = XamlRoot };
        Guid? editingId = intent == ServerEditorIntent.Add ? null : profile?.Id;
        CancellationTokenSource session = new();
        _dialog = dialog;
        _dialogSession = session;

        dialog.TestHandler = async () =>
        {
            ConnectionResult result = await _connections.TestAsync(editingId, dialog.Origin, dialog.EnteredPassword, session.Token).ConfigureAwait(true);
            if (!session.IsCancellationRequested)
            {
                dialog.ShowResult(ResultSeverity(result), result.Message);
            }
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            try
            {
                args.Cancel = !await CommitDialogAsync(dialog, editingId, intent, session.Token).ConfigureAwait(true);
            }
            finally
            {
                deferral.Complete();
            }
        };
        dialog.Closed += (_, _) =>
        {
            session.Cancel();
            if (ReferenceEquals(_dialog, dialog))
            {
                _dialog = null;
            }

            if (ReferenceEquals(_dialogSession, session))
            {
                _dialogSession = null;
            }
        };
        _ = ShowSafeAsync(dialog, session);
    }

    private async Task ShowSafeAsync(ServerEditorDialog dialog, CancellationTokenSource session)
    {
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // The XamlRoot can disappear while the window is closing.
            session.Cancel();
            if (ReferenceEquals(_dialog, dialog))
            {
                _dialog = null;
            }

            if (ReferenceEquals(_dialogSession, session))
            {
                _dialogSession = null;
            }
        }
    }

    private async Task<bool> CommitDialogAsync(ServerEditorDialog dialog, Guid? editingId, ServerEditorIntent intent, CancellationToken token)
    {
        if (_connections.IsConnecting)
        {
            dialog.ShowResult(InfoBarSeverity.Warning, "Finish the current connection attempt, then save again.");
            return false;
        }

        // Set before Save: the raised change notification refreshes the page feedback immediately.
        _showFeedback = true;
        ServerProfile? profile = _connections.Save(editingId, dialog.ProfileName, dialog.Origin, dialog.Remember);
        if (profile is null)
        {
            dialog.ShowResult(InfoBarSeverity.Warning, _connections.Message);
            return false;
        }

        if (intent != ServerEditorIntent.Connect)
        {
            return true;
        }

        await _connections.ConnectAsync(profile.Id, dialog.EnteredPassword, token).ConfigureAwait(true);
        if (_connections.Active?.Profile.Id == profile.Id || token.IsCancellationRequested)
        {
            return true;
        }

        dialog.ShowResult(ResultSeverity(_connections.LastResult), _connections.Message);
        return false;
    }

    private static InfoBarSeverity ResultSeverity(ConnectionResult? result) =>
        result is null || result.Outcome == ConnectionOutcome.Cancelled ? InfoBarSeverity.Informational
        : result.IsReady ? InfoBarSeverity.Success
        : result.Outcome == ConnectionOutcome.ValidationFailed ? InfoBarSeverity.Warning
        : InfoBarSeverity.Error;

    private void CloseDialog()
    {
        _dialogSession?.Cancel();
        _dialog?.Hide();
    }

    private void Refresh()
    {
        bool busy = _connections.IsConnecting || _local.IsAdding;
        bool localStatusChanged = LocalDetectionStatus.Text != _local.Message;
        LocalDetectionStatus.Text = _local.Message;
        LocalDetectionBadge.Text = _local.Detected is { } detected
            ? "OpenCode " + detected.Version
            : _local.IsBusy ? "Checking" : "";
        LocalDetectionBadge.Foreground = _local.Detected is null ? ShellUi.Brush("ShellTextTertiaryBrush") : ShellUi.Brush("ShellConnectedBrush");
        LocalDetectionProgress.Visibility = _local.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        DetectLocalButton.IsEnabled = !_local.IsBusy && !_connections.IsConnecting;
        AddLocalButton.Visibility = _local.Detected is null ? Visibility.Collapsed : Visibility.Visible;
        AddLocalButton.IsEnabled = _local.CanAdd;
        AddLocalButton.Content = _local.IsAlreadyConnected ? "Local environment connected" : "Add local environment";
        if (localStatusChanged)
        {
            FrameworkElementAutomationPeer.FromElement(LocalDetectionStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        EmptyState.Visibility = _connections.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderProfiles(busy);

        AddServerButton.IsEnabled = !busy;
        ConnectingProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshResult();
    }

    private void RefreshResult()
    {
        ResultBar.IsOpen = _showFeedback || _connections.CredentialError is not null;
        ResultBar.Message = _connections.CredentialError ?? _connections.Message;
        ResultBar.Severity = _connections.CredentialError is not null ? InfoBarSeverity.Warning : _connections.LastResult is { } result
            ? result.IsReady ? InfoBarSeverity.Success : result.Outcome == ConnectionOutcome.Cancelled ? InfoBarSeverity.Informational : InfoBarSeverity.Error
            : InfoBarSeverity.Informational;
    }

    private void RenderProfiles(bool busy)
    {
        ServerList.Children.Clear();
        foreach (ServerProfile profile in _connections.Profiles)
        {
            ConnectionCardPresentation presentation = ShellPresentation.ConnectionCard(profile, _connections.Active, _connections.ConnectingId, busy);
            Border card = ShellUi.Template<Border>("ConnectionCardTemplate");
            card.Background = ShellUi.Brush(presentation.IsActive ? "ShellAccentSurfaceBrush" : "ShellCardSurfaceBrush");
            card.BorderBrush = ShellUi.Brush(presentation.IsActive ? "ShellAccentBrush" : "ShellBorderBrush");
            Border iconSurface = ShellUi.Part<Border>(card, "IconSurface");
            iconSurface.Background = ShellUi.Brush(presentation.IsActive ? "ShellConnectedSurfaceBrush" : "ShellSubtleSurfaceBrush");
            iconSurface.BorderBrush = ShellUi.Brush(presentation.IsActive ? "ShellConnectedBrush" : "ShellBorderBrush");
            FontIcon icon = ShellUi.Part<FontIcon>(card, "Icon");
            icon.Glyph = ShellUi.Glyph(presentation.IsActive ? "ShellIconCheck" : "ShellIconServer");
            icon.Foreground = ShellUi.Brush(presentation.IsActive ? "ShellConnectedBrush" : "ShellTextSecondaryBrush");
            ShellUi.Part<TextBlock>(card, "Name").Text = presentation.Name;
            TextBlock address = ShellUi.Part<TextBlock>(card, "Address");
            address.Text = presentation.Address;
            ShellUi.Tooltip(address, presentation.Address);
            ShellUi.Part<ContentControl>(card, "State").Content = ShellUi.Badge(presentation.StateLabel,
                presentation.IsActive ? ShellTone.Success : presentation.IsConnecting ? ShellTone.Info : ShellTone.Neutral,
                presentation.IsActive ? "ShellIconCheck" : presentation.IsConnecting ? "ShellIconClock" : null);
            TextBlock version = ShellUi.Part<TextBlock>(card, "Version");
            version.Text = presentation.IsActive && _connections.Active is { } active ? "OpenCode " + active.Version : "";
            version.Visibility = version.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Button select = ShellUi.Part<Button>(card, "Select");
            select.Content = presentation.PrimaryActionLabel;
            select.Style = ShellUi.Resource<Style>(presentation.IsActive ? "ShellSecondaryButtonStyle" : "ShellPrimaryButtonStyle");
            AutomationProperties.SetName(select, presentation.AutomationName);
            select.Click += (_, _) => ConnectOrPrompt(profile);
            Button disconnect = ShellUi.Part<Button>(card, "Disconnect");
            disconnect.Visibility = presentation.IsActive ? Visibility.Visible : Visibility.Collapsed;
            disconnect.Click += (_, _) =>
            {
                CloseDialog();
                _connections.Disconnect();
                _showFeedback = true;
                Refresh();
            };
            Button more = ShellUi.Part<Button>(card, "More");
            more.Visibility = !presentation.IsActive && presentation.CanRemove ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(more, "More actions for " + profile.Name);
            ShellUi.Tooltip(more, "More actions for " + profile.Name);
            ShellUi.Part<MenuFlyoutItem>(card, "Edit").Click += (_, _) => ShowDialog(profile, ServerEditorIntent.Edit);
            ShellUi.Part<MenuFlyoutItem>(card, "Remove").Click += (_, _) =>
            {
                _showFeedback = true;
                _connections.Remove(profile.Id);
            };
            AutomationProperties.SetName(card, presentation.AutomationName);
            ServerList.Children.Add(card);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs args) => ShowDialog(null, ServerEditorIntent.Add);

    internal void FocusInitial() => AddServerButton.Focus(FocusState.Programmatic);
}
