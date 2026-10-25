using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Views;

public sealed partial class ChangeReviewView : UserControl
{
    private ChangeReviewWorkspace? _review;
    private ChangeReviewState? _state;
    private ReviewResult? _result;
    private ReviewFile? _file;
    private bool _rendering;
    private bool _restoring;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _restoreTimer;
    private int _restoreTicks;
    private long _turnRequest;
    private IReadOnlyList<HistoryMessage> _history = [];
    internal event EventHandler<string>? PreviewRequested;

    public ChangeReviewView() { InitializeComponent(); }

    private void Review_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Keep both the file list and patch readable at the minimum workspace size.
        // Keyboard focus scrolls the comparison controls into view when compact.
        ComparisonControls.MaxHeight = args.NewSize.Height < 450 ? 96 : 220;
    }

    internal void Bind(ChangeReviewWorkspace review)
    {
        _review = review;
        review.Changed += Review_Changed;
    }

    internal void Unbind()
    {
        SavePosition();
        CancelRestore();
        if (_review is { } review) { review.Changed -= Review_Changed; }
        _review = null;
        _state = null;
    }

    internal void SavePosition()
    {
        if (_state is { } state && !_restoring && _file is not null)
        {
            state.PatchOffset = PatchScroll.VerticalOffset;
            state.HorizontalOffset = PatchScroll.HorizontalOffset;
        }
        if (_state is { Result: not null } owner && !_restoring && FindScroll(ChangedFiles) is { } list)
        {
            owner.ListOffset = list.VerticalOffset;
        }
    }

    internal void UpdateHistory(IReadOnlyList<HistoryMessage> history)
    {
        if (_history.Where(m => m.Kind == "user").Select(m => m.Id).SequenceEqual(history.Where(m => m.Kind == "user").Select(m => m.Id))) { return; }
        _history = history;
        RenderTurns();
    }

    private void RenderTurns()
    {
        string? from = (FromTurn.SelectedItem as ComboBoxItem)?.Tag as string ?? _state?.Comparison.From;
        string? to = (ToTurn.SelectedItem as ComboBoxItem)?.Tag as string ?? _state?.Comparison.To;
        FromTurn.Items.Clear();
        ToTurn.Items.Clear();
        ToTurn.Items.Add(new ComboBoxItem { Content = "Same turn", Tag = null });
        foreach (HistoryMessage message in _history.Where(m => m.Kind == "user"))
        {
            string text = message.Text.Replace('\n', ' ').Replace('\r', ' ');
            string label = message.Id + " · " + text[..Math.Min(text.Length, 60)];
            ComboBoxItem first = new() { Content = label, Tag = message.Id };
            ComboBoxItem last = new() { Content = label, Tag = message.Id };
            FromTurn.Items.Add(first); ToTurn.Items.Add(last);
            if (message.Id == from) { FromTurn.SelectedItem = first; }
            if (message.Id == to) { ToTurn.SelectedItem = last; }
        }
        if (FromTurn.SelectedIndex < 0 && FromTurn.Items.Count > 0) { FromTurn.SelectedIndex = FromTurn.Items.Count - 1; }
        if (ToTurn.SelectedIndex < 0) { ToTurn.SelectedIndex = 0; }
    }

    private void Review_Changed(object? sender, EventArgs args) => Render();

    internal void Render()
    {
        ChangeReviewState? state = _review?.Current;
        bool switched = _state != state;
        if (switched) { SavePosition(); }
        _rendering = true;
        try
        {
            _state = state;
            if (state is null)
            {
                CancelRestore();
                ChangedFiles.Items.Clear(); Patch.Text = ""; Basis.Text = "";
                ReviewStatus.Text = "Open a connected project to review changes.";
                PreviewButton.IsEnabled = false;
                _result = null; _file = null;
                return;
            }
            if (switched)
            {
                ComparisonMode.SelectedIndex = (int)state.Comparison.Mode;
                BaseInput.Text = state.Comparison.Base ?? "";
                FromTurn.SelectedItem = null; ToTurn.SelectedItem = null;
                RenderTurns();
            }
            ShowInputs();
            Basis.Text = state.Result?.Basis ?? "";
            ReviewStatus.Text = state.Loading ? "Loading comparison…" : state.Error ??
                (state.Result is { } result ? result.Files.Count == 0 ? "No file changes returned. Turns without recorded snapshots also return no patch."
                    : result.Files.Count + " changed files" + (result.Limited ? " · file list limited to 1,000" : "")
                    : "Choose a comparison and apply it.");
            if (switched || !ReferenceEquals(_result, state.Result))
            {
                _result = state.Result;
                ChangedFiles.Items.Clear();
                foreach (ReviewFile file in state.Result?.Files ?? [])
                {
                    TextBlock text = ShellUi.Text(file.Status + " · " + file.Path + "  +" + file.Additions + " −" + file.Deletions, "ShellCaptionTextStyle");
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                    ListViewItem item = new() { Content = text, Tag = file };
                    AutomationProperties.SetName(item, text.Text);
                    ShellUi.Tooltip(item, text.Text);
                    ChangedFiles.Items.Add(item);
                    if (file.Path == state.SelectedPath) { ChangedFiles.SelectedItem = item; }
                }
                ShowPatch();
            }
        }
        finally { _rendering = false; }
    }

    private void ShowInputs()
    {
        BaseInput.Visibility = ComparisonMode.SelectedIndex is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        TurnInputs.Visibility = ComparisonMode.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs args) { if (!_rendering) { ShowInputs(); } }

    private async void Refresh_ClickAsync(object sender, RoutedEventArgs args)
    {
        _turnRequest++;
        if (_review is null || ComparisonMode.SelectedIndex < 0) { return; }
        SavePosition();
        ReviewMode mode = (ReviewMode)ComparisonMode.SelectedIndex;
        await _review.LoadAsync(new(mode, mode is ReviewMode.Branch or ReviewMode.Committed && !string.IsNullOrWhiteSpace(BaseInput.Text) ? BaseInput.Text.Trim() : null,
            mode == ReviewMode.Turns ? (FromTurn.SelectedItem as ComboBoxItem)?.Tag as string : null,
            mode == ReviewMode.Turns ? (ToTurn.SelectedItem as ComboBoxItem)?.Tag as string : null)).ConfigureAwait(true);
    }

    internal async Task OpenTurnAsync(string from, string? path)
    {
        if (_review is null) { return; }
        long request = ++_turnRequest;
        ChangeReviewState? owner = _review.Current;
        SavePosition();
        await _review.LoadAsync(new(ReviewMode.Turns, From: from)).ConfigureAwait(true);
        if (request != _turnRequest || _review.Current is not { } state || state != owner || state.Comparison.From != from) { return; }
        state.SelectedPath = state.Result?.Files.FirstOrDefault(f => path is null || f.Path == path)?.Path;
        _rendering = true;
        ComparisonMode.SelectedIndex = 3;
        FromTurn.SelectedItem = FromTurn.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, from));
        ToTurn.SelectedIndex = 0;
        ChangedFiles.SelectedItem = ChangedFiles.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as ReviewFile)?.Path == state.SelectedPath);
        _rendering = false;
        ShowInputs(); ShowPatch();
        ChangedFiles.Focus(FocusState.Programmatic);
    }

    private void File_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_rendering || _state is not { } state) { return; }
        CancelRestore();
        state.SelectedPath = (ChangedFiles.SelectedItem as ListViewItem)?.Tag is ReviewFile file ? file.Path : null;
        state.PatchOffset = state.HorizontalOffset = 0;
        ShowPatch();
    }

    private void ShowPatch()
    {
        _file = _state?.Result?.Files.FirstOrDefault(f => f.Path == _state.SelectedPath);
        Patch.Text = _file?.Patch ?? "";
        FileStatus.Text = _file is { } file ? file.Path + (file.Truncated ? " · Patch limited to 100,000 characters" : file.Patch.Length == 0 ? " · No textual patch supplied" : " · Unified diff, 3 context lines") : "Select a changed file";
        ShellUi.Tooltip(FileStatus, FileStatus.Text);
        PreviewButton.IsEnabled = _file is { Status: not "deleted" };
        ChangeReviewState? owner = _state;
        ReviewFile? selected = _file;
        CancelRestore();
        if (owner?.Result is null) { return; }
        _restoring = true;
        double vertical = owner.PatchOffset;
        double horizontal = owner.HorizontalOffset;
        double listOffset = owner.ListOffset;
        _restoreTimer = DispatcherQueue.CreateTimer();
        _restoreTimer.Interval = TimeSpan.FromMilliseconds(25);
        _restoreTimer.Tick += (_, _) =>
        {
            if (_state != owner || _file != selected || ++_restoreTicks > 20) { CancelRestore(); return; }
            ScrollViewer? list = FindScroll(ChangedFiles);
            PatchScroll.ChangeView(horizontal, Math.Min(vertical, PatchScroll.ScrollableHeight), null, true);
            list?.ChangeView(null, Math.Min(listOffset, list.ScrollableHeight), null, true);
            if (_restoreTicks >= 3 && Math.Abs(PatchScroll.VerticalOffset - Math.Min(vertical, PatchScroll.ScrollableHeight)) < 1 &&
                list is not null && Math.Abs(list.VerticalOffset - Math.Min(listOffset, list.ScrollableHeight)) < 1)
            {
                CancelRestore();
            }
        };
        _restoreTimer.Start();
    }

    private void CancelRestore()
    {
        _restoreTimer?.Stop();
        _restoreTimer = null;
        _restoring = false;
        _restoreTicks = 0;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) { return scroll; }
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScroll(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { } child) { return child; }
        }
        return null;
    }

    private void Patch_ViewChanged(object sender, ScrollViewerViewChangedEventArgs args) { if (!_restoring && !_rendering) { SavePosition(); } }
    private void Preview_Click(object sender, RoutedEventArgs args) { if (_file is { Status: not "deleted" } file) { PreviewRequested?.Invoke(this, file.Path); } }
}
