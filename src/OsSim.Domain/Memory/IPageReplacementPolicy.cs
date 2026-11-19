namespace OsSim.Domain.Memory;

public interface IPageReplacementPolicy
{
    PageReplacementAlgorithm Algorithm { get; }

    PageReplacementDecision SelectVictim(
        IReadOnlyList<PageFrameSnapshot> occupiedFrames,
        IReadOnlyList<ulong> futurePageReferences,
        int clockHand);
}
