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

/// <summary>Builds small shell fragments from the shared semantic resources.</summary>
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

    internal static FontIcon Icon(string glyphKey, double? size = null, Brush? foreground = null)
    {
        FontIcon icon = new()
        {
            Glyph = Glyph(glyphKey),
            FontFamily = Resource<FontFamily>("ShellIconFontFamily"),
            FontSize = size ?? Dimension("ShellIconMedium"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        if (foreground is not null)
        {
            icon.Foreground = foreground;
        }

        return icon;
    }

    internal static TextBlock Text(string text, string styleKey = "ShellBodyTextStyle", TextWrapping? wrapping = null)
    {
        TextBlock block = new()
        {
            Text = text,
            Style = Resource<Style>(styleKey),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (wrapping is not null)
        {
            block.TextWrapping = wrapping.Value;
        }

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
        StackPanel content = Stack(Dimension("ShellSpaceSmall"), Orientation.Horizontal);
        content.VerticalAlignment = VerticalAlignment.Center;
        if (glyphKey is not null)
        {
            content.Children.Add(Icon(glyphKey));
        }

        content.Children.Add(Text(label, "ShellBodyTextStyle", TextWrapping.NoWrap));
        Button button = new()
        {
            Content = content,
            Style = Resource<Style>(styleKey),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Click += click;
        AutomationProperties.SetName(button, automationName ?? label);
        return button;
    }

    internal static Button IconButton(string glyphKey, string automationName, RoutedEventHandler click, string styleKey = "ShellIconButtonStyle")
    {
        Button button = new()
        {
            Content = Icon(glyphKey),
            Style = Resource<Style>(styleKey),
        };
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
        StackPanel content = Stack(Dimension("ShellSpaceTiny"), Orientation.Horizontal);
        if (glyphKey is not null)
        {
            content.Children.Add(Icon(glyphKey, Dimension("ShellIconSmall"), ToneBrush(tone)));
        }

        TextBlock text = Text(label, "ShellCaptionTextStyle", TextWrapping.NoWrap);
        text.Foreground = ToneBrush(tone);
        text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        content.Children.Add(text);
        return new Border
        {
            Style = Resource<Style>("ShellBadgeStyle"),
            Background = ToneSurface(tone),
            BorderBrush = tone == ShellTone.Neutral ? Brush("ShellBorderBrush") : ToneBrush(tone),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
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
        StackPanel body = Stack(compact ? Dimension("ShellSpaceSmall") : Dimension("ShellSpaceCompact"));
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.MaxWidth = 440;
        if (!compact)
        {
            Border iconSurface = new()
            {
                Width = 40,
                Height = 40,
                CornerRadius = Resource<CornerRadius>("ShellCornerRadiusLarge"),
                Background = ToneSurface(tone),
                BorderBrush = tone == ShellTone.Neutral ? Brush("ShellBorderBrush") : ToneBrush(tone),
                BorderThickness = Resource<Thickness>("ShellHairlineThickness"),
                Child = Icon(glyphKey, Dimension("ShellIconLarge"), ToneBrush(tone)),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            body.Children.Add(iconSurface);
        }

        TextBlock heading = Text(title, compact ? "ShellSubheadingTextStyle" : "ShellHeadingTextStyle");
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.TextAlignment = TextAlignment.Center;
        body.Children.Add(heading);
        if (!compact)
        {
            TextBlock description = Text(message, "ShellSecondaryTextStyle");
            description.HorizontalAlignment = HorizontalAlignment.Center;
            description.TextAlignment = TextAlignment.Center;
            body.Children.Add(description);
        }

        if (actions.Length > 0)
        {
            StackPanel row = Stack(Dimension("ShellSpaceSmall"), Orientation.Horizontal);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (UIElement action in actions)
            {
                row.Children.Add(action);
            }

            body.Children.Add(row);
        }

        return new Border
        {
            Padding = compact ? Resource<Thickness>("ShellCardPadding") : Resource<Thickness>("ShellCardSpaciousPadding"),
            Margin = new Thickness(0, compact ? 8 : 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = body,
        };
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
