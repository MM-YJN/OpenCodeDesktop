using System;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

public sealed partial class DetailsView : UserControl
{
    private SampleSession? _session;
    private bool _loading;
    private ScrollViewer[] Scrolls => [InfoScroll, FilesScroll, ChangesScroll];

    public DetailsView() => InitializeComponent();

    internal void Show(SampleSession? session)
    {
        _loading = true;
        _session = session;
        EmptyState.Visibility = session is null ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = session is null ? Visibility.Collapsed : Visibility.Visible;
        if (session is not null)
        {
            Metadata.Text = $"Session: {session.Title}\nProvider: Embedded fixtures\nModel: {session.Model}\nAgent: {session.Agent}\nExecution: {session.Execution}";
            Instructions.Text = session.Instructions;
            InstructionsFeedback.Text = "Changes apply only to this sample session.";
            FileSearch.Text = "";
            FilterFiles();
            SelectFile();
            Comparison.SelectedIndex = session.Comparison;
            Compare();
            Tabs.SelectedIndex = session.DetailsTab;
        }

        RestoreScroll();
        _loading = false;
    }

    private void RestoreScroll()
    {
        if (_session is not null)
        {
            int tab = _session.DetailsTab;
            ScrollViewer scroll = Scrolls[tab];
            scroll.UpdateLayout();
            scroll.ChangeView(null, _session.DetailsOffsets[tab], null, true);
        }
    }

    private void Tabs_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!_loading && _session is not null && Tabs.SelectedIndex >= 0)
        {
            _session.DetailsTab = Tabs.SelectedIndex;
            RestoreScroll();
        }
    }

    private void Scroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (!_loading && _session is not null && Tabs.SelectedIndex >= 0 && ReferenceEquals(sender, Scrolls[Tabs.SelectedIndex]))
        {
            _session.DetailsOffsets[Tabs.SelectedIndex] = ((ScrollViewer)sender).VerticalOffset;
        }
    }

    private void SaveInstructions_Click(object sender, RoutedEventArgs args)
    {
        if (_session is not null)
        {
            _session.Instructions = Instructions.Text;
            InstructionsFeedback.Text = "Saved in memory only.";
        }
    }

    private void CancelInstructions_Click(object sender, RoutedEventArgs args)
    {
        Instructions.Text = _session?.Instructions ?? "";
        InstructionsFeedback.Text = "Unsaved edits discarded.";
    }

    private void SelectFile()
    {
        if (_session is not { } session)
        {
            return;
        }

        FilePath.Text = session.SelectedFile;
        string content = SampleFixtures.Files[session.SelectedFile];
        FilePreview.Text = session.SelectedFile.EndsWith(".png", StringComparison.Ordinal) || session.SelectedFile.StartsWith("missing/", StringComparison.Ordinal)
            ? content : string.Join("\n", content.Split('\n').Select((line, index) => $"{index + 1,3}  {line}"));
    }

    private void FilterFiles()
    {
        if (_session is null)
        {
            return;
        }

        FilesList.ItemsSource = SampleFixtures.Files.Keys.Where(name => name.Contains(FileSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        FilesList.SelectedItem = _session.SelectedFile;
        FilesFeedback.Text = FilesList.Items.Count == 0 ? "No matching sample files." : "Embedded content only — server paths are never opened locally.";
    }

    private void FileSearch_Changed(object sender, TextChangedEventArgs args)
    {
        if (!_loading)
        {
            FilterFiles();
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs args) => FileSearch.Text = "";
    private void File_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_session is not null && FilesList.SelectedItem is string name)
        {
            _session.SelectedFile = name;
            SelectFile();
        }
    }

    private void SelectChange() => Diff.Text = _session is not { } session || string.IsNullOrEmpty(session.SelectedChange)
        ? "No changes in this sample comparison." : SampleFixtures.Diff(session.SelectedChange, session.Comparison);

    private void Compare()
    {
        if (_session is not { } session)
        {
            return;
        }

        ComparisonBasis.Text = SampleFixtures.Basis(session.Comparison) + "\nUnified diff: + added, - removed, space unchanged.";
        string[] paths = SampleFixtures.Changes(session.Comparison);
        if (!paths.Contains(session.SelectedChange))
        {
            session.SelectedChange = paths.FirstOrDefault() ?? "";
        }

        ChangedFiles.ItemsSource = paths;
        ChangedFiles.SelectedItem = session.SelectedChange;
        SelectChange();
    }

    private void Comparison_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!_loading && _session is not null && Comparison.SelectedIndex >= 0)
        {
            _session.Comparison = Comparison.SelectedIndex;
            Compare();
        }
    }

    private void ChangedFile_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_session is not null && ChangedFiles.SelectedItem is string path)
        {
            _session.SelectedChange = path;
            SelectChange();
        }
    }
}
