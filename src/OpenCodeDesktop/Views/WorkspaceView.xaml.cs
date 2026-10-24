using System;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using OpenCodeDesktop.Presentation;

using Windows.System;

namespace OpenCodeDesktop.Views;

public sealed partial class WorkspaceView : UserControl
{
    private readonly SampleWorkspace _state = new();
    private bool? _projectsInline;
    private bool? _detailsInline;
    private bool _expanded;
    private bool _rendering;
    private double _detailsWidth = Dimension("ShellDetailsWidth");
    private TextBox? _draft;
    private ListViewItem? _selectedSessionItem;
    private FrameworkElement? _requestCard;

    public event EventHandler? SettingsRequested;
    internal Button ConnectionSelector => ServerSelector;
    public event EventHandler? AttentionChanged;
    public int AttentionCount => _state.AttentionCount;

    public WorkspaceView()
    {
        InitializeComponent();
        Render();
    }

    private T LoadTemplate<T>(string key) where T : FrameworkElement => (T)((DataTemplate)Resources[key]).LoadContent();
    private static T Part<T>(FrameworkElement root, string name) where T : FrameworkElement => (T)root.FindName(name);

    private TextBlock BodyText(string text)
    {
        TextBlock block = LoadTemplate<TextBlock>("BodyTextTemplate");
        block.Text = text;
        return block;
    }

    private Border Message(string text)
    {
        Border card = LoadTemplate<Border>("MessageTemplate");
        Part<TextBlock>(card, "Message").Text = text;
        return card;
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
        Details.Show(session);
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
        _selectedSessionItem = null;
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
            Expander group = LoadTemplate<Expander>("ProjectGroupTemplate");
            group.Header = project.Name;
            group.IsExpanded = project.Expanded;
            group.Expanding += (_, _) => project.Expanded = true;
            group.Collapsed += (_, _) => project.Expanded = false;
            ListView list = Part<ListView>(group, "Sessions");
            AutomationProperties.SetName(list, project.Name + " sessions");
            foreach (SampleSession session in sessions)
            {
                ListViewItem item = LoadTemplate<ListViewItem>("SessionRowTemplate");
                Part<TextBlock>(item, "Label").Text = session.Title + "\n" + (session.Archived ? "Archived · " : "") + session.Execution;
                item.Tag = session;
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
            SampleSession worktreeSession = sessions.Contains(_state.Selected) ? _state.Selected! : sessions[0];
            ComboBox worktree = Part<ComboBox>(group, "Worktree");
            worktree.SelectedIndex = worktreeSession.Worktree == "main" ? 0 : 1;
            worktree.SelectionChanged += (_, _) =>
            {
                worktreeSession.Worktree = worktree.SelectedIndex == 0 ? "main" : "sample/recipe-tags-worktree";
                _state.Selected = worktreeSession;
                Render();
            };
            Part<Button>(group, "NewTask").Click += (_, _) =>
            {
                _state.Create(project);
                Render();
            };
            ProjectList.Children.Add(group);
        }

        if (count == 0)
        {
            ProjectList.Children.Add(BodyText("No matching sessions."));
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
        ContentDialog dialog = LoadTemplate<ContentDialog>("SessionDialogTemplate");
        dialog.XamlRoot = XamlRoot;
        dialog.Title = delete ? "Delete this in-memory session?" : "Rename sample session";
        dialog.PrimaryButtonText = delete ? "Delete sample" : "Save sample title";
        TextBox title = Part<TextBox>(dialog, "TitleEditor");
        title.Text = session.Title;
        title.Visibility = delete ? Visibility.Collapsed : Visibility.Visible;
        Part<TextBlock>(dialog, "DeleteInformation").Visibility = delete ? Visibility.Visible : Visibility.Collapsed;
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
            Conversation.Children.Add(Message("No session selected. Choose a session or create a new task."));
            return;
        }

        if (session.IsNew)
        {
            Conversation.Children.Add(BodyText("New sample draft. Submitting appends a local message; no agent response will be generated."));
        }
        else
        {
            StackPanel recorded = LoadTemplate<StackPanel>("RecordedConversationTemplate");
            Part<Button>(recorded, "OpenFile").Click += (_, _) => OpenReference(false);
            Part<Button>(recorded, "ReviewChanges").Click += (_, _) => OpenReference(true);
            Conversation.Children.Add(recorded);
            if (session.PermissionPending || session.PermissionResult.Length > 0)
            {
                Border card = LoadTemplate<Border>("PermissionTemplate");
                Part<StackPanel>(card, "Actions").Visibility = session.PermissionPending ? Visibility.Visible : Visibility.Collapsed;
                TextBlock result = Part<TextBlock>(card, "Result");
                result.Text = session.PermissionResult;
                result.Visibility = session.PermissionPending ? Visibility.Collapsed : Visibility.Visible;
                Part<Button>(card, "Reject").Click += (_, _) => RespondPermission("Rejected");
                Part<Button>(card, "Allow").Click += (_, _) => RespondPermission("Allow once");
                if (session.PermissionPending)
                {
                    _requestCard = card;
                }

                Conversation.Children.Add(card);
            }

            if (session.QuestionPending || session.QuestionResult.Length > 0)
            {
                Border card = LoadTemplate<Border>("QuestionTemplate");
                Part<StackPanel>(card, "Pending").Visibility = session.QuestionPending ? Visibility.Visible : Visibility.Collapsed;
                TextBlock result = Part<TextBlock>(card, "Result");
                result.Text = session.QuestionResult;
                result.Visibility = session.QuestionPending ? Visibility.Collapsed : Visibility.Visible;
                TextBox answer = Part<TextBox>(card, "Answer");
                answer.Text = session.Answer;
                answer.TextChanged += (_, _) => session.Answer = answer.Text;
                Part<Button>(card, "Submit").Click += (_, _) =>
                {
                    if (session.RespondQuestion(false))
                    {
                        Render();
                    }
                    else
                    {
                        Part<TextBlock>(card, "Feedback").Text = "Enter an answer before submitting.";
                        answer.Focus(FocusState.Programmatic);
                    }
                };
                Part<Button>(card, "Cancel").Click += (_, _) =>
                {
                    session.RespondQuestion(true);
                    Render();
                };
                if (session.QuestionPending)
                {
                    _requestCard = card;
                }

                Conversation.Children.Add(card);
            }
        }

        foreach (string message in session.Messages)
        {
            Conversation.Children.Add(Message(message));
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
        _draft = null;
        if (_state.Selected is not { } session)
        {
            Composer.Children.Add(BodyText("Select a session or create a new task to write a message."));
            return;
        }

        StackPanel composer = LoadTemplate<StackPanel>("ComposerTemplate");
        StackPanel pending = Part<StackPanel>(composer, "PendingList");
        foreach (SampleInput input in session.PendingInputs.ToArray())
        {
            StackPanel row = LoadTemplate<StackPanel>("PendingInputTemplate");
            Part<TextBlock>(row, "Input").Text = input.Text;
            ComboBox delivery = Part<ComboBox>(row, "Delivery");
            delivery.SelectedIndex = input.Mode == "Queue" ? 0 : 1;
            delivery.SelectionChanged += (_, _) => input.Mode = delivery.SelectedIndex == 0 ? "Queue" : "Steer";
            Part<Button>(row, "Cancel").Click += (_, _) =>
            {
                session.PendingInputs.Remove(input);
                RenderComposer();
            };
            pending.Children.Add(row);
        }

        Part<Expander>(composer, "PendingInputs").Header = "Pending inputs (" + session.PendingInputs.Count + ")";
        TextBox draft = Part<TextBox>(composer, "Draft");
        _draft = draft;
        draft.Text = session.Draft;
        draft.TextChanged += (_, _) => session.Draft = draft.Text;
        ComboBox worktree = Part<ComboBox>(composer, "Worktree");
        worktree.SelectedIndex = session.Worktree == "main" ? 0 : 1;
        worktree.SelectionChanged += (_, _) =>
        {
            session.Worktree = worktree.SelectedIndex == 0 ? "main" : "sample/recipe-tags-worktree";
            UpdateSessionPath();
        };
        ComboBox model = Part<ComboBox>(composer, "Model");
        model.SelectedIndex = session.Model == "Sample reasoning model" ? 0 : 1;
        model.SelectionChanged += (_, _) =>
        {
            session.Model = model.SelectedIndex == 0 ? "Sample reasoning model" : "Sample fast model";
            Details.Show(session);
        };
        ComboBox agent = Part<ComboBox>(composer, "Agent");
        agent.SelectedIndex = session.Agent == "Build (sample)" ? 0 : 1;
        agent.SelectionChanged += (_, _) =>
        {
            session.Agent = agent.SelectedIndex == 0 ? "Build (sample)" : "Plan (sample)";
            Details.Show(session);
        };
        StackPanel attachments = Part<StackPanel>(composer, "Attachments");
        foreach (string attachment in session.Attachments.ToArray())
        {
            Button remove = LoadTemplate<Button>("ActionTemplate");
            remove.Content = "Remove reference: " + attachment;
            AutomationProperties.SetName(remove, (string)remove.Content);
            remove.Click += (_, _) =>
            {
                session.Attachments.Remove(attachment);
                RenderComposer();
            };
            attachments.Children.Add(remove);
        }

        ComboBox reference = Part<ComboBox>(composer, "Reference");
        string[] referencePaths = ["Choose sample…", .. SampleFixtures.Files.Keys];
        reference.ItemsSource = referencePaths;
        reference.SelectedIndex = 0;
        reference.SelectionChanged += (_, _) =>
        {
            if (reference.SelectedIndex > 0)
            {
                string path = SampleFixtures.Files.Keys.ElementAt(reference.SelectedIndex - 1);
                if (!session.Attachments.Contains(path))
                {
                    session.Attachments.Add(path);
                }

                RenderComposer();
            }
        };
        ComboBox command = Part<ComboBox>(composer, "Command");
        command.SelectedIndex = 0;
        command.SelectionChanged += (_, _) =>
        {
            if (command.SelectedIndex > 0)
            {
                session.Draft += command.SelectedIndex == 1 ? " /review " : " $explain ";
                draft.Text = session.Draft;
            }
        };
        ComboBox mode = Part<ComboBox>(composer, "SubmissionMode");
        mode.SelectedIndex = session.SubmissionMode;
        mode.SelectionChanged += (_, _) => session.SubmissionMode = mode.SelectedIndex;
        Button interrupt = Part<Button>(composer, "Interrupt");
        interrupt.IsEnabled = session.Execution == "Running";
        interrupt.Click += (_, _) =>
        {
            session.Execution = "Interrupted (sample)";
            Render();
        };
        Part<Button>(composer, "Submit").Click += (_, _) =>
        {
            string delivery = session.SubmissionMode == 0 ? "Append locally" : session.SubmissionMode == 1 ? "Queue" : "Steer";
            if (session.Submit(delivery))
            {
                Render();
            }
            else
            {
                Part<TextBlock>(composer, "Feedback").Text = "Write a draft or add a reference first.";
            }
        };
        Composer.Children.Add(composer);
    }

    public async void ShowAttentionAsync()
    {
        ContentDialog dialog = LoadTemplate<ContentDialog>("AttentionDialogTemplate");
        dialog.XamlRoot = XamlRoot;
        StackPanel list = Part<StackPanel>(dialog, "Requests");
        foreach (SampleSession session in _state.Attention.ToArray())
        {
            Button select = LoadTemplate<Button>("ActionTemplate");
            select.Content = session.Title + (session.Archived ? " (archived)" : "");
            AutomationProperties.SetName(select, (string)select.Content);
            select.Click += (_, _) =>
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
            };
            list.Children.Add(select);
        }

        if (list.Children.Count == 0)
        {
            list.Children.Add(BodyText("No pending requests."));
        }

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

        Details.Show(session);
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
