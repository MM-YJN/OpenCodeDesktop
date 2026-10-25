using System;
using System.Collections.Generic;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Presentation;
using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Views;

public sealed partial class DetailsView : UserControl
{
    private ChangeReviewWorkspace? _review;
    private ChangeReviewState? _owner;
    internal event EventHandler<string>? PreviewRequested;

    internal void Bind(ChangeReviewWorkspace review)
    {
        _review = review;
        ReviewContent.Bind(review);
        ReviewContent.PreviewRequested += PreviewFile;
    }

    private void PreviewFile(object? sender, string path) => PreviewRequested?.Invoke(this, path);

    internal void Unbind()
    {
        ReviewContent.PreviewRequested -= PreviewFile;
        ReviewContent.Unbind();
        _review = null;
        _owner = null;
    }

    internal void SavePosition() => ReviewContent.SavePosition();

    internal void UpdateReview(IReadOnlyList<HistoryMessage> history)
    {
        if (_owner != _review?.Current)
        {
            _owner = _review?.Current;
            DetailsPivot.SelectedIndex = _owner?.ShowChanges == true ? 1 : 0;
        }
        ReviewContent.UpdateHistory(history);
        ReviewContent.Render();
    }

    internal async System.Threading.Tasks.Task OpenTurnAsync(string from, string? path)
    {
        if (_owner is { } owner) { owner.ShowChanges = true; }
        DetailsPivot.SelectedIndex = 1;
        await ReviewContent.OpenTurnAsync(from, path).ConfigureAwait(true);
    }

    private async void Details_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_owner is not { } owner) { return; }
        bool changes = DetailsPivot.SelectedIndex == 1;
        if (owner.ShowChanges == changes) { return; }
        owner.ShowChanges = changes;
        if (owner.ShowChanges && owner.Result is null && !owner.Loading && owner.Error is null && _review is { } review)
        {
            await review.LoadAsync(owner.Comparison).ConfigureAwait(true);
        }
    }

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
                        ShellUi.Text("Review and tools", "ShellCaptionTextStyle"),
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
