using System;
using System.Collections.Generic;

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
        InfoTitle.Text = presentation.Title;
        ShellUi.Tooltip(InfoTitle, presentation.Title);
        InfoSubtitle.Text = presentation.Subtitle;
        InfoFields.ItemsSource = presentation.Fields;
        UnavailableMessage.Text = presentation.UnavailableMessage;

        if (DetailsPivot.SelectedIndex < 0)
        {
            DetailsPivot.SelectedIndex = 0;
        }
    }
}
