using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Connections;

namespace OpenCodeDesktop.Views;

public enum ServerEditorIntent
{
    Add,
    Edit,
    Connect,
}

/// <summary>Add/edit/connect dialog for one server profile. The caller wires <see cref="TestHandler"/> and PrimaryButtonClick.</summary>
public sealed partial class ServerEditorDialog : ContentDialog
{
    private readonly bool _editable;
    private bool _testing;

    public ServerEditorDialog(ServerProfile? profile, ServerEditorIntent intent, bool isActive)
    {
        _editable = !isActive;
        InitializeComponent();
        HasSavedCredential = profile?.CredentialReference is not null;
        Title = isActive ? "Active server"
            : intent == ServerEditorIntent.Add ? "Add server"
            : intent == ServerEditorIntent.Connect ? "Connect" : "Edit server";
        PrimaryButtonText = intent == ServerEditorIntent.Connect ? "Connect" : "Save";
        CloseButtonText = isActive ? "Close" : "Cancel";

        ServerOrigin.PlaceholderText = ServerAddress.DefaultOrigin;
        ServerName.Text = profile?.Name ?? "";
        ServerOrigin.Text = profile?.Address.Display ?? ServerAddress.DefaultOrigin;
        RememberCredentials.IsChecked = profile?.RememberCredentials ?? true;
        DefaultAddressHint.Text = "Leave the address blank to use " + ServerAddress.DefaultOrigin + ", the Windows OpenCode v2 default.";

        if (HasSavedCredential && intent != ServerEditorIntent.Add)
        {
            SavedPasswordPanel.Visibility = Visibility.Visible;
            ServerPassword.Visibility = Visibility.Collapsed;
            PasswordHint.Visibility = Visibility.Visible;
            PasswordHint.Text = "A password is saved on this device and will be reused. Select it to enter a new password. Changing the address discards the saved password.";
        }

        if (!_editable)
        {
            ServerName.IsEnabled = ServerOrigin.IsEnabled = ServerPassword.IsEnabled = false;
            SavedPasswordButton.IsEnabled = false;
            RememberCredentials.IsEnabled = false;
            TestButton.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = false;
            ShowResult(InfoBarSeverity.Informational, "Disconnect this server before editing or removing it.");
        }

        Opened += (_, _) => FocusInitial(intent);
    }

    public string ProfileName => ServerName.Text;
    public string Origin => ServerOrigin.Text;
    public bool Remember => RememberCredentials.IsChecked == true;
    public bool HasSavedCredential { get; }

    /// <summary>Empty when the saved credential should be reused (the saved-password field stayed closed or blank).</summary>
    public string EnteredPassword => ServerPassword.Visibility == Visibility.Visible ? ServerPassword.Password : "";

    public Func<Task>? TestHandler { get; set; }

    public void ShowResult(InfoBarSeverity severity, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private void FocusInitial(ServerEditorIntent intent)
    {
        Control target = intent == ServerEditorIntent.Add ? ServerName
            : SavedPasswordPanel.Visibility == Visibility.Visible ? SavedPasswordButton
            : ServerPassword;
        target.Focus(FocusState.Programmatic);
    }

    private void SavedPassword_Click(object sender, RoutedEventArgs args)
    {
        SavedPasswordPanel.Visibility = Visibility.Collapsed;
        ServerPassword.Visibility = Visibility.Visible;
        PasswordHint.Text = "Enter a new password, or leave it blank to keep the saved password. Changing the address discards the saved password.";
        ServerPassword.Focus(FocusState.Programmatic);
    }

    private async void Test_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_testing || TestHandler is null)
        {
            return;
        }

        _testing = true;
        TestButton.IsEnabled = false;
        IsPrimaryButtonEnabled = false;
        TestProgress.IsActive = true;
        TestProgress.Visibility = Visibility.Visible;
        try
        {
            await TestHandler().ConfigureAwait(true);
        }
        finally
        {
            _testing = false;
            TestButton.IsEnabled = true;
            IsPrimaryButtonEnabled = _editable;
            TestProgress.IsActive = false;
            TestProgress.Visibility = Visibility.Collapsed;
        }
    }
}
