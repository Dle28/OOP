namespace OsSim.Domain.Memory;

public sealed class FifoPageReplacementPolicy : IPageReplacementPolicy
{
    public PageReplacementAlgorithm Algorithm => PageReplacementAlgorithm.Fifo;

    public PageReplacementDecision SelectVictim(
        IReadOnlyList<PageFrameSnapshot> occupiedFrames,
        IReadOnlyList<ulong> futurePageReferences,
        int clockHand) =>
        new(occupiedFrames.MinBy(frame => frame.LoadedOrder)!.Index, Array.Empty<int>(), clockHand);
}

public sealed class LruPageReplacementPolicy : IPageReplacementPolicy
{
    public PageReplacementAlgorithm Algorithm => PageReplacementAlgorithm.Lru;

    public PageReplacementDecision SelectVictim(
        IReadOnlyList<PageFrameSnapshot> occupiedFrames,
        IReadOnlyList<ulong> futurePageReferences,
        int clockHand) =>
        new(occupiedFrames.MinBy(frame => frame.LastAccessOrder)!.Index, Array.Empty<int>(), clockHand);
}

public sealed class OptimalPageReplacementPolicy : IPageReplacementPolicy
{
    public PageReplacementAlgorithm Algorithm => PageReplacementAlgorithm.Optimal;

    public PageReplacementDecision SelectVictim(
        IReadOnlyList<PageFrameSnapshot> occupiedFrames,
        IReadOnlyList<ulong> futurePageReferences,
        int clockHand)
    {
        var victim = occupiedFrames
            .Select(frame => new
            {
                Frame = frame,
                NextUse = FindNextUse(futurePageReferences, frame.VirtualPageNumber!.Value)
            })
            .OrderByDescending(item => item.NextUse ?? int.MaxValue)
            .ThenBy(item => item.Frame.Index)
            .First();
        return new PageReplacementDecision(victim.Frame.Index, Array.Empty<int>(), clockHand);
    }

    private static int? FindNextUse(IReadOnlyList<ulong> future, ulong page)
    {
        for (var index = 0; index < future.Count; index++)
        {
            if (future[index] == page)
            {
                return index;
            }
        }

        return null;
    }
}

public sealed class ClockPageReplacementPolicy : IPageReplacementPolicy
{
    public PageReplacementAlgorithm Algorithm => PageReplacementAlgorithm.Clock;

    public PageReplacementDecision SelectVictim(
        IReadOnlyList<PageFrameSnapshot> occupiedFrames,
        IReadOnlyList<ulong> futurePageReferences,
        int clockHand)
    {
        var byIndex = occupiedFrames.ToDictionary(frame => frame.Index);
        var cleared = new HashSet<int>();
        var hand = clockHand;
        while (true)
        {
            if (!byIndex.TryGetValue(hand, out var frame))
            {
                hand = (hand + 1) % (byIndex.Keys.Max() + 1);
                continue;
            }

            if (!frame.Referenced || cleared.Contains(frame.Index))
            {
                return new PageReplacementDecision(frame.Index, Array.AsReadOnly(cleared.Order().ToArray()), (frame.Index + 1) % (byIndex.Keys.Max() + 1));
            }

            cleared.Add(frame.Index);
            hand = (hand + 1) % (byIndex.Keys.Max() + 1);
        }
    }
}
