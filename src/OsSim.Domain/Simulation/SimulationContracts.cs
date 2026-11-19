namespace OsSim.Domain.Simulation;

public sealed record SimulationEvent(
    long Sequence,
    decimal VirtualTime,
    string Code,
    string Description,
    string? ActorId = null);

public sealed record SimulationCommand(string? ActorId = null, bool AutomaticDispatch = true);

public sealed record SimulationCreationResult<TSnapshot>(
    ISimulationSession<TSnapshot>? Session,
    IReadOnlyList<SimulationError> Errors)
    where TSnapshot : class
{
    public bool Succeeded => Session is not null && Errors.Count == 0;

    public static SimulationCreationResult<TSnapshot> Created(ISimulationSession<TSnapshot> session) =>
        new(session, Array.Empty<SimulationError>());

    public static SimulationCreationResult<TSnapshot> Failed(IEnumerable<SimulationError> errors) =>
        new(null, Array.AsReadOnly(errors.ToArray()));
}

public sealed record SimulationStepResult<TSnapshot>(
    bool Succeeded,
    bool IsCompleted,
    TSnapshot Snapshot,
    SimulationEvent? Event,
    SimulationError? Error)
    where TSnapshot : class;

public interface ISimulationModule<TInput, TSnapshot>
    where TSnapshot : class
{
    ValidationResult Validate(TInput input);

    SimulationCreationResult<TSnapshot> CreateSession(TInput input);
}

public interface ISimulationSession<TSnapshot>
    where TSnapshot : class
{
    TSnapshot CurrentSnapshot { get; }

    /// <summary>Number of successful transitions already applied; zero is the initial state.</summary>
    int StepPosition { get; }

    /// <summary>Total transitions for a fixed trace, or null when the trace grows interactively.</summary>
    int? StepCount { get; }

    bool CanStepForward { get; }

    bool CanStepBackward { get; }

    SimulationStepResult<TSnapshot> StepForward(SimulationCommand? command = null);

    SimulationStepResult<TSnapshot> StepBackward();

    SimulationStepResult<TSnapshot> Reset();
}

public sealed record SimulationFrame<TSnapshot>(TSnapshot Snapshot, SimulationEvent Event)
    where TSnapshot : class;
