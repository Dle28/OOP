namespace OsSim.Wpf.ViewModels;

public sealed record WaitGraphEdgeVisual(
    string FromLabel,
    string ToLabel,
    string EdgeLabel,
    bool IsAllocation);
