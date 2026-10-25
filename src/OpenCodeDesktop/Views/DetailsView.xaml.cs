using System;
using System.Collections.Generic;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class DetailsView : UserControl
{
    public DetailsView()
    {
        InitializeComponent();
    }

    internal void Update(DetailsPresentation presentation)
    {
        InfoPanel.Children.Clear();
        TextBlock title = ShellUi.Text(presentation.Title, "ShellSubheadingTextStyle");
        ShellUi.Heading(title, AutomationHeadingLevel.Level3);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        ShellUi.Tooltip(title, presentation.Title);
        InfoPanel.Children.Add(title);
        InfoPanel.Children.Add(ShellUi.Text(presentation.Subtitle, "ShellTertiaryTextStyle"));
        InfoPanel.Children.Add(ShellUi.Divider());

        Grid fields = new() { ColumnSpacing = 12, RowSpacing = 10 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (DetailFieldPresentation field in presentation.Fields)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = ShellUi.Text(field.Label, "ShellTertiaryTextStyle", TextWrapping.NoWrap);
            label.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(label, fields.RowDefinitions.Count - 1);
            TextBlock value = ShellUi.Text(field.Value, "ShellSecondaryTextStyle");
            value.IsTextSelectionEnabled = true;
            Grid.SetRow(value, fields.RowDefinitions.Count - 1);
            Grid.SetColumn(value, 1);
            fields.Children.Add(label);
            fields.Children.Add(value);
        }

        InfoPanel.Children.Add(fields);
        Border unavailable = ShellUi.Card(new StackPanel
        {
            Spacing = ShellUi.Dimension("ShellSpaceSmall"),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = ShellUi.Dimension("ShellSpaceSmall"),
                    Children =
                    {
                        ShellUi.Icon("ShellIconInfo", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellInfoBrush")),
                        ShellUi.Text("Not available in this release", "ShellCaptionTextStyle"),
                    },
                },
                ShellUi.Text(presentation.UnavailableMessage, "ShellTertiaryTextStyle"),
            },
        }, "ShellSubtleCardStyle");
        InfoPanel.Children.Add(unavailable);

        if (DetailsPivot.SelectedIndex < 0)
        {
            DetailsPivot.SelectedIndex = 0;
        }
    }
}
