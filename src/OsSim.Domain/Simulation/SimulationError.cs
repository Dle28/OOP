namespace OsSim.Domain.Simulation;

public enum SimulationErrorCode
{
    InvalidInput,
    InvalidCommand,
    BoundaryReached,
    Deadlocked,
    InvariantViolation
}

public sealed record SimulationError(SimulationErrorCode Code, string Message);

public sealed record ValidationResult(IReadOnlyList<SimulationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Valid { get; } = new(Array.Empty<SimulationError>());

    public static ValidationResult Invalid(params SimulationError[] errors) => new(Array.AsReadOnly(errors));

    public static ValidationResult From(IEnumerable<SimulationError> errors)
    {
        var materialized = errors.ToArray();
        return materialized.Length == 0 ? Valid : new(Array.AsReadOnly(materialized));
    }
}
