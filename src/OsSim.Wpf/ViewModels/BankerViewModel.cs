using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsSim.Application;
using OsSim.Domain.ResourceAllocation;
using OsSim.Domain.Simulation;
using OsSim.Wpf.Playback;

namespace OsSim.Wpf.ViewModels;

public partial class BankerViewModel : ObservableObject, ISimulationAreaViewModel
{
    private readonly SimulationCatalog _catalog;

    [ObservableProperty]
    private string resourceNamesInput = "A, B, C, D";

    [ObservableProperty]
    private string availableInput = "1, 5, 2, 0";

    [ObservableProperty]
    private string processIdsInput = "P0, P1, P2, P3, P4";

    [ObservableProperty]
    private string allocationMatrixInput = "0,0,1,2\n1,0,0,0\n1,3,5,4\n0,6,3,2\n0,0,1,4";

    [ObservableProperty]
    private string maxDemandMatrixInput = "0,0,1,2\n1,7,5,0\n2,3,5,6\n0,6,5,2\n0,6,5,6";

    [ObservableProperty]
    private string requestProcessId = "P1";

    [ObservableProperty]
    private string requestAmountsInput = "0, 0, 2, 0";

    [ObservableProperty]
    private string statusText = "Nhập ma trận tài nguyên rồi tạo mô phỏng Banker.";

    [ObservableProperty]
    private string requestStatus = "Request được kiểm tra trên bản sao trạng thái đầu vào; kết quả không sửa phiên playback.";

    [ObservableProperty]
    private string requestSafeSequence = string.Empty;

    public BankerViewModel(SimulationCatalog catalog)
    {
        _catalog = catalog;
        Playback = new PlaybackController<BankerSnapshot>();
        Playback.SnapshotUpdated += OnSnapshotUpdated;
        CreateSimulation();
    }

    public SimulationArea Area => SimulationArea.ResourceAllocation;

    public PlaybackController<BankerSnapshot> Playback { get; }

    public BankerSnapshot? Snapshot => Playback.CurrentSnapshot;

    public SimulationActionVisual CurrentAction =>
        SimulationActionNarrator.Describe(Area, Snapshot?.Events.LastOrDefault());

    public IReadOnlyList<MetricCardVisual> MetricCards
    {
        get
        {
            var snapshot = Snapshot;
            var safe = snapshot?.State == BankerRunState.Safe;
            var unsafeState = snapshot?.State == BankerRunState.Unsafe;
            var state = snapshot?.State switch
            {
                BankerRunState.Safe => "SAFE STATE",
                BankerRunState.Unsafe => "UNSAFE STATE",
                BankerRunState.Running => "ĐANG PHÂN TÍCH",
                _ => "CHƯA TẠO"
            };
            var sequence = snapshot?.SafeSequence.Count > 0
                ? string.Join(" → ", snapshot.SafeSequence)
                : "Chưa có thứ tự an toàn";
            var work = snapshot is null ? "—" : FormatVector(snapshot.ResourceNames, snapshot.Work);
            return
            [
                new MetricCardVisual("TRẠNG THÁI HỆ THỐNG", state,
                    snapshot is null ? "Chạy bước Banker để kiểm tra" : "Theo snapshot hiện tại",
                    safe ? MetricTone.Success : unsafeState ? MetricTone.Danger : MetricTone.Primary),
                new MetricCardVisual("SAFE SEQUENCE", sequence,
                    snapshot?.State == BankerRunState.Unsafe ? "Không thể hoàn tất mọi tiến trình" : "Thứ tự tiến trình có thể hoàn tất",
                    safe ? MetricTone.Success : MetricTone.Primary),
                new MetricCardVisual("WORK HIỆN TẠI", work,
                    snapshot is null ? "Available ban đầu" : $"Bước {snapshot.StepNumber} · Available: {FormatVector(snapshot.ResourceNames, snapshot.Available)}",
                    MetricTone.Primary)
            ];
        }
    }

    public IReadOnlyList<BankerProcessVisual> ProcessRows => Snapshot?.Processes
        .Select(process => new BankerProcessVisual(
            process.Id,
            FormatVector(Snapshot!.ResourceNames, process.Allocation),
            FormatVector(Snapshot!.ResourceNames, process.MaxDemand),
            FormatVector(Snapshot!.ResourceNames, process.Need),
            process.IsFinished ? "DONE" : "—",
            process.IsFinished,
            process.Id == Snapshot!.LastCompletedProcessId ? Snapshot.StepNumber : -1))
        .ToArray() ?? Array.Empty<BankerProcessVisual>();

    public BankerRagGraphVisual ResourceGraph => BankerRagProjector.Project(Snapshot);

    public IReadOnlyList<SimulationEvent> Events => Snapshot?.Events.Reverse().ToArray() ?? Array.Empty<SimulationEvent>();

    [RelayCommand]
    private void CreateSimulation()
    {
        if (!TryBuildInput(out var input, out var error))
        {
            StatusText = error!.Message;
            return;
        }

        var result = _catalog.CreateBankerSession(input!);
        if (!result.Succeeded)
        {
            StatusText = string.Join(Environment.NewLine, result.Errors.Select(item => item.Message));
            return;
        }

        Playback.Attach(result.Session!);
        RequestStatus = "Request test dùng trạng thái đầu vào và không thay đổi phiên playback.";
        RequestSafeSequence = string.Empty;
        StatusText = $"Đã tạo mô phỏng Banker với {input!.Processes.Count} tiến trình và {input.ResourceNames.Count} loại tài nguyên.";
        NotifySnapshotProperties();
    }

    [RelayCommand]
    private void TestRequest()
    {
        if (!TryBuildInput(out var input, out var error))
        {
            RequestStatus = error!.Message;
            return;
        }

        if (!TryParseVector(RequestAmountsInput, input!.ResourceNames.Count, "Request", out var amounts, out error))
        {
            RequestStatus = error!.Message;
            return;
        }

        var result = _catalog.TestBankerRequest(input!, new BankerResourceRequest(RequestProcessId, amounts!));
        RequestStatus = result.Succeeded
            ? result.Message
            : string.Join(Environment.NewLine, result.Errors.Select(item => item.Message));
        RequestSafeSequence = result.SafeSequence.Count == 0
            ? string.Empty
            : $"Safe sequence sau cấp phát thử: {string.Join(" → ", result.SafeSequence)}";
    }

    private bool TryBuildInput(out BankerInput? input, out SimulationError? error)
    {
        var resourceNames = SplitTokens(ResourceNamesInput);
        if (resourceNames.Length == 0)
        {
            input = null;
            error = Invalid("Nhập ít nhất một tên tài nguyên.");
            return false;
        }

        if (!TryParseVector(AvailableInput, resourceNames.Length, "Available", out var available, out error))
        {
            input = null;
            return false;
        }

        var processIds = SplitTokens(ProcessIdsInput);
        if (!TryParseMatrix(AllocationMatrixInput, resourceNames.Length, "Allocation", out var allocation, out error) ||
            !TryParseMatrix(MaxDemandMatrixInput, resourceNames.Length, "Max", out var maxDemand, out error))
        {
            input = null;
            return false;
        }

        if (processIds.Length != allocation!.Length || processIds.Length != maxDemand!.Length)
        {
            input = null;
            error = Invalid("Số mã tiến trình phải bằng số dòng của cả hai ma trận.");
            return false;
        }

        var processes = processIds.Select((id, index) => new BankerProcessInput(
            id,
            Array.AsReadOnly(allocation[index]),
            Array.AsReadOnly(maxDemand[index]))).ToArray();
        input = new BankerInput(
            Array.AsReadOnly(resourceNames),
            Array.AsReadOnly(available!),
            Array.AsReadOnly(processes));
        error = null;
        return true;
    }

    private static bool TryParseMatrix(
        string input,
        int resourceCount,
        string label,
        out int[][]? rows,
        out SimulationError? error)
    {
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            rows = null;
            error = Invalid($"Ma trận {label} cần ít nhất một dòng.");
            return false;
        }

        var parsed = new int[lines.Length][];
        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryParseVector(lines[index], resourceCount, $"{label}, dòng {index + 1}", out var row, out error))
            {
                rows = null;
                return false;
            }

            parsed[index] = row!;
        }

        rows = parsed;
        error = null;
        return true;
    }

    private static bool TryParseVector(
        string input,
        int expectedCount,
        string label,
        out int[]? values,
        out SimulationError? error)
    {
        var tokens = SplitTokens(input);
        if (tokens.Length != expectedCount)
        {
            values = null;
            error = Invalid($"{label} cần đúng {expectedCount} giá trị.");
            return false;
        }

        var parsed = new int[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[index]))
            {
                values = null;
                error = Invalid($"'{tokens[index]}' trong {label} không phải số nguyên.");
                return false;
            }
        }

        values = parsed;
        error = null;
        return true;
    }

    private static string[] SplitTokens(string input) => input.Split(
        [',', ';', ' ', '\t'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void OnSnapshotUpdated() => NotifySnapshotProperties();

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(MetricCards));
        OnPropertyChanged(nameof(ProcessRows));
        OnPropertyChanged(nameof(ResourceGraph));
        OnPropertyChanged(nameof(Events));
    }

    private static string FormatVector(IReadOnlyList<string> resourceNames, IReadOnlyList<int> values) =>
        string.Join("   ", values.Select((value, index) => $"{resourceNames[index]}:{value}"));

    private static SimulationError Invalid(string message) => new(SimulationErrorCode.InvalidInput, message);
}

public sealed record BankerProcessVisual(
    string Id,
    string AllocationText,
    string MaxText,
    string NeedText,
    string FinishLabel,
    bool IsFinished,
    long PulseVersion);

