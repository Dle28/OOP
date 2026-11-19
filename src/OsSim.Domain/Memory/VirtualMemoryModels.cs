using OsSim.Domain.Simulation;

namespace OsSim.Domain.Memory;

public enum PageReplacementAlgorithm
{
    Fifo,
    Lru,
    Optimal,
    Clock
}

public enum MemoryAccessOutcome
{
    TlbHit,
    PageTableHit,
    PageFault
}

public sealed record VirtualMemoryInput(
    IReadOnlyList<ulong> VirtualAddresses,
    int VirtualAddressBits,
    int PageSize,
    int FrameCount,
    PageReplacementAlgorithm ReplacementAlgorithm,
    int TlbCapacity = 0);

public sealed record PageFrameSnapshot(
    int Index,
    ulong? VirtualPageNumber,
    bool Referenced,
    long LoadedOrder,
    long LastAccessOrder)
{
    public long? LoadedOrderDisplay => VirtualPageNumber is null ? null : LoadedOrder;
}

public sealed record PageTableEntry(ulong VirtualPageNumber, int FrameIndex);

public sealed record TlbEntry(ulong VirtualPageNumber, int FrameIndex, long LastUsedOrder);

public sealed record MemoryMetrics(
    int AccessCount,
    int TlbHits,
    int TlbMisses,
    int PageTableHits,
    int PageFaults,
    int Replacements,
    int SwapIns = 0,
    int SwapOuts = 0);

public sealed record VirtualMemorySnapshot(
    int AccessIndex,
    ulong? VirtualAddress,
    ulong? VirtualPageNumber,
    ulong? Offset,
    int? FrameIndex,
    ulong? PhysicalAddress,
    MemoryAccessOutcome? Outcome,
    IReadOnlyList<PageFrameSnapshot> Frames,
    IReadOnlyList<PageTableEntry> PageTable,
    IReadOnlyList<TlbEntry> Tlb,
    IReadOnlyList<ulong> SwapPages,
    MemoryMetrics Metrics,
    IReadOnlyList<SimulationEvent> Events,
    bool IsCompleted);

public readonly record struct PageReplacementDecision(int VictimFrameIndex, IReadOnlyList<int> ClearedReferenceFrames, int NextClockHand);
