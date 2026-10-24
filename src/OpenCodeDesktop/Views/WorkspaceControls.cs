using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenCodeDesktop.Views;

internal static class WorkspaceControls
{
    public static StackPanel Stack() => new() { Spacing = (double)Application.Current.Resources["ShellSpaceSmall"] };

    public static TextBlock Text(string text, bool heading = false) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources[heading ? "ShellHeadingTextStyle" : "ShellBodyTextStyle"],
        IsTextSelectionEnabled = true,
    };

    public static TextBlock Code(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Consolas"),
        TextWrapping = TextWrapping.NoWrap,
        IsTextSelectionEnabled = true,
    };

    public static Button Button(string label, Action action)
    {
        Button button = new() { Content = label };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    public static ComboBox Choice(string name, string[] items, int selected, Action<int> changed)
    {
        ComboBox box = new() { Header = name, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(box, name);
        foreach (string item in items)
        {
            box.Items.Add(item);
        }

        box.SelectedIndex = selected;
        box.SelectionChanged += (_, _) => changed(box.SelectedIndex);
        return box;
    }

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Auto,
    };

    public static Border Card(UIElement content) => new()
    {
        Style = (Style)Application.Current.Resources["ShellCardStyle"],
        Child = content,
    };

    public static StackPanel Actions(params UIElement[] controls)
    {
        StackPanel panel = Stack();
        panel.Orientation = Orientation.Horizontal;
        foreach (UIElement control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }
}
