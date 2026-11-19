using OsSim.Domain.Memory;

namespace OsSim.Domain.Tests;

public sealed class VirtualMemoryTests
{
    private static readonly ulong[] ClassicReferenceString = [7, 0, 1, 2, 0, 3, 0, 4, 2, 3, 0, 3, 2];

    [Fact]
    public void Fifo_has_ten_faults_for_the_classic_three_frame_reference_string()
    {
        var result = RunToEnd(new VirtualMemoryInput(ClassicReferenceString, 8, 1, 3, PageReplacementAlgorithm.Fifo));

        Assert.Equal(10, result.Metrics.PageFaults);
        Assert.Equal(3, result.Metrics.AccessCount - result.Metrics.PageFaults);
    }

    [Fact]
    public void Optimal_uses_future_references_to_reduce_faults()
    {
        var result = RunToEnd(new VirtualMemoryInput(ClassicReferenceString, 8, 1, 3, PageReplacementAlgorithm.Optimal));

        Assert.Equal(7, result.Metrics.PageFaults);
    }

    [Fact]
    public void Tlb_hit_is_not_counted_as_page_table_hit_or_page_fault()
    {
        var result = RunToEnd(new VirtualMemoryInput([0, 0, 4], 8, 4, 2, PageReplacementAlgorithm.Lru, TlbCapacity: 2));

        Assert.Equal(1, result.Metrics.TlbHits);
        Assert.Equal(2, result.Metrics.TlbMisses);
        Assert.Equal(2, result.Metrics.PageFaults);
        Assert.Equal(0, result.Metrics.PageTableHits);
    }

    [Fact]
    public void Tlb_miss_can_be_a_page_table_hit_without_a_fault()
    {
        var result = RunToEnd(new VirtualMemoryInput([0, 4, 0], 8, 4, 2, PageReplacementAlgorithm.Fifo, TlbCapacity: 1));

        Assert.Equal(3, result.Metrics.TlbMisses);
        Assert.Equal(1, result.Metrics.PageTableHits);
        Assert.Equal(2, result.Metrics.PageFaults);
    }

    [Fact]
    public void Evicting_a_page_invalidates_its_tlb_entry()
    {
        var creation = new VirtualMemoryModule().CreateSession(
            new VirtualMemoryInput([0, 4, 8, 0], 8, 4, 2, PageReplacementAlgorithm.Fifo, TlbCapacity: 2));
        Assert.True(creation.Succeeded);
        var session = creation.Session!;
        session.StepForward();
        session.StepForward();
        session.StepForward();

        Assert.DoesNotContain(session.CurrentSnapshot.Tlb, entry => entry.VirtualPageNumber == 0);
        Assert.DoesNotContain(session.CurrentSnapshot.PageTable, entry => entry.VirtualPageNumber == 0);

        var final = RunToEnd(new VirtualMemoryInput([0, 4, 8, 0], 8, 4, 2, PageReplacementAlgorithm.Fifo, TlbCapacity: 2));
        Assert.Equal(4, final.Metrics.PageFaults);
    }

    [Fact]
    public void Address_translation_splits_page_and_offset()
    {
        var result = RunToEnd(new VirtualMemoryInput([13], 8, 4, 2, PageReplacementAlgorithm.Lru));

        Assert.Equal((ulong)3, result.VirtualPageNumber);
        Assert.Equal((ulong)1, result.Offset);
        Assert.Equal((ulong)1, result.PhysicalAddress);
        Assert.Equal(MemoryAccessOutcome.PageFault, result.Outcome);
    }

    [Theory]
    [InlineData(PageReplacementAlgorithm.Fifo)]
    [InlineData(PageReplacementAlgorithm.Lru)]
    [InlineData(PageReplacementAlgorithm.Optimal)]
    [InlineData(PageReplacementAlgorithm.Clock)]
    public void Every_replacement_policy_completes_a_finite_trace(PageReplacementAlgorithm algorithm)
    {
        var result = RunToEnd(new VirtualMemoryInput([0, 4, 8, 12, 0, 8, 16, 4], 8, 4, 3, algorithm));

        Assert.True(result.IsCompleted);
        Assert.Equal(8, result.Metrics.AccessCount);
    }

    [Fact]
    public void Invalid_page_size_and_out_of_range_address_are_rejected()
    {
        var result = new VirtualMemoryModule().CreateSession(
            new VirtualMemoryInput([8], 3, 3, 2, PageReplacementAlgorithm.Fifo));

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.True(result.Errors.Count >= 2);
    }

    private static VirtualMemorySnapshot RunToEnd(VirtualMemoryInput input)
    {
        var creation = new VirtualMemoryModule().CreateSession(input);
        Assert.True(creation.Succeeded, string.Join(Environment.NewLine, creation.Errors.Select(error => error.Message)));
        var session = creation.Session!;
        var guard = 0;
        while (session.CanStepForward && guard++ < 10_000)
        {
            var step = session.StepForward();
            Assert.True(step.Succeeded, step.Error?.Message);
        }

        Assert.False(session.CanStepForward);
        return session.CurrentSnapshot;
    }
}
