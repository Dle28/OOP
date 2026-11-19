using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsSim.Application;
using OsSim.Domain.Cpu;
using OsSim.Domain.Simulation;
using OsSim.Wpf.Playback;

namespace OsSim.Wpf.ViewModels;

public partial class CpuSchedulerViewModel : ObservableObject, ISimulationAreaViewModel
{
    private readonly SimulationCatalog _catalog;

    [ObservableProperty]
    private string processInput = "P1, 0, 7, 2\nP2, 2, 4, 1\nP3, 4, 1, 0";

    [ObservableProperty]
    private CpuAlgorithm selectedAlgorithm = CpuAlgorithm.Fcfs;

    [ObservableProperty]
    private string timeQuantum = "2";

    [ObservableProperty]
    private string contextSwitchOverhead = "0.5";

    [ObservableProperty]
    private string statusText = "Nhập mỗi tiến trình theo dạng: mã, thời điểm đến, CPU burst, độ ưu tiên.";

    public CpuSchedulerViewModel(SimulationCatalog catalog)
    {
        _catalog = catalog;
        Playback = new PlaybackController<CpuSimulationSnapshot>();
        Playback.SnapshotUpdated += OnSnapshotUpdated;
        CreateSimulation();
    }

    public PlaybackController<CpuSimulationSnapshot> Playback { get; }

    public SimulationArea Area => SimulationArea.Cpu;

    public IReadOnlyList<SelectionOption<CpuAlgorithm>> Algorithms { get; } =
    [
        new(CpuAlgorithm.Fcfs, "FCFS"),
        new(CpuAlgorithm.Sjf, "SJF (không ngắt)"),
        new(CpuAlgorithm.Srtf, "SRTF (có ngắt)"),
        new(CpuAlgorithm.Priority, "Ưu tiên (không ngắt)"),
        new(CpuAlgorithm.PreemptivePriority, "Ưu tiên (có ngắt)"),
        new(CpuAlgorithm.RoundRobin, "Round Robin")
    ];

    public CpuSimulationSnapshot? Snapshot => Playback.CurrentSnapshot;

    public SimulationActionVisual CurrentAction =>
        SimulationActionNarrator.Describe(Area, Snapshot?.Events.LastOrDefault());

    public ObservableCollection<CpuProcessVisual> ProcessStates { get; } = [];

    public IReadOnlyList<string> ReadyQueue => Snapshot?.ReadyQueue
        .Select((id, index) => $"{index + 1}. {id}")
        .ToArray() ?? Array.Empty<string>();

    public string ReadyQueueStatus => Snapshot is null
        ? "Chưa tạo mô phỏng."
        : Snapshot.ReadyQueue.Count == 0 ? "Không có tiến trình chờ." : string.Empty;

    public IReadOnlyList<MetricCardVisual> MetricCards
    {
        get
        {
            var metrics = Snapshot?.Metrics;
            var pending = "Hiện khi mô phỏng hoàn tất";
            return
            [
                new MetricCardVisual("THỜI GIAN CHỜ TB", metrics?.AverageWaitingTime.ToString("F2") ?? "—",
                    metrics is null ? pending : "đơn vị thời gian", MetricTone.Primary),
                new MetricCardVisual("MỨC SỬ DỤNG CPU", metrics is null ? "—" : $"{metrics.CpuUtilizationPercent:F1}%",
                    metrics is null ? pending : "trên tổng thời gian mô phỏng", MetricTone.Success),
                new MetricCardVisual("LƯỢT CHUYỂN NGỮ CẢNH", Snapshot?.ContextSwitchCount.ToString() ?? "—",
                    Snapshot is null ? pending : $"overhead {Snapshot.ContextSwitchTime:F1} đơn vị", MetricTone.Warning)
            ];
        }
    }

    public string RunningProcessStatus => Snapshot?.DispatchingProcessId is { } dispatchingProcessId
        ? $"Chuyển ngữ cảnh tới {dispatchingProcessId} · thời điểm {Snapshot.Time:F1}"
        : Snapshot?.RunningProcessId is { } processId
            ? $"CPU đang chạy {processId} · thời điểm {Snapshot.Time:F1}"
            : Snapshot is null ? "CPU sẵn sàng" : $"CPU rỗi · thời điểm {Snapshot.Time:F1}";

    public long CpuPulseVersion => Snapshot is null || (Snapshot.RunningProcessId is null && Snapshot.DispatchingProcessId is null)
        ? -1
        : decimal.ToInt64(Snapshot.Time * 1_000m);

    public IReadOnlyList<GanttSegmentVisual> GanttSegments
    {
        get
        {
            if (Snapshot is null) return Array.Empty<GanttSegmentVisual>();
            return Snapshot.GanttSegments.TakeLast(64)
                .Select(segment =>
                {
                    var active = segment.IsContextSwitch
                        ? segment.End == Snapshot.Time && Snapshot.DispatchingProcessId is not null
                        : !segment.IsIdle && segment.ProcessId == Snapshot.RunningProcessId && segment.End == Snapshot.Time;
                    return new GanttSegmentVisual(
                        segment.Start,
                        segment.End,
                        segment.IsContextSwitch ? "CS"
                            : segment.IsIdle ? "CPU rỗi" : segment.ProcessId!,
                        segment.IsContextSwitch
                            ? Math.Clamp(28 + (double)segment.Duration * 20, 32, 120)
                            : Math.Clamp(40 + (double)segment.Duration * 12, 58, 180),
                        segment.IsIdle,
                        segment.IsContextSwitch,
                        active ? decimal.ToInt64(Snapshot.Time * 1_000m) : -1);
                })
                .ToArray();
        }
    }

    public string GanttCaption => Snapshot is null
        ? "Các đoạn thực thi sẽ hiện ra sau khi tạo mô phỏng."
        : Snapshot.GanttSegments.Count > 64
            ? $"Đang hiển thị 64 đoạn gần nhất trên tổng {Snapshot.GanttSegments.Count} đoạn."
            : "Khối cam CS là overhead chuyển ngữ cảnh; khối xám biểu diễn CPU rỗi.";

    public string MetricsText => Snapshot?.Metrics is not { } metrics
        ? "Số liệu xuất hiện khi các tiến trình hoàn tất."
        : $"Đáp ứng TB {metrics.AverageResponseTime:F2} · quay vòng TB {metrics.AverageTurnaroundTime:F2} · " +
          $"chi phí chuyển ngữ cảnh {metrics.ContextSwitchTime:F1} đơn vị.";

    public IReadOnlyList<CpuProcessMetrics> ProcessMetrics => Snapshot?.Metrics?.Processes ?? Array.Empty<CpuProcessMetrics>();

    public IReadOnlyList<OsSim.Domain.Simulation.SimulationEvent> Events =>
        Snapshot?.Events.Reverse().ToArray() ?? Array.Empty<OsSim.Domain.Simulation.SimulationEvent>();

    [RelayCommand]
    private void CreateSimulation()
    {
        if (!TryParseInput(out var processes, out var parseError))
        {
            StatusText = parseError!.Message;
            return;
        }

        if (!long.TryParse(TimeQuantum, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantum))
        {
            StatusText = InvalidInput("Quantum phải là số nguyên.").Message;
            return;
        }

        if (!decimal.TryParse(ContextSwitchOverhead, NumberStyles.Number, CultureInfo.InvariantCulture, out var overhead))
        {
            StatusText = InvalidInput("Chi phí chuyển ngữ cảnh phải là số không âm; dùng dấu chấm cho phần thập phân.").Message;
            return;
        }

        var result = _catalog.CreateCpuSession(new CpuSchedulingInput(
            processes!, SelectedAlgorithm, quantum, ContextSwitchOverhead: overhead));
        if (!result.Succeeded)
        {
            StatusText = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
            return;
        }

        Playback.Attach(result.Session!);
        RefreshProcessStates();
        var algorithmLabel = Algorithms.Single(option => option.Value == SelectedAlgorithm).Label;
        StatusText = $"Đã tạo mô phỏng {algorithmLabel} với {processes!.Count} tiến trình.";
        NotifySnapshotProperties();
    }

    private bool TryParseInput(out IReadOnlyList<CpuProcessInput>? processes, out SimulationError? error)
    {
        var parsed = new List<CpuProcessInput>();
        var lines = ProcessInput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var fields = lines[index].Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length is < 3 or > 4 ||
                !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var arrival) ||
                !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var burst))
            {
                processes = null;
                error = InvalidInput($"Dòng {index + 1} phải có dạng: mã, thời điểm đến, CPU burst, độ ưu tiên tùy chọn.");
                return false;
            }

            var priority = 0;
            if (fields.Length == 4 &&
                !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
            {
                processes = null;
                error = InvalidInput($"Dòng {index + 1} có độ ưu tiên không hợp lệ.");
                return false;
            }

            parsed.Add(new CpuProcessInput(fields[0], arrival, burst, priority));
        }

        processes = parsed;
        error = null;
        return true;
    }

    private void OnSnapshotUpdated()
    {
        RefreshProcessStates();
        NotifySnapshotProperties();
    }

    private void RefreshProcessStates()
    {
        var snapshot = Snapshot;
        var next = snapshot?.Processes
            .Select(process => new CpuProcessVisual(
                process.Id,
                ProcessStateLabel(process.State),
                process.RemainingTime,
                process.Priority,
                process.BurstTime == 0 ? 0 : 100d * (process.BurstTime - process.RemainingTime) / process.BurstTime,
                process.State == CpuProcessState.Running,
                process.State is CpuProcessState.Running or CpuProcessState.ContextSwitching
                    ? decimal.ToInt64(snapshot!.Time * 1_000m)
                    : -1))
            .ToArray() ?? Array.Empty<CpuProcessVisual>();

        if (!ProcessStates.Select(process => process.Id).SequenceEqual(next.Select(process => process.Id), StringComparer.Ordinal))
        {
            ProcessStates.Clear();
            foreach (var process in next)
            {
                ProcessStates.Add(process);
            }

            return;
        }

        for (var index = 0; index < next.Length; index++)
        {
            ProcessStates[index].Update(next[index]);
        }
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(ReadyQueue));
        OnPropertyChanged(nameof(ReadyQueueStatus));
        OnPropertyChanged(nameof(MetricCards));
        OnPropertyChanged(nameof(RunningProcessStatus));
        OnPropertyChanged(nameof(CpuPulseVersion));
        OnPropertyChanged(nameof(GanttSegments));
        OnPropertyChanged(nameof(GanttCaption));
        OnPropertyChanged(nameof(MetricsText));
        OnPropertyChanged(nameof(ProcessMetrics));
        OnPropertyChanged(nameof(Events));
    }

    private static string ProcessStateLabel(CpuProcessState state) => state switch
    {
        CpuProcessState.New => "Chưa đến",
        CpuProcessState.Ready => "Sẵn sàng",
        CpuProcessState.ContextSwitching => "Đang chuyển ngữ cảnh",
        CpuProcessState.Running => "Đang chạy",
        CpuProcessState.Terminated => "Hoàn tất",
        _ => state.ToString()
    };

    private static SimulationError InvalidInput(string message) => new(SimulationErrorCode.InvalidInput, message);
}

public sealed partial class CpuProcessVisual : ObservableObject
{
    public CpuProcessVisual(
        string id,
        string stateLabel,
        long remainingTime,
        int priority,
        double progressPercent,
        bool isRunning,
        long pulseVersion)
    {
        this.id = id;
        this.stateLabel = stateLabel;
        this.remainingTime = remainingTime;
        this.priority = priority;
        this.progressPercent = progressPercent;
        this.isRunning = isRunning;
        this.pulseVersion = pulseVersion;
    }

    [ObservableProperty]
    private string id;

    [ObservableProperty]
    private string stateLabel;

    [ObservableProperty]
    private long remainingTime;

    [ObservableProperty]
    private int priority;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private long pulseVersion;

    public string RemainingLabel => $"CPU còn lại: {RemainingTime}";

    public void Update(CpuProcessVisual next)
    {
        Id = next.Id;
        StateLabel = next.StateLabel;
        RemainingTime = next.RemainingTime;
        Priority = next.Priority;
        ProgressPercent = next.ProgressPercent;
        IsRunning = next.IsRunning;
        PulseVersion = next.PulseVersion;
    }

    partial void OnRemainingTimeChanged(long value) => OnPropertyChanged(nameof(RemainingLabel));
}

public sealed record GanttSegmentVisual(
    decimal Start,
    decimal End,
    string Label,
    double Width,
    bool IsIdle,
    bool IsContextSwitch,
    long PulseVersion);
