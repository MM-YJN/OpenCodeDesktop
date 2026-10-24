using System;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Presentation;

using static OpenCodeDesktop.Views.WorkspaceControls;

namespace OpenCodeDesktop.Views;

internal sealed class DetailsView : UserControl
{
    private readonly Pivot _tabs = new();
    private readonly ScrollViewer[] _scrolls = [Scroll(Stack()), Scroll(Stack()), Scroll(Stack())];
    private SampleSession? _session;
    private bool _loading;

    public DetailsView()
    {
        string[] names = ["Info", "Files", "Changes"];
        for (int index = 0; index < names.Length; index++)
        {
            int tab = index;
            PivotItem item = new() { Header = names[index], Content = _scrolls[index], Margin = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, names[index]);
            _tabs.Items.Add(item);
            _scrolls[index].ViewChanged += (_, _) =>
            {
                if (!_loading && _session is not null && _tabs.SelectedIndex == tab)
                {
                    _session.DetailsOffsets[tab] = _scrolls[tab].VerticalOffset;
                }
            };
        }

        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_loading && _session is not null)
            {
                _session.DetailsTab = _tabs.SelectedIndex;
                RestoreScroll();
            }
        };
        Content = _tabs;
    }

    public void Show(SampleSession? session)
    {
        _loading = true;
        _session = session;
        if (session is null)
        {
            foreach (ScrollViewer scroll in _scrolls)
            {
                scroll.Content = Text("No session selected. Choose a session to inspect its details.");
            }
        }
        else
        {
            _scrolls[0].Content = Info(session);
            _scrolls[1].Content = Files(session);
            _scrolls[2].Content = Changes(session);
            _tabs.SelectedIndex = session.DetailsTab;
        }

        RestoreScroll();
        _loading = false;
    }

    private void RestoreScroll()
    {
        if (_session is not null)
        {
            int tab = _session.DetailsTab;
            _scrolls[tab].UpdateLayout();
            _scrolls[tab].ChangeView(null, _session.DetailsOffsets[tab], null, true);
        }
    }

    private static StackPanel Info(SampleSession session)
    {
        StackPanel panel = Stack();
        panel.Children.Add(Text("Sample metadata", true));
        panel.Children.Add(Text($"Session: {session.Title}\nProvider: Embedded fixtures\nModel: {session.Model}\nAgent: {session.Agent}\nExecution: {session.Execution}"));
        panel.Children.Add(new Expander { Header = "Context · 24% (sample)", Content = Text("24,000 / 100,000 sample tokens. No live usage is measured.") });
        panel.Children.Add(new Expander { Header = "Activity & usage (sample)", Content = Text("Read 3 files · Edited 2 fixtures\nInput: 1,200 tokens · Output: 480 tokens\nIllustrative cost: $0.02 · No billing occurred.") });
        TextBox editor = new() { Header = "Local sample instructions", Text = session.Instructions, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        TextBlock feedback = Text("Changes apply only to this sample session.");
        StackPanel instructions = Stack();
        instructions.Children.Add(editor);
        instructions.Children.Add(Actions(Button("Save sample", () =>
        {
            session.Instructions = editor.Text;
            feedback.Text = "Saved in memory only.";
        }),
            Button("Cancel edits", () =>
            {
                editor.Text = session.Instructions;
                feedback.Text = "Unsaved edits discarded.";
            })));
        instructions.Children.Add(feedback);
        panel.Children.Add(new Expander { Header = "Instructions", Content = instructions, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return panel;
    }

    private static StackPanel Files(SampleSession session)
    {
        StackPanel panel = Stack();
        TextBox search = new() { Header = "Search embedded files", PlaceholderText = "Filter paths" };
        ListView files = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 180 };
        TextBlock empty = Text("");
        TextBlock path = Text(session.SelectedFile);
        TextBlock preview = Code("");
        void Select()
        {
            path.Text = session.SelectedFile;
            string content = SampleFixtures.Files[session.SelectedFile];
            preview.Text = session.SelectedFile.EndsWith(".png", StringComparison.Ordinal) || session.SelectedFile.StartsWith("missing/", StringComparison.Ordinal)
                ? content : string.Join("\n", content.Split('\n').Select((line, index) => $"{index + 1,3}  {line}"));
        }

        void Filter()
        {
            files.Items.Clear();
            foreach (string name in SampleFixtures.Files.Keys.Where(name => name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                files.Items.Add(name);
            }

            files.SelectedItem = session.SelectedFile;
            empty.Text = files.Items.Count == 0 ? "No matching sample files." : "Embedded content only — server paths are never opened locally.";
        }

        files.SelectionChanged += (_, _) =>
        {
            if (files.SelectedItem is string name)
            {
                session.SelectedFile = name;
                Select();
            }
        };
        search.TextChanged += (_, _) => Filter();
        panel.Children.Add(search);
        panel.Children.Add(Button("Clear file search", () => search.Text = ""));
        panel.Children.Add(files);
        panel.Children.Add(empty);
        panel.Children.Add(path);
        panel.Children.Add(preview);
        Filter();
        Select();
        return panel;
    }

    private static StackPanel Changes(SampleSession session)
    {
        StackPanel panel = Stack();
        TextBlock basis = Text("");
        ListView files = new() { MaxHeight = 160 };
        TextBlock diff = Code("");
        void Select()
        {
            diff.Text = string.IsNullOrEmpty(session.SelectedChange) ? "No changes in this sample comparison."
                : SampleFixtures.Diff(session.SelectedChange, session.Comparison);
        }

        void Compare()
        {
            basis.Text = SampleFixtures.Basis(session.Comparison) + "\nUnified diff: + added, - removed, space unchanged.";
            string[] paths = SampleFixtures.Changes(session.Comparison);
            if (!paths.Contains(session.SelectedChange))
            {
                session.SelectedChange = paths.FirstOrDefault() ?? "";
            }

            files.Items.Clear();
            foreach (string path in paths)
            {
                files.Items.Add(path);
            }

            files.SelectedItem = session.SelectedChange;
            Select();
        }

        panel.Children.Add(Choice("Compare", ["Session turn", "Working copy", "Branch"], session.Comparison,
            index =>
            {
                session.Comparison = index;
                Compare();
            }));
        files.SelectionChanged += (_, _) =>
        {
            if (files.SelectedItem is string path)
            {
                session.SelectedChange = path;
                Select();
            }
        };
        panel.Children.Add(basis);
        panel.Children.Add(files);
        panel.Children.Add(diff);
        Compare();
        return panel;
    }
}
