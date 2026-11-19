namespace OsSim.Domain.Simulation;

public sealed record SimulationTransition<TSnapshot>(
    bool Succeeded,
    TSnapshot Snapshot,
    SimulationEvent? Event,
    SimulationError? Error)
    where TSnapshot : class;

public sealed class ReplayableSimulationSession<TSnapshot> : ISimulationSession<TSnapshot>
    where TSnapshot : class
{
    private readonly TSnapshot _initialSnapshot;
    private readonly Func<TSnapshot, SimulationCommand?, SimulationTransition<TSnapshot>> _advance;
    private readonly Func<TSnapshot, bool> _canContinue;
    private readonly Func<TSnapshot, bool> _isCompleted;
    private readonly Func<TSnapshot, SimulationError?> _terminalError;
    private readonly List<SimulationTransition<TSnapshot>> _history = [];
    private int _cursor;

    public ReplayableSimulationSession(
        TSnapshot initialSnapshot,
        Func<TSnapshot, SimulationCommand?, SimulationTransition<TSnapshot>> advance,
        Func<TSnapshot, bool> canContinue,
        Func<TSnapshot, SimulationError?>? terminalError = null,
        Func<TSnapshot, bool>? isCompleted = null)
    {
        _initialSnapshot = initialSnapshot;
        _advance = advance;
        _canContinue = canContinue;
        _isCompleted = isCompleted ?? (_ => false);
        _terminalError = terminalError ?? (_ => new SimulationError(SimulationErrorCode.BoundaryReached, "Mô phỏng không còn bước tiếp theo."));
    }

    public TSnapshot CurrentSnapshot => _cursor == 0 ? _initialSnapshot : _history[_cursor - 1].Snapshot;

    public int StepPosition => _cursor;

    public int? StepCount => null;

    public bool CanStepForward => _canContinue(CurrentSnapshot);

    public bool CanStepBackward => _cursor > 0;

    public SimulationStepResult<TSnapshot> StepForward(SimulationCommand? command = null)
    {
        if (!CanStepForward)
        {
            return new(false, true, CurrentSnapshot, null, _terminalError(CurrentSnapshot));
        }

        var transition = _advance(CurrentSnapshot, command);
        if (!transition.Succeeded)
        {
            return new(false, false, CurrentSnapshot, null, transition.Error);
        }

        if (_cursor < _history.Count)
        {
            _history.RemoveRange(_cursor, _history.Count - _cursor);
        }

        _history.Add(transition);
        _cursor++;
        return new(true, _isCompleted(CurrentSnapshot), CurrentSnapshot, transition.Event, null);
    }

    public SimulationStepResult<TSnapshot> StepBackward()
    {
        if (!CanStepBackward)
        {
            return new(false, false, CurrentSnapshot, null,
                new SimulationError(SimulationErrorCode.BoundaryReached, "Mô phỏng đang ở trạng thái ban đầu."));
        }

        _cursor--;
        return new(true, _isCompleted(CurrentSnapshot), CurrentSnapshot, null, null);
    }

    public SimulationStepResult<TSnapshot> Reset()
    {
        _cursor = 0;
        _history.Clear();
        return new(true, _isCompleted(CurrentSnapshot), CurrentSnapshot, null, null);
    }
}
