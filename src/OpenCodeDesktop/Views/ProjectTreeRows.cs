using System;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

internal interface IProjectTreeRow
{
    Grid Root { get; }
    void Update(ProjectTreeNodePresentation model, bool connected);
}

/// <summary>Project row with its inline draft and overflow actions.</summary>
internal sealed class ProjectRowView : IProjectTreeRow
{
    private readonly FontIcon _folder;
    private readonly TextBlock _name;
    private readonly TextBlock _detail;
    private readonly Button _newSession;
    private readonly Button _more;

    internal ProjectRowView(ProjectTreeNodePresentation model, Action newSession)
    {
        Root = ShellUi.Template<Grid>("ProjectRowTemplate");
        _folder = ShellUi.Part<FontIcon>(Root, "Folder");
        _name = ShellUi.Part<TextBlock>(Root, "Label");
        _newSession = ShellUi.Part<Button>(Root, "NewSession");
        _newSession.Click += (_, _) => newSession();
        _more = ShellUi.Part<Button>(Root, "More");
        _detail = ShellUi.Part<TextBlock>(Root, "Detail");
        _detail.Margin = new Thickness(ShellUi.Dimension("ShellIconSmall") + ShellUi.Dimension("ShellSpaceTiny"), 0, 0, 0);

        Update(model, true);
    }

    public Grid Root { get; }
    internal Button MoreButton => _more;
    internal Button NewSessionButton => _newSession;

    public void Update(ProjectTreeNodePresentation model, bool connected)
    {
        _name.Text = model.Label;
        _name.FontWeight = model.IsActive ? FontWeights.SemiBold : FontWeights.Normal;
        _folder.Foreground = model.IsActive ? ShellUi.Brush("ShellAccentBrush") : ShellUi.Brush("ShellTextSecondaryBrush");
        _newSession.IsEnabled = connected;
        _more.IsEnabled = connected;
        _detail.Text = model.Detail ?? "";
        _detail.Visibility = string.IsNullOrEmpty(_detail.Text) ? Visibility.Collapsed : Visibility.Visible;
        _detail.Foreground = model.IsErrorDetail
            ? ShellUi.Brush("ShellDangerBrush")
            : model.IsLoading ? ShellUi.Brush("ShellAccentBrush") : ShellUi.Brush("ShellTextTertiaryBrush");

        string accessible = "Project " + model.Label + ", " + model.Path +
            (model.IsActive ? ", active" : "") +
            (model.Detail is { Length: > 0 } ? ", " + model.Detail : "");
        AutomationProperties.SetName(Root, accessible);
        AutomationProperties.SetName(_newSession, "New session draft in " + model.Label);
        AutomationProperties.SetName(_more, "More actions for " + model.Label);
        ShellUi.Tooltip(Root, model.Path + (model.Detail is { Length: > 0 } ? "\n" + model.Detail : ""));
        ShellUi.Tooltip(_newSession, "New session draft in " + model.Label);
        ShellUi.Tooltip(_more, "More actions for " + model.Label);
    }
}

/// <summary>A leaf session row; the native tree owns selection and focus.</summary>
internal sealed class SessionRowView : IProjectTreeRow
{
    private readonly TextBlock _title;

    internal SessionRowView(ProjectTreeNodePresentation model)
    {
        Root = ShellUi.Template<Grid>("SessionRowTemplate");
        Root.Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0);
        _title = ShellUi.Part<TextBlock>(Root, "Label");
        MoreButton = ShellUi.Part<Button>(Root, "More");
        Update(model, true);
    }

    public Grid Root { get; }

    internal Button MoreButton { get; }

    public void Update(ProjectTreeNodePresentation model, bool _)
    {
        _title.Text = model.Label;
        AutomationProperties.SetName(MoreButton, "Actions for session " + model.Label);
        AutomationProperties.SetName(Root, "Session " + model.Label + " in " + ProjectTreeNodePresentation.DisplayName(model.Directory));
        ShellUi.Tooltip(Root, model.Label);
    }
}

/// <summary>The active project's unsaved-session row.</summary>
internal sealed class DraftRowView : IProjectTreeRow
{
    private readonly FontIcon _icon;

    internal DraftRowView(ProjectTreeNodePresentation model)
    {
        Root = ShellUi.Template<Grid>("DraftRowTemplate");
        Root.Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0);
        _icon = ShellUi.Part<FontIcon>(Root, "Icon");
        Update(model, true);
    }

    public Grid Root { get; }

    public void Update(ProjectTreeNodePresentation model, bool _)
    {
        _icon.Foreground = ShellUi.Brush("ShellTextSecondaryBrush");
        AutomationProperties.SetName(Root, "New session draft in " + ProjectTreeNodePresentation.DisplayName(model.Directory));
        ShellUi.Tooltip(Root, "Keep unsent input before a server session is created.");
    }
}

/// <summary>Paging action row for one project.</summary>
internal sealed class LoadMoreRowView : IProjectTreeRow
{
    internal LoadMoreRowView(ProjectTreeNodePresentation model)
    {
        Root = ShellUi.Template<Grid>("LoadMoreRowTemplate");
        Root.Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0);
        Update(model, true);
    }

    public Grid Root { get; }

    public void Update(ProjectTreeNodePresentation model, bool _)
    {
        AutomationProperties.SetName(Root, "Load more sessions for " + ProjectTreeNodePresentation.DisplayName(model.Directory));
        ShellUi.Tooltip(Root, "Load the next page of sessions for this project.");
    }
}
