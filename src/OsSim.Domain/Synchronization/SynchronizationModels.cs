using OsSim.Domain.Simulation;

namespace OsSim.Domain.Synchronization;

public enum SynchronizationScenario
{
    ProducerConsumer,
    DiningPhilosophers
}

public enum DiningPhilosophersMode
{
    NaiveLeftFirst,
    ResourceOrdering
}

public enum VirtualActorState
{
    Ready,
    Blocked,
    Terminated
}

public sealed record SynchronizationInput(
    SynchronizationScenario Scenario,
    int BufferCapacity = 3,
    int ProducerCount = 1,
    int ConsumerCount = 1,
    int ItemsPerProducer = 3,
    int ItemsPerConsumer = 3,
    int PhilosopherCount = 5,
    int MealsPerPhilosopher = 2,
    DiningPhilosophersMode DiningMode = DiningPhilosophersMode.NaiveLeftFirst);

public sealed record VirtualActorSnapshot(
    string Id,
    string Role,
    VirtualActorState State,
    int ProgramCounter,
    int CompletedWork,
    string? WaitingFor,
    IReadOnlyList<string> HeldResources);

public sealed record SynchronizationResourceSnapshot(
    string Id,
    int Count,
    string? OwnerActorId,
    IReadOnlyList<string> WaitQueue);

public sealed record WaitForEdge(string WaiterActorId, string ResourceId, string? OwnerActorId);

public sealed record SynchronizationSnapshot(
    long StepNumber,
    IReadOnlyList<VirtualActorSnapshot> Actors,
    IReadOnlyList<SynchronizationResourceSnapshot> Resources,
    IReadOnlyList<string> Buffer,
    IReadOnlyList<SimulationEvent> Events,
    IReadOnlyList<WaitForEdge> WaitForGraph,
    IReadOnlyList<IReadOnlyList<string>> DeadlockCycles,
    int NextAutomaticActorIndex,
    bool IsCompleted,
    bool IsDeadlocked)
{
    public string? LastDispatchedActorId { get; init; }

    public IReadOnlyList<string> ReadyActorIds => Array.AsReadOnly(
        Actors.Where(actor => actor.State == VirtualActorState.Ready).Select(actor => actor.Id).ToArray());
}
