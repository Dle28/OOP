using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OsSim.Wpf.Animations;

public static class PulseAnimation
{
    public static readonly DependencyProperty PulseVersionProperty = DependencyProperty.RegisterAttached(
        "PulseVersion",
        typeof(long),
        typeof(PulseAnimation),
        new PropertyMetadata(0L, OnPulseVersionChanged));

    public static long GetPulseVersion(DependencyObject element) =>
        (long)element.GetValue(PulseVersionProperty);

    public static void SetPulseVersion(DependencyObject element, long value) =>
        element.SetValue(PulseVersionProperty, value);

    private static void OnPulseVersionChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element || !SystemParameters.ClientAreaAnimation ||
            element.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        // Transforms declared in a DataTemplate may be frozen and shared by WPF.
        // Clone them into an element-local mutable transform before starting animation.
        if (scale.IsFrozen)
        {
            scale = scale.CloneCurrentValue();
            element.RenderTransform = scale;
        }

        var animation = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        try
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone());
        }
        catch (InvalidOperationException)
        {
            // A presentation element can be sealed during template replacement.
            // Keep the simulation usable even if WPF refuses this cosmetic pulse.
        }
    }
}
