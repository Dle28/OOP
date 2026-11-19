using OsSim.Domain.Simulation;

namespace OsSim.Domain.ResourceAllocation;

public enum BankerRunState
{
    Running,
    Safe,
    Unsafe
}

public enum BankerRequestDisposition
{
    Granted,
    WaitForResources,
    WouldBeUnsafe
}

public sealed record BankerProcessInput(
    string Id,
    IReadOnlyList<int> Allocation,
    IReadOnlyList<int> MaxDemand);

public sealed record BankerInput(
    IReadOnlyList<string> ResourceNames,
    IReadOnlyList<int> Available,
    IReadOnlyList<BankerProcessInput> Processes);

public sealed record BankerResourceRequest(
    string ProcessId,
    IReadOnlyList<int> Amounts);

public sealed record BankerProcessSnapshot(
    string Id,
    IReadOnlyList<int> Allocation,
    IReadOnlyList<int> MaxDemand,
    IReadOnlyList<int> Need,
    bool IsFinished);

public sealed record BankerSnapshot(
    int StepNumber,
    IReadOnlyList<string> ResourceNames,
    IReadOnlyList<int> Available,
    IReadOnlyList<int> Work,
    IReadOnlyList<BankerProcessSnapshot> Processes,
    IReadOnlyList<string> SafeSequence,
    BankerRunState State,
    string? LastCompletedProcessId,
    IReadOnlyList<SimulationEvent> Events,
    bool IsCompleted);

public sealed record BankerRequestResult(
    bool Succeeded,
    BankerRequestDisposition? Disposition,
    IReadOnlyList<string> SafeSequence,
    IReadOnlyList<int> AvailableAfterEvaluation,
    IReadOnlyList<SimulationError> Errors,
    string Message);
