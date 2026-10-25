using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Infrastructure.Workspaces;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView
{
    private PromptDraft? _attachmentOwner;
    private long _attachmentVersion = -1;

    private void RenderAttachments()
    {
        PromptDraft? draft = _live?.CurrentDraft;
        AttachLocal.IsEnabled = _live is { IsConnected: true, Location: not null };
        AttachmentFeedback.Visibility = AttachmentFeedback.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_attachmentOwner == draft && _attachmentVersion == draft?.Version) { return; }
        if (_attachmentOwner != draft) { AttachmentFeedback.Text = ""; }
        _attachmentOwner = draft;
        _attachmentVersion = draft?.Version ?? -1;
        AttachmentList.Children.Clear();
        AttachmentScroll.Visibility = draft?.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (draft is null) { return; }
        foreach (PromptAttachment file in draft.Files)
        {
            string identity = (file.IsServer ? "Server reference · " : "Local content · ") + file.Identity;
            TextBlock header = ShellUi.Text(file.Name + (file.IsServer ? " · server" : " · local"), "ShellCaptionTextStyle");
            header.TextTrimming = TextTrimming.CharacterEllipsis;
            TextBlock detail = ShellUi.Text(identity, "ShellCaptionTextStyle", TextWrapping.Wrap);
            detail.IsTextSelectionEnabled = true;
            StackPanel details = ShellUi.Stack(8);
            details.Width = 300;
            details.Children.Add(detail);
            Flyout flyout = new() { Content = details };
            Button inspect = new() { Content = header, Flyout = flyout, MaxWidth = 240, Style = ShellUi.Resource<Style>("ShellQuietButtonStyle") };
            AutomationProperties.SetName(inspect, "Inspect attachment " + identity);
            AutomationProperties.SetAutomationId(inspect, "InspectAttachment" + AttachmentList.Children.Count);
            ShellUi.Tooltip(inspect, identity);
            Button remove = ShellUi.LabelButton("Remove", null, (_, _) =>
            {
                if (_live?.CurrentDraft == draft)
                {
                    _live.RemoveAttachment(file);
                    flyout.Hide();
                    RenderAttachments();
                    UpdateSend();
                    AttachLocal.Focus(FocusState.Programmatic);
                }
            }, "ShellQuietButtonStyle");
            AutomationProperties.SetName(remove, "Remove " + file.Name);
            AutomationProperties.SetAutomationId(remove, "RemoveAttachment" + AttachmentList.Children.Count);
            details.Children.Add(remove);
            AttachmentList.Children.Add(inspect);
        }
    }

    private async void AttachLocal_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_live is not { } live) { return; }
        PromptDraft? owner = live.CurrentDraft;
        try
        {
            FileOpenPicker picker = new(XamlRoot.ContentIslandEnvironment.AppWindowId);
            picker.FileTypeFilter.Add("*");
            PickFileResult? result = await picker.PickSingleFileAsync();
            if (result is null || _live != live || live.CurrentDraft != owner) { return; }
            await live.AddLocalAttachmentAsync(new LocalPromptFileReader(), result.Path, CancellationToken.None).ConfigureAwait(true);
            if (live.CurrentDraft == owner) { AttachmentFeedback.Text = ""; }
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            if (_live == live && live.CurrentDraft == owner) { AttachmentFeedback.Text = error is ArgumentException ? error.Message : "The file picker is unavailable. Try again."; }
        }
        RenderAttachments();
        UpdateSend();
    }

    private async Task AddFileToPromptAsync(ProjectFilesState files)
    {
        if (_live is not { } live || live.Server?.Profile.Id != files.ConnectionId) { return; }
        long generation = _tabGeneration;
        if (live.Location != files.Location)
        {
            WorkspaceTabKey draftKey = new(files.ConnectionId, files.Location.Directory, null, false);
            _tabs.Open(new(draftKey, "New-session draft · " + ProjectName(files.Location.Directory)));
            await ActivateWorkspaceTabAsync(draftKey).ConfigureAwait(true);
            if (_tabs.Active != draftKey || live.Location != files.Location || live.SelectedSessionId is not null) { return; }
            generation = _tabGeneration;
        }
        if (_live != live || live.Server?.Profile.Id != files.ConnectionId || generation != _tabGeneration) { return; }
        try
        {
            await live.AddServerAttachmentAsync(files, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception error) when (error is ArgumentException or WorkspaceException)
        {
            if (_tabs.Active?.IsFiles == true) { throw; }
            AttachmentFeedback.Text = error is ArgumentException ? error.Message : "Unable to read the server file. Refresh and choose a readable file.";
            AttachmentFeedback.Visibility = Visibility.Visible;
            return;
        }
        if (generation != _tabGeneration || _live != live) { return; }
        WorkspaceTabKey key = new(files.ConnectionId, files.Location.Directory, live.SelectedSessionId, false);
        _tabs.Open(new(key, (live.Selected?.Title ?? "New-session draft") + " · " + ProjectName(files.Location.Directory)));
        await ActivateWorkspaceTabAsync(key).ConfigureAwait(true);
        DraftBox.Focus(FocusState.Programmatic);
    }
}
