using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsSim.Domain.Simulation;

namespace OsSim.Wpf.Playback;

public partial class PlaybackController<TSnapshot> : ObservableObject, IDisposable
    where TSnapshot : class
{
    private readonly DispatcherTimer _timer;
    private ISimulationSession<TSnapshot>? _session;
    private Func<bool, SimulationCommand?>? _commandFactory;
    private bool _synchronizingPosition;

    [ObservableProperty]
    private TSnapshot? currentSnapshot;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private int intervalMilliseconds = 1_800;

    [ObservableProperty]
    private int currentStepPosition;

    [ObservableProperty]
    private string statusMessage = "Tạo mô phỏng để bắt đầu.";

    public PlaybackController()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds) };
        _timer.Tick += OnTimerTick;
    }

    public event Action? SnapshotUpdated;

    public bool CanStepForward => _session?.CanStepForward ?? false;

    public bool CanStepBackward => _session?.CanStepBackward ?? false;

    public bool CanPlay => CanStepForward && !IsPlaying;

    public int? StepCount => _session?.StepCount;

    public int TimelineMaximum => Math.Max(1, StepCount ?? CurrentStepPosition);

    public bool CanSeek => StepCount is > 0;

    public string TimelineLabel => _session is null
        ? "Chưa có timeline"
        : StepCount is int totalSteps
            ? CurrentStepPosition == 0
                ? $"Bắt đầu / {totalSteps}"
                : $"Bước {CurrentStepPosition} / {totalSteps}"
            : CurrentStepPosition == 0
                ? "Sẵn sàng"
                : $"Bước {CurrentStepPosition} · luồng tương tác";

    public string IntervalLabel => $"{IntervalMilliseconds / 1000d:0.0} giây / bước";

    public void Attach(ISimulationSession<TSnapshot> session, Func<bool, SimulationCommand?>? commandFactory = null)
    {
        Pause();
        _session = session;
        _commandFactory = commandFactory;
        CurrentSnapshot = session.CurrentSnapshot;
        SynchronizePosition();
        StatusMessage = "Mô phỏng đã sẵn sàng.";
        RefreshState();
        SnapshotUpdated?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    partial void OnIntervalMillisecondsChanged(int value)
    {
        var safeValue = Math.Clamp(value, 500, 5_000);
        if (safeValue != value)
        {
            IntervalMilliseconds = safeValue;
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(safeValue);
        OnPropertyChanged(nameof(IntervalLabel));
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        PlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentStepPositionChanged(int value)
    {
        if (!_synchronizingPosition)
        {
            Seek(value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (_session is null || !_session.CanStepForward)
        {
            return;
        }

        StatusMessage = "Đang phát mô phỏng.";
        IsPlaying = true;
        _timer.Start();
    }

    [RelayCommand]
    private void Pause()
    {
        _timer.Stop();
        IsPlaying = false;
        if (_session is not null)
        {
            StatusMessage = "Đã tạm dừng mô phỏng.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStepForward))]
    private void StepForward()
    {
        Pause();
        Advance(automatic: false);
    }

    [RelayCommand(CanExecute = nameof(CanStepBackward))]
    private void StepBackward()
    {
        if (_session is null)
        {
            return;
        }

        Pause();
        var result = _session.StepBackward();
        Apply(result);
    }

    [RelayCommand]
    private void Reset()
    {
        if (_session is null)
        {
            return;
        }

        Pause();
        Apply(_session.Reset());
        StatusMessage = "Đã đặt lại mô phỏng.";
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        Advance(automatic: true);
    }

    private void Advance(bool automatic)
    {
        if (_session is null)
        {
            return;
        }

        var result = _session.StepForward(_commandFactory?.Invoke(automatic));
        Apply(result);
        if (!result.Succeeded || !_session.CanStepForward)
        {
            _timer.Stop();
            IsPlaying = false;
            if (result.Succeeded && result.IsCompleted)
            {
                StatusMessage = "Mô phỏng hoàn tất.";
            }
        }
    }

    private void Apply(SimulationStepResult<TSnapshot> result)
    {
        CurrentSnapshot = result.Snapshot;
        SynchronizePosition();
        if (result.Error is not null)
        {
            StatusMessage = result.Error.Message;
        }
        else if (result.IsCompleted)
        {
            StatusMessage = "Mô phỏng hoàn tất.";
        }
        else
        {
            StatusMessage = IsPlaying ? "Đang phát từng bước." : "Đang xem bước hiện tại.";
        }

        RefreshState();
        SnapshotUpdated?.Invoke();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(TimelineLabel));
        OnPropertyChanged(nameof(CanStepForward));
        OnPropertyChanged(nameof(CanStepBackward));
        OnPropertyChanged(nameof(CanPlay));
        StepForwardCommand.NotifyCanExecuteChanged();
        StepBackwardCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
    }

    private void Seek(int requestedPosition)
    {
        if (_session?.StepCount is not int stepCount)
        {
            SynchronizePosition();
            return;
        }

        var targetPosition = Math.Clamp(requestedPosition, 0, stepCount);
        if (targetPosition == _session.StepPosition)
        {
            SynchronizePosition();
            return;
        }

        Pause();
        SimulationStepResult<TSnapshot>? latestResult = null;
        while (_session.StepPosition < targetPosition && _session.CanStepForward)
        {
            latestResult = _session.StepForward(_commandFactory?.Invoke(true));
            if (!latestResult.Succeeded)
            {
                break;
            }
        }

        while (_session.StepPosition > targetPosition && _session.CanStepBackward)
        {
            latestResult = _session.StepBackward();
            if (!latestResult.Succeeded)
            {
                break;
            }
        }

        if (latestResult is not null)
        {
            Apply(latestResult);
        }
        else
        {
            SynchronizePosition();
        }
    }

    private void SynchronizePosition()
    {
        _synchronizingPosition = true;
        try
        {
            CurrentStepPosition = _session?.StepPosition ?? 0;
        }
        finally
        {
            _synchronizingPosition = false;
        }

        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(TimelineLabel));
    }
}
