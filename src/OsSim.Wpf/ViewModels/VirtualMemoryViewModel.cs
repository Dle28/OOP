using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsSim.Application;
using OsSim.Domain.Memory;
using OsSim.Domain.Simulation;
using OsSim.Wpf.Playback;

namespace OsSim.Wpf.ViewModels;

public partial class VirtualMemoryViewModel : ObservableObject, ISimulationAreaViewModel
{
    private const int MaximumVisibleReferences = 14;
    private readonly SimulationCatalog _catalog;
    private IReadOnlyList<ulong> _referenceAddresses = Array.Empty<ulong>();
    private MemoryAccessOutcome?[] _referenceOutcomes = [];
    private int _activePageSize = 1;

    [ObservableProperty]
    private string addressInput = "0, 4, 0, 8, 12, 4, 16, 0";

    [ObservableProperty]
    private int virtualAddressBits = 16;

    [ObservableProperty]
    private int pageSize = 4;

    [ObservableProperty]
    private int frameCount = 4;

    [ObservableProperty]
    private int tlbCapacity = 2;

    [ObservableProperty]
    private PageReplacementAlgorithm selectedAlgorithm = PageReplacementAlgorithm.Lru;

    [ObservableProperty]
    private string statusText = "Nhập địa chỉ ảo dạng thập phân hoặc hệ 16 có tiền tố 0x.";

    public VirtualMemoryViewModel(SimulationCatalog catalog)
    {
        _catalog = catalog;
        Playback = new PlaybackController<VirtualMemorySnapshot>();
        Playback.SnapshotUpdated += OnSnapshotUpdated;
        CreateSimulation();
    }

    public PlaybackController<VirtualMemorySnapshot> Playback { get; }

    public SimulationArea Area => SimulationArea.Memory;

    public IReadOnlyList<SelectionOption<PageReplacementAlgorithm>> Algorithms { get; } =
    [
        new(PageReplacementAlgorithm.Fifo, "FIFO"),
        new(PageReplacementAlgorithm.Lru, "LRU"),
        new(PageReplacementAlgorithm.Optimal, "Optimal (tham chiếu tương lai)"),
        new(PageReplacementAlgorithm.Clock, "Clock (cơ hội thứ hai)")
    ];

    public VirtualMemorySnapshot? Snapshot => Playback.CurrentSnapshot;

    public SimulationActionVisual CurrentAction =>
        SimulationActionNarrator.Describe(Area, Snapshot?.Events.LastOrDefault());

    public IReadOnlyList<MetricCardVisual> MetricCards
    {
        get
        {
            var metrics = Snapshot?.Metrics;
            var accesses = metrics?.AccessCount ?? 0;
            var hits = metrics is null ? 0 : metrics.TlbHits + metrics.PageTableHits;
            var faults = metrics?.PageFaults ?? 0;
            return
            [
                new MetricCardVisual("TỶ LỆ TRÚNG", Ratio(hits, accesses),
                    accesses == 0 ? "TLB + bảng trang" : $"{hits} / {accesses} lượt · TLB + bảng trang", MetricTone.Success),
                new MetricCardVisual("TỶ LỆ LỖI TRANG", Ratio(faults, accesses),
                    accesses == 0 ? "Chưa truy cập địa chỉ" : $"{faults} / {accesses} lượt", MetricTone.Warning),
                new MetricCardVisual("LƯỢT THAY TRANG", metrics?.Replacements.ToString() ?? "—",
                    accesses == 0 ? "Chưa có lượt truy cập"
                        : $"swap-in {metrics!.SwapIns} · swap-out {metrics.SwapOuts}", MetricTone.Primary)
            ];
        }
    }

    public string ReferenceSequenceCaption
    {
        get
        {
            if (_referenceAddresses.Count == 0)
            {
                return "Tạo mô phỏng để xem chuỗi trang.";
            }

            var (start, end) = GetVisibleReferenceRange();
            return end - start == _referenceAddresses.Count
                ? $"{_referenceAddresses.Count} lượt tham chiếu"
                : $"Hiển thị {start + 1}–{end} / {_referenceAddresses.Count} lượt";
        }
    }

    public IReadOnlyList<MemoryReferenceVisual> ReferenceSequence
    {
        get
        {
            var (start, end) = GetVisibleReferenceRange();
            var processed = Snapshot?.Metrics.AccessCount ?? 0;
            var currentIndex = processed - 1;
            var pageSize = (ulong)_activePageSize;
            return Enumerable.Range(start, end - start)
                .Select(index =>
                {
                    var outcome = index < _referenceOutcomes.Length ? _referenceOutcomes[index] : null;
                    var current = index == currentIndex;
                    return new MemoryReferenceVisual(
                        index + 1,
                        _referenceAddresses[index],
                        _referenceAddresses[index] / pageSize,
                        ReferenceOutcomeLabel(outcome),
                        current,
                        outcome is MemoryAccessOutcome.TlbHit or MemoryAccessOutcome.PageTableHit,
                        outcome == MemoryAccessOutcome.PageFault,
                        current ? Snapshot?.AccessIndex ?? -1 : -1);
                })
                .ToArray();
        }
    }

    public IReadOnlyList<MemoryFlowStageVisual> TranslationFlow
    {
        get
        {
            var activeStage = Snapshot?.Outcome switch
            {
                MemoryAccessOutcome.TlbHit => 2,
                MemoryAccessOutcome.PageTableHit => 3,
                MemoryAccessOutcome.PageFault => 4,
                _ => -1
            };
            var labels = new[] { "Địa chỉ ảo", "Tách VPN / độ dời", "Tra TLB", "Tra bảng trang", "Xử lý lỗi trang", "Địa chỉ vật lý" };
            return labels.Select((label, index) => new MemoryFlowStageVisual(
                $"BƯỚC {index + 1}",
                label,
                activeStage == index ? "ĐANG XỬ LÝ"
                    : activeStage >= 0 && (index < activeStage || index == labels.Length - 1) ? "HOÀN TẤT"
                    : IsSkippedStage(index, activeStage) ? "BỎ QUA"
                    : "ĐANG CHỜ",
                activeStage == index,
                activeStage >= 0 && index < activeStage || index == labels.Length - 1 && activeStage >= 0,
                IsSkippedStage(index, activeStage),
                activeStage == index ? Snapshot!.AccessIndex : -1))
                .ToArray();
        }
    }

    public IReadOnlyList<MemoryFrameVisual> FrameCards
    {
        get
        {
            var snapshot = Snapshot;
            if (snapshot is null) return Array.Empty<MemoryFrameVisual>();

            var currentAccessTime = Math.Max(0, snapshot.AccessIndex - 1);
            var replacedFrame = snapshot.Events.Any(item => item.VirtualTime == currentAccessTime && item.Code == "PAGE_REPLACEMENT")
                ? snapshot.FrameIndex
                : null;
            return snapshot.Frames.Select(frame => new MemoryFrameVisual(
                frame.Index,
                frame.VirtualPageNumber is { } page ? $"Trang {page}" : "Chưa nạp",
                frame.VirtualPageNumber is null ? "Frame trống" : frame.Referenced ? "Đã tham chiếu" : "Chưa tham chiếu",
                frame.VirtualPageNumber is null,
                frame.Index == snapshot.FrameIndex,
                frame.Index == replacedFrame,
                frame.Index == snapshot.FrameIndex ? snapshot.AccessIndex : -1))
                .ToArray();
        }
    }

    public IReadOnlyList<PageTableEntry> PageTable => Snapshot?.PageTable ?? Array.Empty<PageTableEntry>();

    public IReadOnlyList<TlbEntry> Tlb => Snapshot?.Tlb ?? Array.Empty<TlbEntry>();

    public IReadOnlyList<ulong> SwapPages => Snapshot?.SwapPages ?? Array.Empty<ulong>();

    public string SwapSummary => Snapshot is null
        ? "Trang bị thay khỏi RAM sẽ xuất hiện tại đây."
        : $"{Snapshot.SwapPages.Count} trang trên đĩa · swap-in {Snapshot.Metrics.SwapIns} · swap-out {Snapshot.Metrics.SwapOuts}";

    public IReadOnlyList<SimulationEvent> Events => Snapshot?.Events.Reverse().ToArray() ?? Array.Empty<SimulationEvent>();

    public string CurrentTranslation => Snapshot?.VirtualAddress is not { } address
        ? "Thông tin dịch địa chỉ xuất hiện sau lượt truy cập đầu tiên."
        : $"Địa chỉ ảo {address} → trang {Snapshot.VirtualPageNumber}, độ dời {Snapshot.Offset}, " +
          $"frame {Snapshot.FrameIndex}, địa chỉ vật lý {Snapshot.PhysicalAddress} ({TranslateOutcome(Snapshot.Outcome)})";

    public string MetricsText => Snapshot is null
        ? "Chưa xử lý địa chỉ nào."
        : $"TLB hit/miss: {Snapshot.Metrics.TlbHits}/{Snapshot.Metrics.TlbMisses}    " +
          $"Hit bảng trang: {Snapshot.Metrics.PageTableHits}    " +
          $"Swap in/out: {Snapshot.Metrics.SwapIns}/{Snapshot.Metrics.SwapOuts}";

    [RelayCommand]
    private void CreateSimulation()
    {
        if (!TryParseAddresses(out var addresses, out var error))
        {
            StatusText = error!.Message;
            return;
        }

        var input = new VirtualMemoryInput(addresses!, VirtualAddressBits, PageSize, FrameCount, SelectedAlgorithm, TlbCapacity);
        var result = _catalog.CreateVirtualMemorySession(input);
        if (!result.Succeeded)
        {
            StatusText = string.Join(Environment.NewLine, result.Errors.Select(item => item.Message));
            return;
        }

        _referenceAddresses = Array.AsReadOnly(addresses!.ToArray());
        _referenceOutcomes = new MemoryAccessOutcome?[_referenceAddresses.Count];
        _activePageSize = PageSize;
        Playback.Attach(result.Session!);
        var algorithmLabel = Algorithms.Single(option => option.Value == SelectedAlgorithm).Label;
        StatusText = $"Đã tạo mô phỏng {algorithmLabel} cho {addresses!.Count} địa chỉ.";
        NotifySnapshotProperties();
    }

    private bool TryParseAddresses(out IReadOnlyList<ulong>? addresses, out SimulationError? error)
    {
        var values = new List<ulong>();
        var tokens = AddressInput.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var parsed = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var valueHex) ? valueHex : (ulong?)null
                : ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valueDecimal) ? valueDecimal : null;
            if (parsed is null)
            {
                addresses = null;
                error = new SimulationError(
                    SimulationErrorCode.InvalidInput,
                    $"Địa chỉ '{token}' không phải số nguyên không âm hợp lệ.");
                return false;
            }

            values.Add(parsed.Value);
        }

        addresses = values;
        error = null;
        return true;
    }

    private void OnSnapshotUpdated()
    {
        UpdateReferenceOutcomes();
        NotifySnapshotProperties();
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(MetricCards));
        OnPropertyChanged(nameof(ReferenceSequenceCaption));
        OnPropertyChanged(nameof(ReferenceSequence));
        OnPropertyChanged(nameof(TranslationFlow));
        OnPropertyChanged(nameof(FrameCards));
        OnPropertyChanged(nameof(PageTable));
        OnPropertyChanged(nameof(Tlb));
        OnPropertyChanged(nameof(SwapPages));
        OnPropertyChanged(nameof(SwapSummary));
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(CurrentTranslation));
        OnPropertyChanged(nameof(MetricsText));
    }

    private void UpdateReferenceOutcomes()
    {
        var snapshot = Snapshot;
        var processed = snapshot?.Metrics.AccessCount ?? 0;
        if (processed > 0 && snapshot?.Outcome is { } currentOutcome && processed <= _referenceOutcomes.Length)
        {
            _referenceOutcomes[processed - 1] = currentOutcome;
        }

        for (var index = processed; index < _referenceOutcomes.Length; index++)
        {
            _referenceOutcomes[index] = null;
        }
    }

    private (int Start, int End) GetVisibleReferenceRange()
    {
        var count = _referenceAddresses.Count;
        if (count <= MaximumVisibleReferences) return (0, count);

        var currentIndex = Math.Clamp((Snapshot?.Metrics.AccessCount ?? 0) - 1, 0, count - 1);
        var start = Math.Clamp(currentIndex - MaximumVisibleReferences / 2, 0, count - MaximumVisibleReferences);
        return (start, start + MaximumVisibleReferences);
    }

    private static string TranslateOutcome(MemoryAccessOutcome? outcome) => outcome switch
    {
        MemoryAccessOutcome.TlbHit => "TLB hit",
        MemoryAccessOutcome.PageTableHit => "hit bảng trang",
        MemoryAccessOutcome.PageFault => "lỗi trang",
        _ => "chưa xử lý"
    };

    private static bool IsSkippedStage(int index, int activeStage) => activeStage switch
    {
        2 => index is 3 or 4,
        3 => index == 4,
        _ => false
    };

    private static string Ratio(int count, int total) => total == 0
        ? "—"
        : $"{100d * count / total:F1}%";

    private static string ReferenceOutcomeLabel(MemoryAccessOutcome? outcome) => outcome switch
    {
        MemoryAccessOutcome.TlbHit => "TLB HIT",
        MemoryAccessOutcome.PageTableHit => "PT HIT",
        MemoryAccessOutcome.PageFault => "FAULT",
        _ => "CHỜ"
    };
}

public sealed record MemoryFlowStageVisual(
    string StepLabel,
    string Label,
    string StateLabel,
    bool IsCurrent,
    bool IsComplete,
    bool IsSkipped,
    long PulseVersion);
