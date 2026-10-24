using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace OpenCodeDesktop.Views;

/// <summary>Retains native pane transitions without sliding the workspace content.</summary>
public sealed class PaneOnlySplitView : SplitView
{
    public PaneOnlySplitView()
    {
        DefaultStyleKey = typeof(SplitView);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (VisualTreeHelper.GetChildrenCount(this) == 0
            || VisualTreeHelper.GetChild(this, 0) is not FrameworkElement root)
        {
            return;
        }

        // The bundled WinUI SplitView template translates ContentTransform during
        // inline open/close. Remove only those timelines from this template instance;
        // preserve pane movement, clipping, layout, and overlay light dismissal.
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
        }
    }
}
