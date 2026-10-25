using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Workspaces;
using OpenCodeDesktop.Presentation;

namespace OpenCodeDesktop.Views;

/// <summary>Builds native form controls once per pending request, preserving answers and keyboard focus during streaming.</summary>
internal sealed class TaskFormPresenter(ICollection<Button> actions, ICollection<Control> inputs)
{
    internal FrameworkElement Build(SessionTaskState state, TaskForm form, LiveTaskWorkflow workflow)
    {
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"));
        StackPanel header = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        header.Children.Add(ShellUi.Icon("ShellIconMessage", ShellUi.Dimension("ShellIconSmall"), ShellUi.Brush("ShellAccentBrush")));
        header.Children.Add(ShellUi.Text("Agent question", "ShellBodyStrongTextStyle"));
        header.Children.Add(ShellUi.Badge(form.State, ShellTone.Info));
        content.Children.Add(header);
        content.Children.Add(ShellUi.Text(form.Title, "ShellSecondaryTextStyle"));

        Dictionary<string, object> answers = state.Answers[form.Id];
        List<(TaskField Field, FrameworkElement Control)> conditional = [];
        void UpdateVisibility()
        {
            foreach ((TaskField field, FrameworkElement control) in conditional)
            {
                control.Visibility = !field.Hidden && TaskFormValidation.Visible(field, answers) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        foreach (TaskField field in form.Fields)
        {
            string label = field.Title ?? field.Key;
            if (field.Hidden && field.Required && !answers.ContainsKey(field.Key))
            {
                content.Children.Add(ShellUi.Text(label + ": a required hidden value is unavailable. Cancel this form or refresh.", "ShellTertiaryTextStyle"));
            }

            FrameworkElement control = CreateField(field, label, answers, UpdateVisibility);
            conditional.Add((field, control));
            content.Children.Add(control);
        }

        UpdateVisibility();
        content.Children.Add(ShellUi.Text("Answers must satisfy the declared options and constraints. Unsupported or missing hidden values can be cancelled.", "ShellTertiaryTextStyle"));
        StackPanel footer = ShellUi.Stack(ShellUi.Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        Button submit = ShellUi.LabelButton("Submit answer", null, async (_, _) => await workflow.AnswerAsync(state, form, false).ConfigureAwait(true), "ShellPrimaryButtonStyle");
        Button cancel = ShellUi.LabelButton("Cancel question", null, async (_, _) => await workflow.AnswerAsync(state, form, true).ConfigureAwait(true), "ShellQuietButtonStyle");
        actions.Add(submit);
        actions.Add(cancel);
        footer.Children.Add(submit);
        footer.Children.Add(cancel);
        content.Children.Add(footer);

        Border card = new()
        {
            Style = ShellUi.Resource<Style>("ShellCardStyle"),
            Background = ShellUi.Brush("ShellInfoSurfaceBrush"),
            BorderBrush = ShellUi.Brush("ShellAccentBrush"),
            Child = content,
        };
        AutomationProperties.SetName(card, "Agent question: " + form.Title);
        return card;
    }

    private FrameworkElement CreateField(TaskField field, string label, Dictionary<string, object> answers, Action changed)
    {
        string required = field.Required ? " (required)" : "";
        if (field.Type is "boolean" or "external")
        {
            CheckBox box = new()
            {
                Content = field.Type == "external" ? label + required + "\n" + field.Url + "\nI explicitly acknowledge this external request" : label + required,
                IsChecked = answers.GetValueOrDefault(field.Key) as bool?,
            };
            box.Click += (_, _) =>
            {
                answers[field.Key] = box.IsChecked == true;
                changed();
            };
            return Field(field, box);
        }

        if (field.Type == "multiselect" && !field.Custom)
        {
            return Field(field, CreateMultiselect(field, answers, changed));
        }

        if (field.Type == "string" && field.Options.Count > 0 && !field.Custom)
        {
            ComboBox options = new()
            {
                ItemsSource = field.Options,
                DisplayMemberPath = nameof(TaskOption.Label),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedItem = field.Options.FirstOrDefault(option => option.Value == answers.GetValueOrDefault(field.Key) as string),
            };
            options.SelectionChanged += (_, _) =>
            {
                if (options.SelectedItem is TaskOption option)
                {
                    answers[field.Key] = option.Value;
                }

                changed();
            };
            return Field(field, options);
        }

        return Field(field, CreateTextInput(field, answers, changed));
    }

    private FrameworkElement Field(TaskField field, FrameworkElement control)
    {
        StackPanel content = ShellUi.Stack(ShellUi.Dimension("ShellSpaceTiny"));
        if (control is Control interactive)
        {
            inputs.Add(interactive);
        }

        if (control is not CheckBox)
        {
            TextBlock label = ShellUi.Text((field.Title ?? field.Key) + (field.Required ? " (required)" : ""), "ShellBodyStrongTextStyle");
            content.Children.Add(label);
            AutomationProperties.SetName(control, label.Text);
        }

        if (field.Description is { Length: > 0 } description)
        {
            content.Children.Add(ShellUi.Text(description, "ShellTertiaryTextStyle"));
        }

        content.Children.Add(control);
        string hint = Hint(field);
        if (hint.Length > 0)
        {
            content.Children.Add(ShellUi.Text(hint, "ShellTertiaryTextStyle"));
        }

        AutomationProperties.SetHelpText(control, string.Join(" ", new[] { field.Description, hint }.Where(text => !string.IsNullOrEmpty(text))));

        return content;
    }

    private StackPanel CreateMultiselect(TaskField field, Dictionary<string, object> answers, Action changed)
    {
        StackPanel choices = ShellUi.Stack(ShellUi.Dimension("ShellSpaceTiny"));
        foreach (TaskOption option in field.Options)
        {
            CheckBox check = new()
            {
                Content = option.Label,
                Tag = option.Value,
                IsChecked = answers.GetValueOrDefault(field.Key) is string[] values && values.Contains(option.Value),
            };
            AutomationProperties.SetName(check, (field.Title ?? field.Key) + ": " + option.Label);
            check.Click += (_, _) =>
            {
                answers[field.Key] = choices.Children.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => (string)box.Tag).ToArray();
                changed();
            };
            choices.Children.Add(check);
            inputs.Add(check);
        }

        return choices;
    }

    private static TextBox CreateTextInput(TaskField field, Dictionary<string, object> answers, Action changed)
    {
        TextBox input = new()
        {
            PlaceholderText = field.Placeholder ?? "",
            Text = answers.GetValueOrDefault(field.Key) is string[] array ? string.Join('\n', array) : Convert.ToString(answers.GetValueOrDefault(field.Key), CultureInfo.InvariantCulture) ?? "",
            AcceptsReturn = field.Type == "multiselect",
            TextWrapping = field.Type == "multiselect" ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        input.TextChanged += (_, _) =>
        {
            if (field.Type is "number" or "integer")
            {
                if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    answers[field.Key] = number;
                }
                else
                {
                    answers[field.Key] = input.Text;
                }
            }
            else
            {
                answers[field.Key] = field.Type == "multiselect" ? input.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : input.Text;
            }

            changed();
        };
        return input;
    }

    private static string Hint(TaskField field) => field.Type switch
    {
        "number" or "integer" => "Number" + Range(field.Minimum, field.Maximum, "minimum", "maximum"),
        "string" => "Text" + Length(field.MinLength, field.MaxLength) + (field.Format is null ? "" : " · format: " + field.Format),
        "multiselect" => "One value per line" + Count(field.MinItems, field.MaxItems),
        "boolean" => "Check to accept.",
        "external" => "Acknowledging sends the external request through the server.",
        _ => "Unsupported control. Cancel this form or refresh.",
    };

    private static string Range(double? minimum, double? maximum, string minimumLabel, string maximumLabel) =>
        minimum is null && maximum is null ? "" : " · " + minimumLabel + " " + (minimum?.ToString(CultureInfo.InvariantCulture) ?? "—") +
        " · " + maximumLabel + " " + (maximum?.ToString(CultureInfo.InvariantCulture) ?? "—");

    private static string Length(int? minimum, int? maximum) =>
        minimum is null && maximum is null ? "" : " · length " + (minimum?.ToString(CultureInfo.InvariantCulture) ?? "—") + "–" +
        (maximum?.ToString(CultureInfo.InvariantCulture) ?? "—");

    private static string Count(int? minimum, int? maximum) =>
        minimum is null && maximum is null ? "" : " · minimum " + (minimum?.ToString(CultureInfo.InvariantCulture) ?? "—") +
        " · maximum " + (maximum?.ToString(CultureInfo.InvariantCulture) ?? "—");
}
