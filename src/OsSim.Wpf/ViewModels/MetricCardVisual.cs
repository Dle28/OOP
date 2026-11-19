namespace OsSim.Wpf.ViewModels;

public enum MetricTone
{
    Primary,
    Success,
    Warning,
    Danger
}

public sealed record MetricCardVisual(string Label, string Value, string Detail, MetricTone Tone);
