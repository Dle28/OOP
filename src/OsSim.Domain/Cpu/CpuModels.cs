using OsSim.Domain.Simulation;

namespace OsSim.Domain.Cpu;

public enum CpuAlgorithm
{
    Fcfs,
    Sjf,
    Srtf,
    Priority,
    PreemptivePriority,
    RoundRobin
}

public enum CpuProcessState
{
    New,
    Ready,
    ContextSwitching,
    Running,
    Terminated
}

public enum GanttSegmentKind
{
    Execution,
    Idle,
    ContextSwitch
}

public sealed record CpuProcessInput(string Id, long ArrivalTime, long BurstTime, int Priority = 0);

public sealed record CpuSchedulingInput(
    IReadOnlyList<CpuProcessInput> Processes,
    CpuAlgorithm Algorithm,
    long TimeQuantum = 2,
    decimal ContextSwitchOverhead = 0m);

public sealed record CpuProcessCandidate(
    string Id,
    long ArrivalTime,
    long BurstTime,
    long RemainingTime,
    int Priority,
    int InputOrder);

public sealed record CpuProcessSnapshot(
    string Id,
    long ArrivalTime,
    long BurstTime,
    long RemainingTime,
    int Priority,
    CpuProcessState State,
    decimal? FirstStartTime,
    decimal? CompletionTime,
    int InputOrder);

public sealed record GanttSegment(string? ProcessId, decimal Start, decimal End)
{
    public GanttSegmentKind Kind { get; init; } = ProcessId is null
        ? GanttSegmentKind.Idle
        : GanttSegmentKind.Execution;

    public decimal Duration => End - Start;

    public bool IsIdle => Kind == GanttSegmentKind.Idle;

    public bool IsContextSwitch => Kind == GanttSegmentKind.ContextSwitch;
}

public sealed record CpuProcessMetrics(
    string ProcessId,
    decimal CompletionTime,
    decimal TurnaroundTime,
    decimal WaitingTime,
    decimal ResponseTime);

public sealed record CpuMetrics(
    IReadOnlyList<CpuProcessMetrics> Processes,
    double AverageTurnaroundTime,
    double AverageWaitingTime,
    double AverageResponseTime,
    double CpuUtilizationPercent,
    int ContextSwitchCount = 0,
    decimal ContextSwitchTime = 0m);

public sealed record CpuSimulationSnapshot(
    decimal Time,
    IReadOnlyList<CpuProcessSnapshot> Processes,
    IReadOnlyList<string> ReadyQueue,
    string? RunningProcessId,
    IReadOnlyList<GanttSegment> GanttSegments,
    IReadOnlyList<SimulationEvent> Events,
    bool IsCompleted,
    CpuMetrics? Metrics,
    int ContextSwitchCount = 0,
    decimal ContextSwitchTime = 0m,
    string? DispatchingProcessId = null);
