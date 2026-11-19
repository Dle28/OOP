namespace OsSim.Wpf.ViewModels;

public sealed record MemoryFrameVisual(
    int Index,
    string PageLabel,
    string StatusLabel,
    bool IsVacant,
    bool IsCurrent,
    bool WasReplaced,
    long PulseVersion);
