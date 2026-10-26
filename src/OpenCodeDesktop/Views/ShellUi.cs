using System;
using System.Collections.Generic;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using OpenCodeDesktop.Presentation;

using Windows.UI.ViewManagement;

namespace OpenCodeDesktop.Views;

/// <summary>Populates XAML shell fragments from the shared semantic resources.</summary>
internal static class ShellUi
{
    private static readonly AccessibilitySettings s_accessibility = new();

    internal static T Resource<T>(string key) => (T)Application.Current.Resources[key];
    internal static double Dimension(string key) => Resource<double>(key);
    internal static Thickness Padding(string key) => Resource<Thickness>(key);

    /// <summary>
    /// Resolves dynamic brushes through the active theme dictionary. Direct
    /// <see cref="ResourceDictionary"/> lookup returns the default dictionary,
    /// which would keep dark-theme success green in Windows high contrast.
    /// </summary>
    internal static Brush Brush(string key)
    {
        if (s_accessibility.HighContrast
            && Application.Current.Resources.ThemeDictionaries is IDictionary<object, object> themes
            && themes.TryGetValue("HighContrast", out object? value)
            && value is ResourceDictionary highContrast
            && highContrast.ContainsKey(key))
        {
            return (Brush)highContrast[key];
        }

        return Resource<Brush>(key);
    }
    internal static string Glyph(string key) => Resource<string>(key);

    internal static T Template<T>(string key) where T : FrameworkElement =>
        (T)Resource<DataTemplate>(key).LoadContent();

    internal static T Part<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        (T)root.FindName(name);

    internal static FontIcon Icon(string glyphKey, double? size = null, Brush? foreground = null)
    {
        FontIcon icon = Template<FontIcon>("ShellIconTemplate");
        icon.Glyph = Glyph(glyphKey);
        if (size is not null) { icon.FontSize = size.Value; }
        if (foreground is not null) { icon.Foreground = foreground; }
        return icon;
    }

    internal static TextBlock Text(string text, string styleKey = "ShellBodyTextStyle", TextWrapping? wrapping = null)
    {
        TextBlock block = Template<TextBlock>("ShellTextTemplate");
        block.Text = text;
        block.Style = Resource<Style>(styleKey);
        if (wrapping is not null) { block.TextWrapping = wrapping.Value; }
        return block;
    }

    internal static StackPanel Stack(double spacing = -1, Orientation orientation = Orientation.Vertical)
    {
        StackPanel panel = new() { Orientation = orientation };
        if (spacing >= 0)
        {
            panel.Spacing = spacing;
        }

        return panel;
    }

    internal static Button LabelButton(string label, string? glyphKey, RoutedEventHandler click, string styleKey = "ShellSecondaryButtonStyle", string? automationName = null)
    {
        Button button = Template<Button>("ShellLabelButtonTemplate");
        button.Style = Resource<Style>(styleKey);
        FontIcon icon = Part<FontIcon>(button, "Icon");
        icon.Visibility = glyphKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyphKey is not null) { icon.Glyph = Glyph(glyphKey); }
        Part<TextBlock>(button, "Label").Text = label;
        button.Click += click;
        AutomationProperties.SetName(button, automationName ?? label);
        return button;
    }

    internal static Button IconButton(string glyphKey, string automationName, RoutedEventHandler click, string styleKey = "ShellIconButtonStyle")
    {
        Button button = Template<Button>("ShellIconButtonTemplate");
        button.Style = Resource<Style>(styleKey);
        Part<FontIcon>(button, "Icon").Glyph = Glyph(glyphKey);
        button.Click += click;
        AutomationProperties.SetName(button, automationName);
        ToolTipService.SetToolTip(button, automationName);
        return button;
    }

    internal static Button NavigationRow(UIElement content, string automationName, RoutedEventHandler click, bool selected = false)
    {
        Button button = new()
        {
            Content = content,
            Style = Resource<Style>(selected ? "ShellSelectedNavigationRowStyle" : "ShellNavigationRowStyle"),
        };
        button.Click += click;
        AutomationProperties.SetName(button, automationName);
        AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        return button;
    }

    internal static Border StatusIndicator(Brush background) => new()
    {
        Width = Dimension("ShellStatusIndicatorSize"),
        Height = Dimension("ShellStatusIndicatorSize"),
        CornerRadius = Resource<CornerRadius>("ShellStatusIndicatorCornerRadius"),
        Background = background,
        VerticalAlignment = VerticalAlignment.Center,
    };

    internal static Border Badge(string label, ShellTone tone, string? glyphKey = null)
    {
        Border badge = Template<Border>("ShellBadgeTemplate");
        badge.Background = ToneSurface(tone);
        badge.BorderBrush = tone == ShellTone.Neutral ? Brush("ShellBorderBrush") : ToneBrush(tone);
        FontIcon icon = Part<FontIcon>(badge, "Icon");
        icon.Visibility = glyphKey is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyphKey is not null) { icon.Glyph = Glyph(glyphKey); }
        icon.Foreground = ToneBrush(tone);
        TextBlock text = Part<TextBlock>(badge, "Label");
        text.Text = label;
        text.Foreground = ToneBrush(tone);
        return badge;
    }

    internal static Border Card(UIElement child, string styleKey = "ShellCardStyle") => new()
    {
        Style = Resource<Style>(styleKey),
        Child = child,
    };

    internal static Border Divider(bool vertical = false) => new()
    {
        Style = Resource<Style>(vertical ? "ShellVerticalDividerStyle" : "ShellDividerStyle"),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    internal static Border StatePanel(string glyphKey, string title, string message, ShellTone tone, params UIElement[] actions) =>
        StatePanel(glyphKey, title, message, tone, false, actions);

    internal static Border StatePanel(string glyphKey, string title, string message, ShellTone tone, bool compact, params UIElement[] actions)
    {
        Border panel = Template<Border>("ShellStatePanelTemplate");
        panel.Padding = Resource<Thickness>(compact ? "ShellCardPadding" : "ShellCardSpaciousPadding");
        panel.Margin = new Thickness(0, compact ? 8 : 24, 0, 0);
        Part<StackPanel>(panel, "Body").Spacing = Dimension(compact ? "ShellSpaceSmall" : "ShellSpaceCompact");
        Border iconSurface = Part<Border>(panel, "IconSurface");
        iconSurface.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        iconSurface.Background = ToneSurface(tone);
        iconSurface.BorderBrush = tone == ShellTone.Neutral ? Brush("ShellBorderBrush") : ToneBrush(tone);
        FontIcon icon = Part<FontIcon>(panel, "Icon");
        icon.Glyph = Glyph(glyphKey);
        icon.Foreground = ToneBrush(tone);
        TextBlock heading = Part<TextBlock>(panel, "Heading");
        heading.Text = title;
        heading.Style = Resource<Style>(compact ? "ShellSubheadingTextStyle" : "ShellHeadingTextStyle");
        TextBlock description = Part<TextBlock>(panel, "Description");
        description.Text = message;
        description.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StackPanel row = Part<StackPanel>(panel, "Actions");
        row.Visibility = actions.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement action in actions) { row.Children.Add(action); }
        return panel;
    }

    internal static void Heading(TextBlock block, AutomationHeadingLevel level = AutomationHeadingLevel.Level2) =>
        AutomationProperties.SetHeadingLevel(block, level);

    internal static void Live(TextBlock block, bool assertive = false) =>
        AutomationProperties.SetLiveSetting(block, assertive ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

    internal static void Announce(FrameworkElement element) =>
        FrameworkElementAutomationPeer.FromElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

    internal static void Tooltip(FrameworkElement element, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            ToolTipService.SetToolTip(element, text);
        }
    }

    internal static Brush ToneBrush(ShellTone tone) => tone switch
    {
        ShellTone.Success => Brush("ShellConnectedBrush"),
        ShellTone.Warning => Brush("ShellWarningBrush"),
        ShellTone.Danger => Brush("ShellDangerBrush"),
        ShellTone.Info or ShellTone.Accent => Brush("ShellAccentBrush"),
        _ => Brush("ShellTextSecondaryBrush"),
    };

    internal static Brush ToneSurface(ShellTone tone) => tone switch
    {
        ShellTone.Success => Brush("ShellConnectedSurfaceBrush"),
        ShellTone.Warning => Brush("ShellWarningSurfaceBrush"),
        ShellTone.Danger => Brush("ShellDangerSurfaceBrush"),
        ShellTone.Info or ShellTone.Accent => Brush("ShellAccentSurfaceBrush"),
        _ => Brush("ShellSubtleSurfaceBrush"),
    };
}
