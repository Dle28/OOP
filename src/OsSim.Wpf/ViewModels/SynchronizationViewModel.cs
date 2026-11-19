using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using OsSim.Application;
using OsSim.Domain.Simulation;
using OsSim.Domain.Synchronization;
using OsSim.Wpf.Playback;

namespace OsSim.Wpf.ViewModels;

public partial class SynchronizationViewModel : ObservableObject, ISimulationAreaViewModel
{
    private const int MaximumVisibleBufferSlots = 20;
    private readonly SimulationCatalog _catalog;
    private int _activeBufferCapacity;

    [ObservableProperty]
    private SynchronizationScenario selectedScenario = SynchronizationScenario.ProducerConsumer;

    [ObservableProperty]
    private DiningPhilosophersMode selectedDiningMode = DiningPhilosophersMode.NaiveLeftFirst;

    [ObservableProperty]
    private int bufferCapacity = 3;

    [ObservableProperty]
    private int producerCount = 1;

    [ObservableProperty]
    private int consumerCount = 1;

    [ObservableProperty]
    private int itemsPerProducer = 3;

    [ObservableProperty]
    private int itemsPerConsumer = 3;

    [ObservableProperty]
    private int philosopherCount = 5;

    [ObservableProperty]
    private int mealsPerPhilosopher = 2;

    [ObservableProperty]
    private string? selectedActorId;

    [ObservableProperty]
    private string statusText = "Chọn kịch bản rồi tạo mô phỏng.";

    public SynchronizationViewModel(SimulationCatalog catalog)
    {
        _catalog = catalog;
        Playback = new PlaybackController<SynchronizationSnapshot>();
        Playback.SnapshotUpdated += OnSnapshotUpdated;
        CreateSimulation();
    }

    public PlaybackController<SynchronizationSnapshot> Playback { get; }

    public SimulationArea Area => SimulationArea.Synchronization;

    public IReadOnlyList<SelectionOption<SynchronizationScenario>> Scenarios { get; } =
    [
        new(SynchronizationScenario.ProducerConsumer, "Nhà sản xuất – người tiêu dùng"),
        new(SynchronizationScenario.DiningPhilosophers, "Bài toán triết gia ăn tối")
    ];

    public IReadOnlyList<SelectionOption<DiningPhilosophersMode>> DiningModes { get; } =
    [
        new(DiningPhilosophersMode.NaiveLeftFirst, "Lấy đũa trái trước (có thể deadlock)"),
        new(DiningPhilosophersMode.ResourceOrdering, "Phân cấp tài nguyên (tránh deadlock)")
    ];

    public Visibility ProducerConsumerVisibility => SelectedScenario == SynchronizationScenario.ProducerConsumer
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DiningVisibility => SelectedScenario == SynchronizationScenario.DiningPhilosophers
        ? Visibility.Visible
        : Visibility.Collapsed;

    public SynchronizationSnapshot? Snapshot => Playback.CurrentSnapshot;

    public SimulationActionVisual CurrentAction =>
        SimulationActionNarrator.Describe(Area, Snapshot?.Events.LastOrDefault(), Snapshot?.IsDeadlocked == true);

    public IReadOnlyList<MetricCardVisual> MetricCards
    {
        get
        {
            var snapshot = Snapshot;
            var status = snapshot is null ? "Chưa tạo"
                : snapshot.IsDeadlocked ? "Deadlock"
                : snapshot.IsCompleted ? "Hoàn tất"
                : "Đang chạy";
            var statusTone = snapshot?.IsDeadlocked == true ? MetricTone.Danger
                : snapshot?.IsCompleted == true ? MetricTone.Success
                : MetricTone.Primary;
            var readyCount = snapshot?.ReadyActorIds.Count ?? 0;
            var actorCount = snapshot?.Actors.Count ?? 0;
            var blockedCount = snapshot?.Actors.Count(actor => actor.State == VirtualActorState.Blocked) ?? 0;
            return
            [
                new MetricCardVisual("TRẠNG THÁI KỊCH BẢN", status,
                    snapshot?.IsDeadlocked == true ? "Không còn actor sẵn sàng" : "Theo snapshot hiện tại", statusTone),
                new MetricCardVisual("ACTOR SẴN SÀNG", snapshot is null ? "—" : $"{readyCount} / {actorCount}",
                    snapshot is null ? "Chưa tạo mô phỏng" : "có thể được dispatch ở bước tiếp", MetricTone.Primary),
                new MetricCardVisual("ACTOR ĐANG CHỜ", snapshot is null ? "—" : blockedCount.ToString(),
                    snapshot is null ? "Chưa tạo mô phỏng" : "đang bị chặn trên tài nguyên", 
                    blockedCount > 0 ? MetricTone.Warning : MetricTone.Primary)
            ];
        }
    }

    public string StepText => Snapshot is null ? "Chưa chạy bước nào" : $"Bước {Snapshot.StepNumber}";

    public IReadOnlyList<SelectionOption<string>> ReadyActors => Snapshot?.ReadyActorIds
        .Select(id => new SelectionOption<string>(id, ActorLabel(id)))
        .ToArray() ?? Array.Empty<SelectionOption<string>>();

    public IReadOnlyList<SynchronizationActorDisplay> Actors => Snapshot?.Actors
        .Select(actor => new SynchronizationActorDisplay(
            ActorLabel(actor.Id),
            actor.Role switch
            {
                "Producer" => "Nhà sản xuất",
                "Consumer" => "Người tiêu dùng",
                "Philosopher" => "Triết gia",
                _ => actor.Role
            },
            actor.State switch
            {
                VirtualActorState.Ready => "Sẵn sàng",
                VirtualActorState.Blocked => "Đang chờ",
                VirtualActorState.Terminated => "Đã kết thúc",
                _ => actor.State.ToString()
            },
            actor.CompletedWork,
            actor.WaitingFor is null ? null : ResourceLabel(actor.WaitingFor),
            string.Join(", ", actor.HeldResources.Select(ResourceLabel)),
            actor.Id == Snapshot!.LastDispatchedActorId,
            actor.Id == Snapshot!.LastDispatchedActorId ? Snapshot.StepNumber : -1))
        .ToArray() ?? Array.Empty<SynchronizationActorDisplay>();

    public IReadOnlyList<SynchronizationResourceDisplay> Resources => Snapshot?.Resources
        .Select(resource => new SynchronizationResourceDisplay(
            ResourceLabel(resource.Id), resource.Count,
            resource.OwnerActorId is null ? null : ActorLabel(resource.OwnerActorId),
            string.Join(", ", resource.WaitQueue.Select(ActorLabel))))
        .ToArray() ?? Array.Empty<SynchronizationResourceDisplay>();

    public string BufferSummary
    {
        get
        {
            var snapshot = Snapshot;
            return snapshot is null
                ? "Các ô buffer sẽ xuất hiện sau khi tạo mô phỏng."
                : _activeBufferCapacity == 0
                    ? "Buffer không được dùng trong kịch bản triết gia."
                    : $"Đang chứa {snapshot.Buffer.Count}/{_activeBufferCapacity} sản phẩm.";
        }
    }

    public IReadOnlyList<BufferSlotVisual> BufferSlots
    {
        get
        {
            var snapshot = Snapshot;
            if (snapshot is null)
            {
                return Array.Empty<BufferSlotVisual>();
            }

            var lastEvent = snapshot.Events.LastOrDefault();
            var visibleCount = Math.Min(_activeBufferCapacity, MaximumVisibleBufferSlots);
            var slots = Enumerable.Range(0, visibleCount)
                .Select(index =>
                {
                    var isOccupied = index < snapshot.Buffer.Count;
                    var isChanged = lastEvent?.Code switch
                    {
                        "PRODUCE" => isOccupied && index == snapshot.Buffer.Count - 1,
                        "CONSUME" => index == 0,
                        _ => false
                    };

                    return new BufferSlotVisual(
                        $"Ô {index + 1}",
                        isOccupied ? ItemLabel(snapshot.Buffer[index]) : "Trống",
                        isOccupied,
                        isChanged ? snapshot.StepNumber : -1);
                })
                .ToList();

            if (_activeBufferCapacity > visibleCount)
            {
                var hiddenOccupied = Math.Max(0, snapshot.Buffer.Count - visibleCount);
                var hiddenEmpty = Math.Max(0, _activeBufferCapacity - visibleCount - hiddenOccupied);
                var summary = hiddenOccupied > 0
                    ? $"+{hiddenOccupied} sản phẩm"
                    : $"+{hiddenEmpty} ô trống";
                slots.Add(new BufferSlotVisual("Phần còn lại", summary, false, -1));
            }

            return slots;
        }
    }

    public IReadOnlyList<WaitGraphEdgeVisual> WaitGraphEdges => Snapshot?.WaitForGraph
        .SelectMany(CreateWaitGraphEdges)
        .ToArray() ?? Array.Empty<WaitGraphEdgeVisual>();

    public string WaitGraphSummary => Snapshot is null
        ? "Tạo mô phỏng để xem các cạnh chờ."
        : WaitGraphEdges.Count == 0
            ? "Không có cạnh chờ trong snapshot này."
            : "Actor → tài nguyên: yêu cầu · tài nguyên → actor: đang giữ.";

    public IReadOnlyList<string> DeadlockCycles => Snapshot?.DeadlockCycles
        .Select(cycle => string.Join(" → ", cycle.Append(cycle[0]).Select(ActorLabel)))
        .ToArray() ?? Array.Empty<string>();

    public string DeadlockSummary => Snapshot is null
        ? "Chưa có snapshot để kiểm tra chu trình."
        : Snapshot.IsDeadlocked
            ? $"Phát hiện {DeadlockCycles.Count} chu trình chờ."
            : "Không phát hiện deadlock ở bước hiện tại.";

    public IReadOnlyList<SimulationEvent> Events => Snapshot?.Events.Reverse().ToArray() ?? Array.Empty<SimulationEvent>();

    [RelayCommand]
    private void CreateSimulation()
    {
        var input = new SynchronizationInput(
            SelectedScenario, BufferCapacity, ProducerCount, ConsumerCount, ItemsPerProducer, ItemsPerConsumer,
            PhilosopherCount, MealsPerPhilosopher, SelectedDiningMode);
        var result = _catalog.CreateSynchronizationSession(input);
        if (!result.Succeeded)
        {
            StatusText = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
            return;
        }

        Playback.Attach(result.Session!, automatic => automatic
            ? new SimulationCommand(AutomaticDispatch: true)
            : new SimulationCommand(SelectedActorId, AutomaticDispatch: false));
        _activeBufferCapacity = SelectedScenario == SynchronizationScenario.ProducerConsumer ? BufferCapacity : 0;
        SelectedActorId = result.Session!.CurrentSnapshot.ReadyActorIds.FirstOrDefault();
        var scenarioLabel = Scenarios.Single(option => option.Value == SelectedScenario).Label;
        StatusText = $"Đã tạo mô phỏng: {scenarioLabel}.";
        NotifySnapshotProperties();
    }

    private void OnSnapshotUpdated()
    {
        if (SelectedActorId is null || !(Snapshot?.ReadyActorIds ?? Array.Empty<string>()).Contains(SelectedActorId, StringComparer.Ordinal))
        {
            SelectedActorId = Snapshot?.ReadyActorIds.FirstOrDefault();
        }

        if (Snapshot?.IsDeadlocked == true)
        {
            StatusText = "Đã phát hiện deadlock. Xem đồ thị chờ và chu trình.";
        }
        else if (Snapshot?.IsCompleted == true)
        {
            StatusText = "Kịch bản đồng bộ đã hoàn tất.";
        }
        else if (Snapshot is not null)
        {
            StatusText = Playback.StatusMessage;
        }

        NotifySnapshotProperties();
    }

    partial void OnSelectedScenarioChanged(SynchronizationScenario value)
    {
        OnPropertyChanged(nameof(ProducerConsumerVisibility));
        OnPropertyChanged(nameof(DiningVisibility));
        SelectedActorId = null;
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(MetricCards));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(ReadyActors));
        OnPropertyChanged(nameof(Actors));
        OnPropertyChanged(nameof(Resources));
        OnPropertyChanged(nameof(BufferSummary));
        OnPropertyChanged(nameof(BufferSlots));
        OnPropertyChanged(nameof(WaitGraphEdges));
        OnPropertyChanged(nameof(WaitGraphSummary));
        OnPropertyChanged(nameof(DeadlockCycles));
        OnPropertyChanged(nameof(DeadlockSummary));
        OnPropertyChanged(nameof(Events));
    }

    private static string ActorLabel(string id)
    {
        if (TryGetOrdinalId(id, "Producer-", out var producer))
        {
            return $"Nhà sản xuất {producer}";
        }

        if (TryGetOrdinalId(id, "Consumer-", out var consumer))
        {
            return $"Người tiêu dùng {consumer}";
        }

        if (TryGetOrdinalId(id, "Philosopher-", out var philosopher))
        {
            return $"Triết gia {philosopher + 1}";
        }

        return id;
    }

    private static string ResourceLabel(string id)
    {
        if (id == "empty") return "Ô buffer trống";
        if (id == "full") return "Ô buffer có dữ liệu";
        if (id == "mutex") return "Mutex";
        if (TryGetOrdinalId(id, "fork-", out var fork)) return $"Đũa {fork + 1}";
        return id;
    }

    private static string ItemLabel(string id)
    {
        const string prefix = "Item-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return id;
        var itemIndex = id.LastIndexOf('-');
        if (itemIndex <= prefix.Length || !int.TryParse(id[(itemIndex + 1)..], out var number)) return id;
        return $"{ActorLabel(id[prefix.Length..itemIndex])} – sản phẩm {number}";
    }

    private static IReadOnlyList<WaitGraphEdgeVisual> CreateWaitGraphEdges(WaitForEdge edge)
    {
        var request = new WaitGraphEdgeVisual(
            ActorLabel(edge.WaiterActorId), ResourceLabel(edge.ResourceId), "YÊU CẦU", false);
        return edge.OwnerActorId is null
            ? [request]
            : [request, new WaitGraphEdgeVisual(ResourceLabel(edge.ResourceId), ActorLabel(edge.OwnerActorId), "ĐANG GIỮ", true)];
    }

    private static bool TryGetOrdinalId(string id, string prefix, out int ordinal)
    {
        if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], out ordinal))
        {
            return true;
        }

        ordinal = 0;
        return false;
    }
}

public sealed record SynchronizationActorDisplay(
    string Id,
    string Role,
    string State,
    int CompletedWork,
    string? WaitingFor,
    string HeldResources,
    bool IsDispatched,
    long PulseVersion)
{
    public bool IsBlocked => State == "Đang chờ";

    public string CompletedWorkLabel => $"Công việc hoàn tất: {CompletedWork}";
}

public sealed record BufferSlotVisual(string PositionLabel, string ContentLabel, bool IsOccupied, long PulseVersion);

public sealed record SynchronizationResourceDisplay(string Id, int Count, string? OwnerActorId, string WaitQueue);
