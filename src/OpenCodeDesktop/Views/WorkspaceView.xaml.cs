using System;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Presentation;

using Windows.System;

using static OpenCodeDesktop.Views.WorkspaceControls;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView : UserControl
{
    private readonly SampleWorkspace _state = new();
    private readonly DetailsView _details = new();
    private bool? _projectsInline;
    private bool? _detailsInline;
    private bool _expanded;
    private bool _rendering;
    private double _detailsWidth = Dimension("ShellDetailsWidth");
    private TextBox? _draft;
    private ListViewItem? _selectedSessionItem;
    private FrameworkElement? _requestCard;

    public event EventHandler? SettingsRequested;
    public event EventHandler? AttentionChanged;
    public int AttentionCount => _state.AttentionCount;

    public WorkspaceView()
    {
        InitializeComponent();
        DetailsHost.Content = _details;
        Render();
    }

    private void Render()
    {
        _rendering = true;
        RenderProjects();
        SampleSession? session = _state.Selected;
        SessionTitle.Text = session?.Title ?? "Workspace";
        UpdateSessionPath();
        RenderConversation();
        RenderComposer();
        _details.Show(session);
        double offset = session?.ConversationOffset ?? 0;
        ConversationScroll.UpdateLayout();
        ConversationScroll.ChangeView(null, offset, null, true);
        _rendering = false;
        AttentionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSessionPath()
    {
        SampleSession? session = _state.Selected;
        SessionPath.Text = session is null ? "No server connected · No session selected"
            : $"Sample server path: {_state.Owner(session)!.Directory}\n{session.Worktree} · Execution: {session.Execution}";
    }

    private void RenderProjects()
    {
        ProjectList.Children.Clear();
        int count = 0;
        foreach (SampleProject project in _state.Projects)
        {
            SampleSession[] sessions = project.Sessions.Where(session =>
                (ArchivedFilter.IsChecked == true || !session.Archived)
                && (project.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                    || session.Title.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (sessions.Length == 0)
            {
                continue;
            }

            count += sessions.Length;
            StackPanel group = Stack();
            ListView list = new() { SelectionMode = ListViewSelectionMode.Single };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, project.Name + " sessions");
            foreach (SampleSession session in sessions)
            {
                TextBlock label = Text(session.Title + "\n" + (session.Archived ? "Archived · " : "") + session.Execution);
                // Session labels activate their list item; conversation text remains selectable.
                label.IsTextSelectionEnabled = false;
                ListViewItem item = new()
                {
                    Content = label,
                    Tag = session,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                MenuFlyout menu = new();
                AddMenu(menu, "Rename sample", () => EditSessionAsync(session, false));
                AddMenu(menu, "Fork sample", () =>
                {
                    _state.Fork(session);
                    Render();
                });
                AddMenu(menu, session.Archived ? "Restore sample" : "Archive sample", () =>
                {
                    session.Archived = !session.Archived;
                    RenderProjects();
                });
                AddMenu(menu, "Delete sample…", () => EditSessionAsync(session, true));
                item.ContextFlyout = menu;
                list.Items.Add(item);
                if (_state.Selected == session)
                {
                    list.SelectedItem = item;
                    _selectedSessionItem = item;
                }
            }

            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is ListViewItem { Tag: SampleSession selected } && selected != _state.Selected)
                {
                    _state.Selected = selected;
                    Render();
                    _selectedSessionItem?.Focus(FocusState.Programmatic);
                }
            };
            group.Children.Add(list);
            SampleSession worktreeSession = sessions.Contains(_state.Selected) ? _state.Selected! : sessions[0];
            group.Children.Add(Choice("Sample worktree", ["main", "sample/recipe-tags-worktree"], worktreeSession.Worktree == "main" ? 0 : 1,
                index =>
                {
                    worktreeSession.Worktree = index == 0 ? "main" : "sample/recipe-tags-worktree";
                    _state.Selected = worktreeSession;
                    Render();
                }));
            group.Children.Add(Button("New task here", () =>
            {
                _state.Create(project);
                Render();
            }));
            Expander expander = new() { Header = project.Name, Content = group, IsExpanded = project.Expanded, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            expander.Expanding += (_, _) => project.Expanded = true;
            expander.Collapsed += (_, _) => project.Expanded = false;
            ProjectList.Children.Add(expander);
        }

        if (count == 0)
        {
            ProjectList.Children.Add(Text("No matching sessions."));
        }
    }

    private static void AddMenu(MenuFlyout menu, string label, Action action)
    {
        MenuFlyoutItem item = new() { Text = label };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private async void EditSessionAsync(SampleSession session, bool delete)
    {
        TextBox title = new() { Header = "Sample session title", Text = session.Title };
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = delete ? "Delete this in-memory session?" : "Rename sample session",
            Content = delete ? Text("Only sample state is removed. Restarting the app restores the sample data.") : title,
            PrimaryButtonText = delete ? "Delete sample" : "Save sample title",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        title.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(title.Text);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (delete)
            {
                _state.Delete(session);
            }
            else
            {
                session.Title = title.Text.Trim();
            }

            Render();
        }
    }

    private void RenderConversation()
    {
        Conversation.Children.Clear();
        _requestCard = null;
        if (_state.Selected is not { } session)
        {
            Conversation.Children.Add(Card(Text("No session selected. Choose a session or create a new task.")));
            return;
        }

        if (session.IsNew)
        {
            Conversation.Children.Add(Text("New sample draft. Submitting appends a local message; no agent response will be generated."));
            foreach (string message in session.Messages)
            {
                Conversation.Children.Add(Card(Text(message)));
            }
            return;
        }

        Conversation.Children.Add(Text("You · sample\nAdd category tags to the recipe collection."));
        Conversation.Children.Add(Text("Assistant · recorded sample\nI will add recipe tags and update the ingredient filter. This conversation is illustrative; no agent is running."));
        Conversation.Children.Add(Card(Code("// Sample recipe filter\nvar matches = recipes.Where(recipe =>\n    recipe.Tags.Contains(category));")));
        Conversation.Children.Add(new Expander
        {
            Header = "Read files · Completed (sample)",
            Content = Text("Read 3 embedded fixtures. No files were opened on your computer.\nRecipeList.xaml\nRecipeFilter.cs\ndocs/notes.md"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        });
        Conversation.Children.Add(Button("Open sample file: src/RecipeList.xaml", () => OpenReference(false)));
        Conversation.Children.Add(Button("Review sample changes: 2 files, +4 / -2", () => OpenReference(true)));
        if (session.PermissionPending || session.PermissionResult.Length > 0)
        {
            StackPanel permission = Stack();
            permission.Children.Add(Text("Run verification? · sample permission", true));
            permission.Children.Add(Text("Scope: once · sample server directory\nCommand: dotnet test\nChoices only update this sample workspace; no permission is sent."));
            if (session.PermissionPending)
            {
                permission.Children.Add(Actions(Button("Reject sample", () => RespondPermission("Rejected")),
                    Button("Allow once (sample)", () => RespondPermission("Allow once"))));
            }
            else
            {
                permission.Children.Add(Text(session.PermissionResult));
            }

            Border card = Card(permission);
            if (session.PermissionPending)
            {
                _requestCard = card;
            }

            Conversation.Children.Add(card);
        }

        if (session.QuestionPending || session.QuestionResult.Length > 0)
        {
            StackPanel question = Stack();
            question.Children.Add(Text("Which quantity format should recipes use? · sample question", true));
            if (session.QuestionPending)
            {
                TextBox answer = new() { Header = "Answer (required)", Text = session.Answer, AcceptsReturn = true };
                answer.TextChanged += (_, _) => session.Answer = answer.Text;
                TextBlock feedback = Text("");
                question.Children.Add(answer);
                question.Children.Add(feedback);
                question.Children.Add(Actions(Button("Submit sample answer", () =>
                {
                    if (session.RespondQuestion(false))
                    {
                        Render();
                    }
                    else
                    {
                        feedback.Text = "Enter an answer before submitting.";
                        answer.Focus(FocusState.Programmatic);
                    }
                }), Button("Cancel question", () =>
                {
                    session.RespondQuestion(true);
                    Render();
                })));
            }
            else
            {
                question.Children.Add(Text(session.QuestionResult));
            }

            Border card = Card(question);
            if (session.QuestionPending)
            {
                _requestCard = card;
            }

            Conversation.Children.Add(card);
        }

        foreach (string message in session.Messages)
        {
            Conversation.Children.Add(Card(Text(message)));
        }
    }

    private void RespondPermission(string choice)
    {
        _state.Selected!.RespondPermission(choice);
        Render();
    }

    private void RenderComposer()
    {
        Composer.Children.Clear();
        if (_state.Selected is not { } session)
        {
            Composer.Children.Add(Text("Select a session or create a new task to write a message."));
            return;
        }

        StackPanel pending = Stack();
        foreach (SampleInput input in session.PendingInputs.ToArray())
        {
            pending.Children.Add(Text(input.Text));
            pending.Children.Add(Choice("Delivery for sample input", ["Queue", "Steer"], input.Mode == "Queue" ? 0 : 1,
                index => input.Mode = index == 0 ? "Queue" : "Steer"));
            pending.Children.Add(Button("Cancel sample input", () =>
            {
                session.PendingInputs.Remove(input);
                RenderComposer();
            }));
        }

        Expander pendingInputs = new() { Header = "Pending inputs (" + session.PendingInputs.Count + ")", Content = pending, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        TextBox draft = new() { Text = session.Draft, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 120, PlaceholderText = "Describe a sample task…", Header = "Draft" };
        _draft = draft;
        draft.TextChanged += (_, _) => session.Draft = draft.Text;
        Composer.Children.Add(draft);
        StackPanel tools = Stack();
        tools.Children.Add(Choice("Sample directory / worktree", ["main", "sample/recipe-tags-worktree"], session.Worktree == "main" ? 0 : 1,
            index =>
            {
                session.Worktree = index == 0 ? "main" : "sample/recipe-tags-worktree";
                UpdateSessionPath();
            }));
        tools.Children.Add(Choice("Sample model", ["Sample reasoning model", "Sample fast model"], session.Model == "Sample reasoning model" ? 0 : 1,
            index =>
            {
                session.Model = index == 0 ? "Sample reasoning model" : "Sample fast model";
                _details.Show(session);
            }));
        tools.Children.Add(Choice("Sample agent", ["Build (sample)", "Plan (sample)"], session.Agent == "Build (sample)" ? 0 : 1,
            index =>
            {
                session.Agent = index == 0 ? "Build (sample)" : "Plan (sample)";
                _details.Show(session);
            }));
        foreach (string attachment in session.Attachments.ToArray())
        {
            tools.Children.Add(Button("Remove reference: " + attachment, () =>
            {
                session.Attachments.Remove(attachment);
                RenderComposer();
            }));
        }

        tools.Children.Add(Choice("Add embedded reference", ["Choose sample…", .. SampleFixtures.Files.Keys], 0, index =>
        {
            if (index > 0)
            {
                string path = SampleFixtures.Files.Keys.ElementAt(index - 1);
                if (!session.Attachments.Contains(path))
                {
                    session.Attachments.Add(path);
                }

                RenderComposer();
            }
        }));
        tools.Children.Add(Choice("Discover command / skill", ["Choose sample…", "/review — sample command", "$explain — sample skill"], 0, index =>
        {
            if (index > 0)
            {
                session.Draft += index == 1 ? " /review " : " $explain ";
                draft.Text = session.Draft;
            }
        }));
        tools.Children.Add(Choice("Sample submission mode", ["Append locally", "Queue", "Steer"], session.SubmissionMode,
            index => session.SubmissionMode = index));
        TextBlock feedbackText = Text("");
        Button interrupt = Button("Interrupt sample", () =>
        {
            session.Execution = "Interrupted (sample)";
            Render();
        });
        interrupt.IsEnabled = session.Execution == "Running";
        ToolTipService.SetToolTip(interrupt, "Select the Running sample session to interrupt its local state.");
        Composer.Children.Add(Actions(Button("Submit sample", () =>
        {
            string mode = session.SubmissionMode == 0 ? "Append locally" : session.SubmissionMode == 1 ? "Queue" : "Steer";
            if (session.Submit(mode))
            {
                Render();
            }
            else
            {
                feedbackText.Text = "Write a draft or add a reference first.";
            }
        }), interrupt));
        Composer.Children.Add(feedbackText);
        Composer.Children.Add(pendingInputs);
        Composer.Children.Add(new Expander { Header = "References, model, agent & commands", Content = tools, HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }

    public async void ShowAttentionAsync()
    {
        StackPanel list = Stack();
        ContentDialog dialog = new() { Title = "Needs attention · sample requests", XamlRoot = XamlRoot, CloseButtonText = "Close" };
        foreach (SampleSession session in _state.Attention.ToArray())
        {
            list.Children.Add(Button(session.Title + (session.Archived ? " (archived)" : ""), () =>
            {
                dialog.Hide();
                _state.Selected = session;
                _expanded = false;
                UpdateDetailsLayout();
                if (_detailsInline != true)
                {
                    DetailsLayout.IsPaneOpen = false;
                }
                if (_projectsInline != true)
                {
                    ProjectsLayout.IsPaneOpen = false;
                }
                Render();
                _requestCard?.StartBringIntoView();
            }));
        }

        if (list.Children.Count == 0)
        {
            list.Children.Add(Text("No pending requests."));
        }

        dialog.Content = Scroll(list);
        await dialog.ShowAsync();
    }

    private void OpenReference(bool changes)
    {
        SampleSession session = _state.Selected!;
        session.DetailsTab = changes ? 2 : 1;
        if (changes)
        {
            session.Comparison = 0;
            session.SelectedChange = "src/RecipeList.xaml";
        }
        else
        {
            session.SelectedFile = "src/RecipeList.xaml";
        }

        _details.Show(session);
        DetailsLayout.IsPaneOpen = true;
    }

    private void NewTask_Click(object sender, RoutedEventArgs args)
    {
        _state.Create(_state.Selected is { } session ? _state.Owner(session)! : _state.Projects[0]);
        Render();
        _draft?.Focus(FocusState.Programmatic);
    }
    private void Search_Changed(object sender, TextChangedEventArgs args)
    {
        if (ProjectList is not null)
        {
            RenderProjects();
        }
    }
    private void ClearSearch_Click(object sender, RoutedEventArgs args) => SearchBox.Text = "";
    private void Filter_Changed(object sender, RoutedEventArgs args)
    {
        if (ProjectList is not null)
        {
            RenderProjects();
        }
    }
    private void Settings_Click(object sender, RoutedEventArgs args) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Projects_Click(object sender, RoutedEventArgs args) => ProjectsLayout.IsPaneOpen = !ProjectsLayout.IsPaneOpen;
    private void Details_Click(object sender, RoutedEventArgs args) => DetailsLayout.IsPaneOpen = !DetailsLayout.IsPaneOpen;
    private void CloseProjects_Click(object sender, RoutedEventArgs args)
    {
        ProjectsLayout.IsPaneOpen = false;
        ProjectsToggle.Focus(FocusState.Programmatic);
    }
    private void CloseDetails_Click(object sender, RoutedEventArgs args)
    {
        _expanded = false;
        UpdateDetailsLayout();
        DetailsLayout.IsPaneOpen = false;
        DetailsToggle.Focus(FocusState.Programmatic);
    }
    private void Expand_Click(object sender, RoutedEventArgs args)
    {
        _expanded = !_expanded;
        UpdateDetailsLayout();
    }
    private void Terminal_Click(object sender, RoutedEventArgs args)
    {
        TerminalRegion.Visibility = TerminalRegion.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (TerminalOutput.Text.Length == 0)
        {
            ResetTerminal_Click(sender, args);
        }
    }
    private void CloseTerminal_Click(object sender, RoutedEventArgs args)
    {
        TerminalRegion.Visibility = Visibility.Collapsed;
        TerminalToggle.Focus(FocusState.Programmatic);
    }
    private void ClearTerminal_Click(object sender, RoutedEventArgs args) => TerminalOutput.Text = "Sample output cleared. Reset output restores the recording.";
    private void ResetTerminal_Click(object sender, RoutedEventArgs args) => TerminalOutput.Text = SampleFixtures.Terminal(TerminalSelection.SelectedIndex);
    private void Terminal_Changed(object sender, SelectionChangedEventArgs args)
    {
        TerminalOutput?.Text = SampleFixtures.Terminal(TerminalSelection.SelectedIndex);
    }
    private void Conversation_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (!_rendering && _state.Selected is { } session)
        {
            session.ConversationOffset = ConversationScroll.VerticalOffset;
        }
    }

    private void ProjectsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool inline = args.NewSize.Width >= (double)Application.Current.Resources["ShellProjectsInlineThreshold"];
        if (_projectsInline != inline)
        {
            _projectsInline = inline;
            ProjectsLayout.DisplayMode = inline ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
            ProjectsLayout.IsPaneOpen = inline;
        }
    }

    private void DetailsLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        ComposerScroll.MaxHeight = Math.Min(Dimension("ShellComposerMaximumHeight"), args.NewSize.Height * 0.35);
        TerminalRegion.Height = Math.Min(Dimension("ShellTerminalHeight"), args.NewSize.Height * 0.25);
        bool inline = args.NewSize.Width >= (double)Application.Current.Resources["ShellInspectorInlineThreshold"];
        if (_detailsInline != inline)
        {
            _detailsInline = inline;
            DetailsLayout.IsPaneOpen = inline;
        }

        UpdateDetailsLayout();
    }

    private void UpdateDetailsLayout()
    {
        DetailsLayout.DisplayMode = _expanded || _detailsInline == true ? SplitViewDisplayMode.Inline : SplitViewDisplayMode.Overlay;
        double available = Math.Max(0, DetailsLayout.ActualWidth);
        DetailsLayout.OpenPaneLength = _expanded ? available : Math.Min(_detailsWidth, Math.Max(0, available - 48));
        DetailsRegion.Width = DetailsLayout.OpenPaneLength;
        CentralWorkspace.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandButton.Content = _expanded ? "Restore" : "Expand";
        DetailsGrip.IsEnabled = !_expanded && _detailsInline == true;
    }

    private void ResizeDetails(double delta)
    {
        double minimum = Dimension("ShellDetailsMinimumWidth");
        _detailsWidth = Math.Clamp(_detailsWidth - delta, minimum, Math.Max(minimum, DetailsLayout.ActualWidth - Dimension("ShellConversationMinimumWidth")));
        UpdateDetailsLayout();
    }

    private void DetailsGrip_DragDelta(object sender, DragDeltaEventArgs args) => ResizeDetails(args.HorizontalChange);
    private static double Dimension(string key) => (double)Application.Current.Resources[key];
    private void DetailsGrip_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            ResizeDetails(args.Key == VirtualKey.Left ? -24 : 24);
            args.Handled = true;
        }
    }
}
