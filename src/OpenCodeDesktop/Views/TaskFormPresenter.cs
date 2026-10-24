using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Views;

/// <summary>Builds native form controls once per pending request, preserving answers and keyboard focus during streaming.</summary>
internal sealed class TaskFormPresenter(Func<string, TextBlock> body, Func<string, Func<Task>, Button> action, ICollection<Control> inputs)
{
    internal StackPanel Build(SessionTaskState state, TaskForm form, LiveTaskWorkflow workflow)
    {
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(body(form.Title));
        Dictionary<string, object> answers = state.Answers[form.Id];
        List<(TaskField Field, FrameworkElement Control)> conditional = [];
        void UpdateVisibility()
        {
            foreach ((TaskField Field, FrameworkElement Control) item in conditional)
            {
                item.Control.Visibility = !item.Field.Hidden && TaskFormValidation.Visible(item.Field, answers) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        foreach (TaskField field in form.Fields)
        {
            string label = (field.Title ?? field.Key) + (field.Required ? " (required)" : "") + (field.Description is { } description ? " — " + description : "");
            if (field.Hidden && field.Required && !answers.ContainsKey(field.Key))
            {
                panel.Children.Add(body(label + ": a required hidden value is unavailable. Cancel this form or refresh."));
            }
            FrameworkElement control = CreateField(field, label, answers, UpdateVisibility);
            if (control is Control interactive)
            {
                inputs.Add(interactive);
            }

            conditional.Add((field, control));
            panel.Children.Add(control);
        }
        UpdateVisibility();
        panel.Children.Add(body("Answers must satisfy the declared options and constraints. Unsupported or missing hidden values can be cancelled."));
        panel.Children.Add(action("Submit answer", () => workflow.AnswerAsync(state, form, false)));
        panel.Children.Add(action("Cancel question", () => workflow.AnswerAsync(state, form, true)));
        return panel;
    }

    private FrameworkElement CreateField(TaskField field, string label, Dictionary<string, object> answers, Action changed)
    {
        if (field.Type is "boolean" or "external")
        {
            CheckBox box = new()
            {
                Content = field.Type == "external" ? label + "\n" + field.Url + "\nI explicitly acknowledge this external request" : label,
                IsChecked = answers.GetValueOrDefault(field.Key) as bool?,
            };
            box.Click += (_, _) =>
            {
                answers[field.Key] = box.IsChecked == true;
                changed();
            };
            return box;
        }
        if (field.Type == "multiselect" && !field.Custom)
        {
            return CreateMultiselect(field, label, answers, changed);
        }

        if (field.Type == "string" && field.Options.Count > 0 && !field.Custom)
        {
            ComboBox options = new()
            {
                Header = label,
                ItemsSource = field.Options,
                DisplayMemberPath = nameof(TaskOption.Label),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedItem = field.Options.FirstOrDefault(o => o.Value == answers.GetValueOrDefault(field.Key) as string),
            };
            options.SelectionChanged += (_, _) =>
            {
                if (options.SelectedItem is TaskOption option)
                {
                    answers[field.Key] = option.Value;
                }

                changed();
            };
            return options;
        }
        return CreateTextInput(field, label, answers, changed);
    }

    private StackPanel CreateMultiselect(TaskField field, string label, Dictionary<string, object> answers, Action changed)
    {
        StackPanel choices = new() { Spacing = 4 };
        choices.Children.Add(body(label + " · minimum selections " + field.MinItems + " · maximum selections " + field.MaxItems));
        foreach (TaskOption option in field.Options)
        {
            CheckBox check = new() { Content = option.Label, Tag = option.Value, IsChecked = answers.GetValueOrDefault(field.Key) is string[] values && values.Contains(option.Value) };
            check.Click += (_, _) =>
            {
                answers[field.Key] = choices.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();
                changed();
            };
            choices.Children.Add(check);
            inputs.Add(check);
        }
        return choices;
    }

    private static TextBox CreateTextInput(TaskField field, string label, Dictionary<string, object> answers, Action changed)
    {
        TextBox input = new()
        {
            Header = label + Hint(field),
            PlaceholderText = field.Placeholder ?? "",
            Text = answers.GetValueOrDefault(field.Key) is string[] array ? string.Join('\n', array) : Convert.ToString(answers.GetValueOrDefault(field.Key), CultureInfo.InvariantCulture) ?? "",
            AcceptsReturn = field.Type == "multiselect",
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
    private static string Hint(TaskField field)
    {
        string hint = field.Type switch
        {
            "number" or "integer" => " · " + field.Type + " · minimum " + field.Minimum + " · maximum " + field.Maximum,
            "string" => " · length " + field.MinLength + "–" + field.MaxLength + (field.Format is null ? "" : " · format: " + field.Format) + (field.Pattern is null ? "" : " · pattern: " + field.Pattern),
            "multiselect" => " · one value per line · minimum selections " + field.MinItems + " · maximum selections " + field.MaxItems,
            _ => " · unsupported control; cancel or refresh",
        };
        if (field.Options.Count > 0)
        {
            hint += " · suggested values: " + string.Join(", ", field.Options.Select(o => o.Value));
        }

        return hint;
    }
}
