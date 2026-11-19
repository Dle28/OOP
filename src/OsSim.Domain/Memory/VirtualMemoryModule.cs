using OsSim.Domain.Simulation;

namespace OsSim.Domain.Memory;

public sealed class VirtualMemoryModule : ISimulationModule<VirtualMemoryInput, VirtualMemorySnapshot>
{
    private const int MaxFrameCount = 1024;
    private const int MaxReferenceCount = 5_000;
    private const int MaxTlbCapacity = 4_096;
    private readonly IReadOnlyDictionary<PageReplacementAlgorithm, IPageReplacementPolicy> _policies =
        new Dictionary<PageReplacementAlgorithm, IPageReplacementPolicy>
        {
            [PageReplacementAlgorithm.Fifo] = new FifoPageReplacementPolicy(),
            [PageReplacementAlgorithm.Lru] = new LruPageReplacementPolicy(),
            [PageReplacementAlgorithm.Optimal] = new OptimalPageReplacementPolicy(),
            [PageReplacementAlgorithm.Clock] = new ClockPageReplacementPolicy()
        };

    public ValidationResult Validate(VirtualMemoryInput input)
    {
        var errors = new List<SimulationError>();
        if (input.VirtualAddresses is null || input.VirtualAddresses.Count == 0)
        {
            errors.Add(Invalid("Cần nhập ít nhất một địa chỉ ảo."));
            return ValidationResult.From(errors);
        }

        if (input.VirtualAddresses.Count > MaxReferenceCount)
        {
            errors.Add(Invalid($"Hỗ trợ tối đa {MaxReferenceCount} lượt tham chiếu địa chỉ."));
        }

        if (input.VirtualAddressBits is < 1 or > 64)
        {
            errors.Add(Invalid("Độ rộng địa chỉ ảo phải từ 1 đến 64 bit."));
        }

        if (input.PageSize <= 0 || (input.PageSize & (input.PageSize - 1)) != 0)
        {
            errors.Add(Invalid("Kích thước trang phải là lũy thừa dương của 2."));
        }
        else if (input.VirtualAddressBits is >= 1 and < 64 && (ulong)input.PageSize > (1UL << input.VirtualAddressBits))
        {
            errors.Add(Invalid("Kích thước trang không thể lớn hơn không gian địa chỉ ảo."));
        }

        if (input.FrameCount is < 1 or > MaxFrameCount)
        {
            errors.Add(Invalid($"Số frame vật lý phải từ 1 đến {MaxFrameCount}."));
        }

        if (input.TlbCapacity is < 0 or > MaxTlbCapacity)
        {
            errors.Add(Invalid($"Sức chứa TLB phải từ 0 đến {MaxTlbCapacity}."));
        }

        if (!_policies.ContainsKey(input.ReplacementAlgorithm))
        {
            errors.Add(Invalid("Hãy chọn chính sách thay trang được hỗ trợ."));
        }

        if (input.VirtualAddressBits is >= 1 and < 64)
        {
            var addressSpaceSize = 1UL << input.VirtualAddressBits;
            foreach (var address in input.VirtualAddresses)
            {
                if (address >= addressSpaceSize)
                {
                    errors.Add(Invalid($"Địa chỉ ảo {address} nằm ngoài không gian địa chỉ {input.VirtualAddressBits} bit đã chọn."));
                }
            }
        }

        return ValidationResult.From(errors);
    }

    public SimulationCreationResult<VirtualMemorySnapshot> CreateSession(VirtualMemoryInput input)
    {
        var validation = Validate(input);
        if (!validation.IsValid)
        {
            return SimulationCreationResult<VirtualMemorySnapshot>.Failed(validation.Errors);
        }

        var trace = BuildTrace(input, _policies[input.ReplacementAlgorithm]);
        return SimulationCreationResult<VirtualMemorySnapshot>.Created(
            new TimelineSimulationSession<VirtualMemorySnapshot>(trace.Initial, trace.Frames));
    }

    private static (VirtualMemorySnapshot Initial, IReadOnlyList<SimulationFrame<VirtualMemorySnapshot>> Frames) BuildTrace(
        VirtualMemoryInput input,
        IPageReplacementPolicy policy)
    {
        var frames = Enumerable.Range(0, input.FrameCount)
            .Select(index => new MutableFrame { Index = index })
            .ToList();
        var pageTable = new Dictionary<ulong, int>();
        var tlb = new List<TlbEntry>();
        var swapPages = new List<ulong>();
        var events = new List<SimulationEvent>();
        var traceFrames = new List<SimulationFrame<VirtualMemorySnapshot>>();
        var metrics = new MutableMetrics();
        var accessOrder = 0L;
        var clockHand = 0;
        var initial = CreateSnapshot(0, null, null, null, null, null, null,
            frames, pageTable, tlb, swapPages, metrics, events, false);
        var pages = input.VirtualAddresses.Select(address => address / (ulong)input.PageSize).ToArray();

        for (var accessIndex = 0; accessIndex < input.VirtualAddresses.Count; accessIndex++)
        {
            var address = input.VirtualAddresses[accessIndex];
            var page = pages[accessIndex];
            var offset = address % (ulong)input.PageSize;
            var frameIndex = -1;
            var outcome = MemoryAccessOutcome.PageFault;
            var wasSwappedIn = false;
            ulong? swappedOutPage = null;

            if (input.TlbCapacity > 0)
            {
                var tlbIndex = tlb.FindIndex(entry => entry.VirtualPageNumber == page);
                if (tlbIndex >= 0)
                {
                    var entry = tlb[tlbIndex];
                    frameIndex = entry.FrameIndex;
                    tlb[tlbIndex] = entry with { LastUsedOrder = accessOrder };
                    metrics.TlbHits++;
                    outcome = MemoryAccessOutcome.TlbHit;
                }
                else
                {
                    metrics.TlbMisses++;
                }
            }

            if (frameIndex < 0 && pageTable.TryGetValue(page, out var residentFrame))
            {
                frameIndex = residentFrame;
                metrics.PageTableHits++;
                if (input.TlbCapacity > 0)
                {
                    InsertTlb(tlb, page, frameIndex, input.TlbCapacity, accessOrder);
                }

                outcome = MemoryAccessOutcome.PageTableHit;
            }
            else if (frameIndex < 0)
            {
                metrics.PageFaults++;
                wasSwappedIn = swapPages.Remove(page);
                if (wasSwappedIn)
                {
                    metrics.SwapIns++;
                }

                var freeFrame = frames.FirstOrDefault(frame => frame.VirtualPageNumber is null);
                if (freeFrame is not null)
                {
                    frameIndex = freeFrame.Index;
                }
                else
                {
                    var future = pages.Skip(accessIndex + 1).ToArray();
                    var selection = policy.SelectVictim(
                        frames.Select(ToSnapshot).ToArray(), future, clockHand);
                    clockHand = selection.NextClockHand;
                    foreach (var clearedIndex in selection.ClearedReferenceFrames)
                    {
                        frames[clearedIndex].Referenced = false;
                    }

                    frameIndex = selection.VictimFrameIndex;
                    var evictedPage = frames[frameIndex].VirtualPageNumber!.Value;
                    pageTable.Remove(evictedPage);
                    tlb.RemoveAll(entry => entry.VirtualPageNumber == evictedPage);
                    metrics.Replacements++;
                    swappedOutPage = evictedPage;
                    swapPages.Add(evictedPage);
                    metrics.SwapOuts++;
                }

                frames[frameIndex] = new MutableFrame
                {
                    Index = frameIndex,
                    VirtualPageNumber = page,
                    Referenced = true,
                    LoadedOrder = accessOrder,
                    LastAccessOrder = accessOrder
                };
                pageTable[page] = frameIndex;
                if (input.TlbCapacity > 0)
                {
                    InsertTlb(tlb, page, frameIndex, input.TlbCapacity, accessOrder);
                }

                outcome = MemoryAccessOutcome.PageFault;
            }

            var accessedFrame = frames[frameIndex];
            accessedFrame.Referenced = true;
            accessedFrame.LastAccessOrder = accessOrder;
            var physicalAddress = checked((ulong)frameIndex * (ulong)input.PageSize + offset);
            metrics.AccessCount++;
            if (swappedOutPage is { } outgoingPage)
            {
                events.Add(new SimulationEvent(
                    events.Count + 1,
                    accessIndex,
                    "PAGE_REPLACEMENT",
                    $"Frame {frameIndex} thay trang {outgoingPage} bằng trang {page}.",
                    null));
                events.Add(new SimulationEvent(
                    events.Count + 1,
                    accessIndex,
                    "SWAP_OUT",
                    $"Trang {outgoingPage} được đưa ra swap disk.",
                    null));
            }

            if (wasSwappedIn)
            {
                events.Add(new SimulationEvent(
                    events.Count + 1,
                    accessIndex,
                    "SWAP_IN",
                    $"Trang {page} được nạp lại từ swap disk vào frame {frameIndex}.",
                    null));
            }

            if (swappedOutPage is null && !wasSwappedIn)
            {
                events.Add(new SimulationEvent(
                    events.Count + 1,
                    accessIndex,
                    outcome.ToString().ToUpperInvariant(),
                    $"Địa chỉ {address}: trang {page}, độ dời {offset}, frame {frameIndex}, kết quả {OutcomeLabel(outcome)}.",
                    null));
            }
            var snapshot = CreateSnapshot(
                accessIndex + 1, address, page, offset, frameIndex, physicalAddress, outcome,
                frames, pageTable, tlb, swapPages, metrics, events,
                accessIndex == input.VirtualAddresses.Count - 1);
            traceFrames.Add(new SimulationFrame<VirtualMemorySnapshot>(snapshot, events[^1]));
            accessOrder++;
        }

        return (initial, Array.AsReadOnly(traceFrames.ToArray()));
    }

    private static VirtualMemorySnapshot CreateSnapshot(
        int accessIndex,
        ulong? address,
        ulong? page,
        ulong? offset,
        int? frameIndex,
        ulong? physicalAddress,
        MemoryAccessOutcome? outcome,
        IReadOnlyList<MutableFrame> frames,
        IReadOnlyDictionary<ulong, int> pageTable,
        IReadOnlyList<TlbEntry> tlb,
        IReadOnlyList<ulong> swapPages,
        MutableMetrics metrics,
        IReadOnlyList<SimulationEvent> events,
        bool completed)
    {
        var frameSnapshots = frames.Select(ToSnapshot).ToArray();
        var tableSnapshots = pageTable.OrderBy(entry => entry.Key)
            .Select(entry => new PageTableEntry(entry.Key, entry.Value)).ToArray();
        return new VirtualMemorySnapshot(
            accessIndex,
            address,
            page,
            offset,
            frameIndex,
            physicalAddress,
            outcome,
            Array.AsReadOnly(frameSnapshots),
            Array.AsReadOnly(tableSnapshots),
            Array.AsReadOnly(tlb.OrderBy(entry => entry.LastUsedOrder).ToArray()),
            Array.AsReadOnly(swapPages.ToArray()),
            new MemoryMetrics(metrics.AccessCount, metrics.TlbHits, metrics.TlbMisses, metrics.PageTableHits,
                metrics.PageFaults, metrics.Replacements, metrics.SwapIns, metrics.SwapOuts),
            Array.AsReadOnly(events.ToArray()),
            completed);
    }

    private static void InsertTlb(List<TlbEntry> tlb, ulong page, int frameIndex, int capacity, long accessOrder)
    {
        var index = tlb.FindIndex(entry => entry.VirtualPageNumber == page);
        if (index >= 0)
        {
            tlb[index] = new TlbEntry(page, frameIndex, accessOrder);
            return;
        }

        if (tlb.Count >= capacity)
        {
            var leastRecent = tlb.MinBy(entry => entry.LastUsedOrder)!;
            tlb.Remove(leastRecent);
        }

        tlb.Add(new TlbEntry(page, frameIndex, accessOrder));
    }

    private static PageFrameSnapshot ToSnapshot(MutableFrame frame) => new(
        frame.Index, frame.VirtualPageNumber, frame.Referenced, frame.LoadedOrder, frame.LastAccessOrder);

    private static string OutcomeLabel(MemoryAccessOutcome outcome) => outcome switch
    {
        MemoryAccessOutcome.TlbHit => "TLB hit",
        MemoryAccessOutcome.PageTableHit => "bảng trang đã có",
        MemoryAccessOutcome.PageFault => "lỗi trang",
        _ => outcome.ToString()
    };

    private static SimulationError Invalid(string message) => new(SimulationErrorCode.InvalidInput, message);

    private sealed class MutableFrame
    {
        public required int Index { get; init; }
        public ulong? VirtualPageNumber { get; set; }
        public bool Referenced { get; set; }
        public long LoadedOrder { get; set; }
        public long LastAccessOrder { get; set; }
    }

    private sealed class MutableMetrics
    {
        public int AccessCount { get; set; }
        public int TlbHits { get; set; }
        public int TlbMisses { get; set; }
        public int PageTableHits { get; set; }
        public int PageFaults { get; set; }
        public int Replacements { get; set; }
        public int SwapIns { get; set; }
        public int SwapOuts { get; set; }
    }
}
