using System;
using System.Threading.Tasks;

using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class ProjectFilesView : UserControl
{
    internal Func<ProjectFilesState, Task>? AddToPrompt { get; set; }
    private async void AddToPrompt_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_state is not { SelectedPath: not null } state || AddToPrompt is not { } add) { return; }
        AddPromptFile.IsEnabled = false;
        try { await add(state).ConfigureAwait(true); }
        catch (Exception error) when (error is ArgumentException or WorkspaceException)
        { if (_state == state) { AttachmentStatus.Text = error is ArgumentException ? error.Message : "Unable to read the server file. Refresh and choose a readable file."; } }
        finally { AddPromptFile.IsEnabled = true; }
    }
    private ProjectFilesWorkspace? _files;
    private ProjectFilesState? _state;
    private bool _rendering;
    private long _searchGeneration;
    private object? _entries;
    private object? _preview;
    private bool? _compactFiles;
    private ProjectFilesState? _listRestoreState;
    private double _listRestoreOffset;
    private DispatcherQueueTimer? _listRestoreTimer;
    private int _listRestoreTicks;
    private int _stableListFrames;

    public ProjectFilesView() { InitializeComponent(); }

    internal void Bind(ProjectFilesWorkspace files)
    {
        _files = files;
        files.Changed += FilesChanged;
    }

    internal void Unbind()
    {
        CancelListRestore();
        if (_files is { } files)
        {
            files.Changed -= FilesChanged;
        }
        _files = null;
    }

    internal void SavePosition()
    {
        _searchGeneration++;
        if (_state is not { } state)
        {
            return;
        }
        state.PreviewOffset = PreviewScroll.VerticalOffset;
        state.PreviewHorizontalOffset = PreviewScroll.HorizontalOffset;
        if (_listRestoreState != state && FindScroll(Entries) is { } scroll)
        {
            state.ListOffset = scroll.VerticalOffset;
        }
    }

    internal void Show(string server)
    {
        Identity.Text = server + " · Files";
        Render();
    }

    internal void FocusInitial() => SearchInput.Focus(FocusState.Programmatic);

    private void Files_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        BrowserColumn.Width = new GridLength(Math.Clamp(args.NewSize.Width * 0.28, 200, 320));
        bool compact = args.NewSize.Height < 600;
        if (_compactFiles != compact)
        {
            _compactFiles = compact;
            DirectoryNavigation.IsExpanded = !compact;
            PreviewLimits.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void FilesChanged(object? sender, EventArgs args)
    {
        if (_state != _files?.Current)
        {
            SavePosition();
        }
        Render();
    }

    private void Render()
    {
        ProjectFilesState? state = _files?.Current;
        if (state is null)
        {
            CancelListRestore();
            _state = null;
            _entries = null;
            _preview = null;
            Entries.Items.Clear();
            PreviewText.Text = "";
            SelectedPath.Text = "";
            ServerDirectory.Text = "";
            return;
        }
        _rendering = true;
        try
        {
            bool switched = _state != state;
            if (switched) { AttachmentStatus.Text = ""; }
            AddPromptFile.IsEnabled = state.SelectedPath is not null;
            ProjectFilesPresentation presentation = ProjectFilesPresentation.From(state);
            _state = state;
            FilesHeading.Text = "Files · " + ProjectTreeNodePresentation.DisplayName(state.Location.Directory);
            ServerDirectory.Text = state.Location.Directory;
            ShellUi.Tooltip(ServerDirectory, state.Location.Directory);
            DirectoryInput.Text = state.DirectoryPath;
            SearchInput.Text = state.Query;
            if (!ReferenceEquals(_entries, state.Entries))
            {
                if (!switched) { CancelListRestore(); }
                _entries = state.Entries;
                Entries.Items.Clear();
                foreach (ProjectFileEntry entry in state.Entries)
                {
                    TextBlock label = ShellUi.Text((entry.IsDirectory ? "▸ " : "") + entry.Path, "ShellBodyTextStyle");
                    label.Tag = entry;
                    label.TextTrimming = TextTrimming.CharacterEllipsis;
                    ListViewItem item = new() { Content = label, Tag = entry };
                    AutomationProperties.SetName(item, (entry.IsDirectory ? "Directory " : "File ") + entry.Path);
                    ShellUi.Tooltip(item, entry.Path);
                    Entries.Items.Add(item);
                    if (entry.Path == state.SelectedPath) { Entries.SelectedItem = item; }
                }
            }
            ListStatus.Text = presentation.Listing;
            RetryList.Visibility = state.Error is null ? Visibility.Collapsed : Visibility.Visible;
            SelectedPath.Text = presentation.SelectedPath;
            ShellUi.Tooltip(SelectedPath, SelectedPath.Text);
            PreviewStatus.Text = presentation.PreviewStatus;
            RetryPreview.Visibility = state.PreviewError is null ? Visibility.Collapsed : Visibility.Visible;
            if (!ReferenceEquals(_preview, state.Preview) || switched)
            {
                _preview = state.Preview;
                PreviewText.Text = presentation.Text;
                double offset = state.PreviewOffset;
                double horizontal = state.PreviewHorizontalOffset;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_state == state)
                    {
                        PreviewScroll.ChangeView(horizontal, offset, null, true);
                    }
                });
            }
            if (switched)
            {
                // Virtualized list layout and focus can reset the offset after a
                // queued callback. A bounded timer also progresses when rendering is idle.
                CancelListRestore();
                _listRestoreState = state;
                _listRestoreOffset = state.ListOffset;
                _listRestoreTimer = DispatcherQueue.CreateTimer();
                _listRestoreTimer.Interval = TimeSpan.FromMilliseconds(25);
                _listRestoreTimer.IsRepeating = true;
                _listRestoreTimer.Tick += RestoreListOnTick;
                _listRestoreTimer.Start();
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RestoreListOnTick(DispatcherQueueTimer sender, object args)
    {
        if (_listRestoreState != _state || Visibility != Visibility.Visible || ++_listRestoreTicks > 20)
        {
            CancelListRestore();
            return;
        }
        if (FindScroll(Entries) is not { } scroll || _listRestoreOffset > 0 && scroll.ScrollableHeight == 0) { return; }
        double target = Math.Min(_listRestoreOffset, scroll.ScrollableHeight);
        if (Math.Abs(scroll.VerticalOffset - target) < 1)
        {
            if (++_stableListFrames >= 2 && _listRestoreTicks >= 3) { CancelListRestore(); }
        }
        else
        {
            _stableListFrames = 0;
            scroll.ChangeView(null, target, null, true);
        }
    }

    private void CancelListRestore()
    {
        if (_listRestoreTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= RestoreListOnTick;
            _listRestoreTimer = null;
        }
        _listRestoreState = null;
        _listRestoreTicks = _stableListFrames = 0;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll)
        {
            return scroll;
        }
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScroll(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { } child)
            {
                return child;
            }
        }
        return null;
    }

    private async Task BrowseAsync()
    {
        _searchGeneration++;
        if (_files is { } files)
        {
            await files.BrowseAsync(DirectoryInput.Text, SearchInput.Text).ConfigureAwait(true);
        }
    }
    private async void Go_ClickAsync(object sender, RoutedEventArgs args) => await BrowseAsync().ConfigureAwait(true);
    private async void Directory_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            await BrowseAsync().ConfigureAwait(true);
        }
    }
    private async void Root_ClickAsync(object sender, RoutedEventArgs args)
    {
        _rendering = true;
        DirectoryInput.Text = "";
        SearchInput.Text = "";
        _rendering = false;
        await BrowseAsync().ConfigureAwait(true);
    }
    private async void Refresh_ClickAsync(object sender, RoutedEventArgs args)
    {
        ProjectFilesState? state = _files?.Current;
        await BrowseAsync().ConfigureAwait(true);
        if (_files?.Current == state && Visibility == Visibility.Visible && state?.SelectedPath is { } path)
        {
            await _files.SelectAsync(path).ConfigureAwait(true);
        }
    }
    private async void Search_TextChangedAsync(object sender, TextChangedEventArgs args)
    {
        if (_rendering || _files is null || SearchInput.Text == _files.Current?.Query)
        {
            return;
        }
        long generation = ++_searchGeneration;
        // Invalidate the preceding request immediately; debounce only the new request.
        _files.EditQuery(SearchInput.Text);
        await Task.Delay(250).ConfigureAwait(true);
        if (generation == _searchGeneration && Visibility == Visibility.Visible)
        {
            await BrowseAsync().ConfigureAwait(true);
        }
    }
    private async void Entries_ItemClickAsync(object sender, ItemClickEventArgs args)
    {
        if (_files is not { } files || args.ClickedItem is not FrameworkElement { Tag: ProjectFileEntry entry })
        {
            return;
        }
        if (entry.IsDirectory)
        {
            await files.BrowseAsync(entry.Path, "").ConfigureAwait(true);
        }
        else
        {
            await files.SelectAsync(entry.Path).ConfigureAwait(true);
        }
    }
    private async void RetryPreview_ClickAsync(object sender, RoutedEventArgs args)
    {
        if (_files?.Current?.SelectedPath is { } path)
        {
            await _files.SelectAsync(path).ConfigureAwait(true);
        }
    }
}
