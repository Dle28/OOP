namespace OsSim.Domain.Simulation;

public sealed class TimelineSimulationSession<TSnapshot> : ISimulationSession<TSnapshot>
    where TSnapshot : class
{
    private readonly TSnapshot _initialSnapshot;
    private readonly IReadOnlyList<SimulationFrame<TSnapshot>> _frames;
    private int _cursor;

    public TimelineSimulationSession(TSnapshot initialSnapshot, IReadOnlyList<SimulationFrame<TSnapshot>> frames)
    {
        _initialSnapshot = initialSnapshot;
        _frames = frames;
    }

    public TSnapshot CurrentSnapshot => _cursor == 0 ? _initialSnapshot : _frames[_cursor - 1].Snapshot;

    public int StepPosition => _cursor;

    public int? StepCount => _frames.Count;

    public bool CanStepForward => _cursor < _frames.Count;

    public bool CanStepBackward => _cursor > 0;

    public SimulationStepResult<TSnapshot> StepForward(SimulationCommand? command = null)
    {
        if (!CanStepForward)
        {
            return Failed(SimulationErrorCode.BoundaryReached, "Mô phỏng đang ở bước cuối.");
        }

        var frame = _frames[_cursor++];
        return new(true, !CanStepForward, frame.Snapshot, frame.Event, null);
    }

    public SimulationStepResult<TSnapshot> StepBackward()
    {
        if (!CanStepBackward)
        {
            return Failed(SimulationErrorCode.BoundaryReached, "Mô phỏng đang ở trạng thái ban đầu.");
        }

        _cursor--;
        return new(true, false, CurrentSnapshot, null, null);
    }

    public SimulationStepResult<TSnapshot> Reset()
    {
        _cursor = 0;
        return new(true, _frames.Count == 0, CurrentSnapshot, null, null);
    }

    private SimulationStepResult<TSnapshot> Failed(SimulationErrorCode code, string message) =>
        new(false, !CanStepForward, CurrentSnapshot, null, new SimulationError(code, message));
}
