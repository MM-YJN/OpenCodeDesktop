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
        Root = new Grid
        {
            MinHeight = ShellUi.Dimension("ShellTreeProjectRowHeight"),
            ColumnSpacing = ShellUi.Dimension("ShellSpaceTiny"),
            Padding = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _folder = ShellUi.Icon("ShellIconFolder", ShellUi.Dimension("ShellIconSmall"));
        _folder.VerticalAlignment = VerticalAlignment.Center;
        Root.Children.Add(_folder);

        _name = ShellUi.Text("", "ShellBodyTextStyle", TextWrapping.NoWrap);
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_name, 1);
        Root.Children.Add(_name);

        StackPanel actions = ShellUi.Stack(2, Orientation.Horizontal);
        actions.VerticalAlignment = VerticalAlignment.Center;
        _newSession = ShellUi.IconButton("ShellIconAdd", "New session draft", (_, _) => newSession(), "ShellTreeActionButtonStyle");
        _more = ShellUi.IconButton("ShellIconMore", "More actions", (_, _) => { }, "ShellTreeActionButtonStyle");
        actions.Children.Add(_newSession);
        actions.Children.Add(_more);
        Grid.SetColumn(actions, 2);
        Root.Children.Add(actions);

        _detail = ShellUi.Text("", "ShellCaptionTextStyle", TextWrapping.NoWrap);
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail.Margin = new Thickness(ShellUi.Dimension("ShellIconSmall") + ShellUi.Dimension("ShellSpaceTiny"), 0, 0, 0);
        Grid.SetRow(_detail, 1);
        Grid.SetColumn(_detail, 1);
        Grid.SetColumnSpan(_detail, 2);
        Root.Children.Add(_detail);

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
        ShellUi.Tooltip(Root, model.Path);
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
        Root = new Grid
        {
            MinHeight = ShellUi.Dimension("ShellTreeSessionRowHeight"),
            Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _title = ShellUi.Text("", "ShellBodyTextStyle", TextWrapping.NoWrap);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        Root.Children.Add(_title);
        Update(model, true);
    }

    public Grid Root { get; }

    public void Update(ProjectTreeNodePresentation model, bool _)
    {
        _title.Text = model.Label;
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
        Root = new Grid
        {
            MinHeight = ShellUi.Dimension("ShellTreeSessionRowHeight"),
            ColumnSpacing = ShellUi.Dimension("ShellSpaceSmall"),
            Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _icon = ShellUi.Icon("ShellIconAdd", ShellUi.Dimension("ShellIconSmall"));
        Root.Children.Add(_icon);
        TextBlock label = ShellUi.Text("New session draft", "ShellSecondaryTextStyle", TextWrapping.NoWrap);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        Root.Children.Add(label);
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
        Root = new Grid
        {
            MinHeight = ShellUi.Dimension("ShellTreeSessionRowHeight"),
            Padding = new Thickness(2, 0, ShellUi.Dimension("ShellSpaceSmall"), 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock label = ShellUi.Text("Load more sessions", "ShellTertiaryTextStyle", TextWrapping.NoWrap);
        Root.Children.Add(label);
        Update(model, true);
    }

    public Grid Root { get; }

    public void Update(ProjectTreeNodePresentation model, bool _)
    {
        AutomationProperties.SetName(Root, "Load more sessions for " + ProjectTreeNodePresentation.DisplayName(model.Directory));
        ShellUi.Tooltip(Root, "Load the next page of sessions for this project.");
    }
}
