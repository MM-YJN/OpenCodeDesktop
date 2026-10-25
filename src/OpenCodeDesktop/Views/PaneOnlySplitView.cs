using System.Collections.Generic;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.UI.ViewManagement;

namespace OpenCodeDesktop.Views;

/// <summary>Retains native pane transitions without sliding the workspace content.</summary>
public sealed class PaneOnlySplitView : SplitView
{
    private readonly UISettings _settings = new();
    private readonly Dictionary<VisualStateGroup, List<VisualTransition>> _paneTransitions = [];
    private bool? _animationsEnabled;

    public PaneOnlySplitView()
    {
        DefaultStyleKey = typeof(SplitView);
        // Read the preference before every interaction, including on older Windows
        // versions that do not expose UISettings.AnimationsEnabledChanged.
        PaneOpening += (_, _) => UpdateMotionPreference();
        PaneClosing += (_, _) => UpdateMotionPreference();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _paneTransitions.Clear();
        _animationsEnabled = null;

        if (VisualTreeHelper.GetChildrenCount(this) == 0
            || VisualTreeHelper.GetChild(this, 0) is not FrameworkElement root)
        {
            return;
        }

        // The bundled WinUI SplitView template translates ContentTransform during
        // inline open/close. Remove only those timelines from this template instance;
        // preserve pane movement, clipping, layout, and overlay light dismissal.
        // When Windows animations are disabled, remove all transitions so reduced
        // motion is respected without changing layout or accessibility behavior.
        foreach (VisualStateGroup group in VisualStateManager.GetVisualStateGroups(root))
        {
            foreach (VisualTransition transition in group.Transitions)
            {
                if (transition.Storyboard is not { } storyboard)
                {
                    continue;
                }

                for (int index = storyboard.Children.Count - 1; index >= 0; index--)
                {
                    if (Storyboard.GetTargetName(storyboard.Children[index]) == "ContentTransform")
                    {
                        storyboard.Children.RemoveAt(index);
                    }
                }
            }

            _paneTransitions[group] = new List<VisualTransition>(group.Transitions);
        }

        UpdateMotionPreference();
    }

    private void UpdateMotionPreference()
    {
        bool animations = _settings.AnimationsEnabled;
        if (_animationsEnabled == animations)
        {
            return;
        }

        _animationsEnabled = animations;
        foreach ((VisualStateGroup group, List<VisualTransition> transitions) in _paneTransitions)
        {
            group.Transitions.Clear();
            if (animations)
            {
                foreach (VisualTransition transition in transitions)
                {
                    group.Transitions.Add(transition);
                }
            }
        }
    }
}
