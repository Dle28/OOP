namespace OsSim.Wpf.ViewModels;

public sealed record MemoryReferenceVisual(
    int Position,
    ulong Address,
    ulong VirtualPageNumber,
    string ResultLabel,
    bool IsCurrent,
    bool IsHit,
    bool IsFault,
    long PulseVersion);
