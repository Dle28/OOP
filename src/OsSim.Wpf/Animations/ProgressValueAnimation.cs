using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace OsSim.Wpf.Animations;

public static class ProgressValueAnimation
{
    public static readonly DependencyProperty TargetValueProperty = DependencyProperty.RegisterAttached(
        "TargetValue",
        typeof(double),
        typeof(ProgressValueAnimation),
        new PropertyMetadata(0d, OnTargetValueChanged));

    public static double GetTargetValue(DependencyObject element) =>
        (double)element.GetValue(TargetValueProperty);

    public static void SetTargetValue(DependencyObject element, double value) =>
        element.SetValue(TargetValueProperty, value);

    private static void OnTargetValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ProgressBar progressBar)
        {
            return;
        }

        var targetValue = Math.Clamp((double)args.NewValue, progressBar.Minimum, progressBar.Maximum);
        var startingValue = progressBar.Value;
        progressBar.BeginAnimation(RangeBase.ValueProperty, null);
        progressBar.Value = targetValue;

        if (!SystemParameters.ClientAreaAnimation || !progressBar.IsLoaded || startingValue == targetValue)
        {
            return;
        }

        var animation = new DoubleAnimation(startingValue, targetValue, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        try
        {
            progressBar.BeginAnimation(RangeBase.ValueProperty, animation);
        }
        catch (InvalidOperationException)
        {
            progressBar.Value = targetValue;
        }
    }
}
