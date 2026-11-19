using OsSim.Domain.Simulation;

namespace OsSim.Domain.Cpu;

public sealed class CpuSchedulingModule : ISimulationModule<CpuSchedulingInput, CpuSimulationSnapshot>
{
    private const int MaxProcessCount = 50;
    private const long MaxTotalBurst = 10_000;
    private const long MaxArrivalTime = 1_000_000;
    private readonly IReadOnlyDictionary<CpuAlgorithm, ICpuSchedulingPolicy> _policies;

    public CpuSchedulingModule()
    {
        _policies = new Dictionary<CpuAlgorithm, ICpuSchedulingPolicy>
        {
            [CpuAlgorithm.Fcfs] = new FcfsSchedulingPolicy(),
            [CpuAlgorithm.Sjf] = new SjfSchedulingPolicy(),
            [CpuAlgorithm.Srtf] = new SrtfSchedulingPolicy(),
            [CpuAlgorithm.Priority] = new PrioritySchedulingPolicy(preemptive: false),
            [CpuAlgorithm.PreemptivePriority] = new PrioritySchedulingPolicy(preemptive: true),
            [CpuAlgorithm.RoundRobin] = new RoundRobinSchedulingPolicy()
        };
    }

    public ValidationResult Validate(CpuSchedulingInput input)
    {
        var errors = new List<SimulationError>();
        if (input.Processes is null || input.Processes.Count == 0)
        {
            errors.Add(Invalid("Cần nhập ít nhất một tiến trình."));
            return ValidationResult.From(errors);
        }

        if (input.Processes.Count > MaxProcessCount)
        {
            errors.Add(Invalid($"Tối đa {MaxProcessCount} tiến trình được hỗ trợ."));
        }

        if (!_policies.ContainsKey(input.Algorithm))
        {
            errors.Add(new SimulationError(SimulationErrorCode.InvalidInput, "Hãy chọn thuật toán lập lịch CPU được hỗ trợ."));
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBurst = 0;
        foreach (var process in input.Processes)
        {
            if (string.IsNullOrWhiteSpace(process.Id))
            {
                errors.Add(Invalid("Mỗi tiến trình cần có mã không rỗng."));
            }
            else if (!ids.Add(process.Id.Trim()))
            {
                errors.Add(Invalid($"Mã tiến trình '{process.Id}' bị trùng."));
            }

            if (process.ArrivalTime < 0)
            {
                errors.Add(Invalid($"Tiến trình '{process.Id}' có thời điểm đến âm."));
            }
            else if (process.ArrivalTime > MaxArrivalTime)
            {
                errors.Add(Invalid($"Thời điểm đến của '{process.Id}' không được vượt quá {MaxArrivalTime}."));
            }

            if (process.BurstTime <= 0)
            {
                errors.Add(Invalid($"CPU burst của '{process.Id}' phải lớn hơn 0."));
            }

            if (process.Priority < 0)
            {
                errors.Add(Invalid($"Độ ưu tiên của '{process.Id}' không được âm."));
            }

            if (process.BurstTime > 0)
            {
                totalBurst = process.BurstTime > MaxTotalBurst || totalBurst > MaxTotalBurst - process.BurstTime
                    ? MaxTotalBurst + 1
                    : totalBurst + process.BurstTime;
            }
        }

        if (totalBurst > MaxTotalBurst)
        {
            errors.Add(Invalid($"Tổng CPU burst không được vượt quá {MaxTotalBurst} đơn vị thời gian."));
        }

        if (input.Algorithm == CpuAlgorithm.RoundRobin && input.TimeQuantum <= 0)
        {
            errors.Add(Invalid("Round Robin cần quantum lớn hơn 0."));
        }

        if (input.ContextSwitchOverhead < 0m || input.ContextSwitchOverhead > 1_000m)
        {
            errors.Add(Invalid("Chi phí chuyển ngữ cảnh phải nằm trong khoảng 0–1.000 đơn vị thời gian."));
        }

        return ValidationResult.From(errors);
    }

    public SimulationCreationResult<CpuSimulationSnapshot> CreateSession(CpuSchedulingInput input)
    {
        var validation = Validate(input);
        if (!validation.IsValid)
        {
            return SimulationCreationResult<CpuSimulationSnapshot>.Failed(validation.Errors);
        }

        var trace = BuildTrace(input, _policies[input.Algorithm]);
        return SimulationCreationResult<CpuSimulationSnapshot>.Created(
            new TimelineSimulationSession<CpuSimulationSnapshot>(trace.Initial, trace.Frames));
    }

    private static (CpuSimulationSnapshot Initial, IReadOnlyList<SimulationFrame<CpuSimulationSnapshot>> Frames) BuildTrace(
        CpuSchedulingInput input,
        ICpuSchedulingPolicy policy)
    {
        var states = input.Processes.Select((p, index) => new MutableProcess
        {
            Id = p.Id.Trim(),
            ArrivalTime = p.ArrivalTime,
            BurstTime = p.BurstTime,
            RemainingTime = p.BurstTime,
            Priority = p.Priority,
            InputOrder = index
        }).ToList();
        var ready = new List<MutableProcess>();
        var gantt = new List<GanttSegment>();
        var events = new List<SimulationEvent>();
        var frames = new List<SimulationFrame<CpuSimulationSnapshot>>();
        var now = 0m;
        var running = (MutableProcess?)null;
        var quantumUsed = 0L;
        var previousProcessId = (string?)null;
        var contextSwitchCount = 0;
        var contextSwitchTime = 0m;
        var initial = CreateSnapshot(now, states, ready, running, gantt, events,
            contextSwitchCount, contextSwitchTime, dispatchingProcessId: null);

        while (states.Any(p => p.State != CpuProcessState.Terminated))
        {
            AdmitArrivals(now, states, ready, events);

            if (policy.UsesRoundRobinQueue && running is not null && quantumUsed >= input.TimeQuantum)
            {
                previousProcessId = running.Id;
                running.State = CpuProcessState.Ready;
                ready.Add(running);
                events.Add(new SimulationEvent(events.Count + 1, now, "QUANTUM_EXPIRED", $"Tiến trình {running.Id} đã hết quantum.", running.Id));
                running = null;
                quantumUsed = 0;
            }

            if (running is not null && policy.IsPreemptive && ready.Count > 0)
            {
                var candidate = policy.SelectNext(ready.Select(ToCandidate).ToArray());
                if (policy.ShouldPreempt(ToCandidate(running), candidate))
                {
                    previousProcessId = running.Id;
                    running.State = CpuProcessState.Ready;
                    ready.Add(running);
                    events.Add(new SimulationEvent(events.Count + 1, now, "PREEMPT", $"Tiến trình {running.Id} bị ngắt để nhường CPU cho {candidate.Id}.", running.Id));
                    running = null;
                }
            }

            if (running is null && ready.Count == 0)
            {
                var nextArrival = states.Where(p => p.State == CpuProcessState.New).Min(p => p.ArrivalTime);
                if (nextArrival > now)
                {
                    AddGantt(gantt, null, now, nextArrival, GanttSegmentKind.Idle);
                    events.Add(new SimulationEvent(events.Count + 1, nextArrival, "CPU_IDLE", $"CPU rỗi từ {now} đến {nextArrival}."));
                    now = nextArrival;
                    previousProcessId = null;
                    frames.Add(new SimulationFrame<CpuSimulationSnapshot>(
                        CreateSnapshot(now, states, ready, running, gantt, events,
                            contextSwitchCount, contextSwitchTime, dispatchingProcessId: null), events[^1]));
                }

                AdmitArrivals(now, states, ready, events);
            }

            if (running is null)
            {
                if (ready.Count == 0)
                {
                    continue;
                }

                var selectedCandidate = policy.SelectNext(ready.Select(ToCandidate).ToArray());
                var selected = ready.Single(p => p.Id == selectedCandidate.Id);
                ready.Remove(selected);

                if (previousProcessId is not null && !StringComparer.Ordinal.Equals(previousProcessId, selected.Id))
                {
                    contextSwitchCount++;
                    if (input.ContextSwitchOverhead > 0m)
                    {
                        selected.State = CpuProcessState.ContextSwitching;
                        var switchStart = now;
                        now += input.ContextSwitchOverhead;
                        contextSwitchTime += input.ContextSwitchOverhead;
                        AddGantt(gantt, null, switchStart, now, GanttSegmentKind.ContextSwitch);
                        var switchEvent = new SimulationEvent(
                            events.Count + 1,
                            now,
                            "CONTEXT_SWITCH",
                            $"Chuyển ngữ cảnh từ {previousProcessId} sang {selected.Id} trong khoảng [{switchStart}, {now}).",
                            selected.Id);
                        events.Add(switchEvent);

                        // The process was selected when the switch began. Arrivals during
                        // the overhead are admitted at its end and compete at the next decision.
                        AdmitArrivals(now, states, ready, events);
                        frames.Add(new SimulationFrame<CpuSimulationSnapshot>(
                            CreateSnapshot(now, states, ready, running, gantt, events,
                                contextSwitchCount, contextSwitchTime, selected.Id),
                            switchEvent));
                    }
                    else
                    {
                        events.Add(new SimulationEvent(
                            events.Count + 1,
                            now,
                            "CONTEXT_SWITCH",
                            $"Chuyển ngữ cảnh từ {previousProcessId} sang {selected.Id}; chi phí bằng 0.",
                            selected.Id));
                    }
                }

                running = selected;
                running.State = CpuProcessState.Running;
                if (running.FirstStartTime is null)
                {
                    running.FirstStartTime = now;
                }

                events.Add(new SimulationEvent(events.Count + 1, now, "DISPATCH", $"Tiến trình {running.Id} được cấp CPU.", running.Id));
            }

            var start = now;
            running.RemainingTime--;
            now += 1m;
            quantumUsed++;
            AddGantt(gantt, running.Id, start, now, GanttSegmentKind.Execution);

            if (running.RemainingTime == 0)
            {
                previousProcessId = running.Id;
                running.State = CpuProcessState.Terminated;
                running.CompletionTime = now;
                events.Add(new SimulationEvent(events.Count + 1, now, "COMPLETED", $"Tiến trình {running.Id} hoàn thành tại thời điểm {now}.", running.Id));
                running = null;
                quantumUsed = 0;
            }
            else
            {
                events.Add(new SimulationEvent(events.Count + 1, start, "CPU_TICK", $"CPU chạy tiến trình {running.Id} trong khoảng [{start}, {now}).", running.Id));
            }

            var eventForFrame = events[^1];
            frames.Add(new SimulationFrame<CpuSimulationSnapshot>(
                CreateSnapshot(now, states, ready, running, gantt, events,
                    contextSwitchCount, contextSwitchTime, dispatchingProcessId: null), eventForFrame));
        }

        return (initial, Array.AsReadOnly(frames.ToArray()));
    }

    private static void AdmitArrivals(decimal now, IReadOnlyList<MutableProcess> states, ICollection<MutableProcess> ready, ICollection<SimulationEvent> events)
    {
        foreach (var process in states.Where(p => p.State == CpuProcessState.New && p.ArrivalTime <= now).OrderBy(p => p.ArrivalTime).ThenBy(p => p.InputOrder))
        {
            process.State = CpuProcessState.Ready;
            ready.Add(process);
            events.Add(new SimulationEvent(
                events.Count + 1,
                process.ArrivalTime,
                "ARRIVAL",
                $"Tiến trình {process.Id} đến tại {process.ArrivalTime} và vào hàng đợi sẵn sàng.",
                process.Id));
        }
    }

    private static CpuProcessCandidate ToCandidate(MutableProcess process) => new(
        process.Id, process.ArrivalTime, process.BurstTime, process.RemainingTime, process.Priority, process.InputOrder);

    private static CpuSimulationSnapshot CreateSnapshot(
        decimal now,
        IReadOnlyList<MutableProcess> processes,
        IReadOnlyList<MutableProcess> ready,
        MutableProcess? running,
        IReadOnlyList<GanttSegment> gantt,
        IReadOnlyList<SimulationEvent> events,
        int contextSwitchCount,
        decimal contextSwitchTime,
        string? dispatchingProcessId)
    {
        var snapshots = processes.Select(p => new CpuProcessSnapshot(
            p.Id, p.ArrivalTime, p.BurstTime, p.RemainingTime, p.Priority, p.State,
            p.FirstStartTime, p.CompletionTime, p.InputOrder)).ToArray();
        var completed = processes.All(p => p.State == CpuProcessState.Terminated);
        var metrics = completed
            ? CalculateMetrics(processes, gantt, contextSwitchCount, contextSwitchTime)
            : null;
        return new CpuSimulationSnapshot(
            now,
            Array.AsReadOnly(snapshots),
            Array.AsReadOnly(ready.Select(p => p.Id).ToArray()),
            running?.Id,
            Array.AsReadOnly(gantt.ToArray()),
            Array.AsReadOnly(events.ToArray()),
            completed,
            metrics,
            contextSwitchCount,
            contextSwitchTime,
            dispatchingProcessId);
    }

    private static CpuMetrics CalculateMetrics(
        IReadOnlyList<MutableProcess> processes,
        IReadOnlyList<GanttSegment> gantt,
        int contextSwitchCount,
        decimal contextSwitchTime)
    {
        var values = processes.Select(p => new CpuProcessMetrics(
            p.Id,
            p.CompletionTime!.Value,
            p.CompletionTime.Value - p.ArrivalTime,
            p.CompletionTime.Value - p.ArrivalTime - p.BurstTime,
            p.FirstStartTime!.Value - p.ArrivalTime)).ToArray();
        var elapsed = gantt.Count == 0 ? 0 : gantt.Max(s => s.End) - gantt.Min(s => s.Start);
        var busy = gantt.Where(s => !s.IsIdle).Sum(s => s.Duration);
        return new CpuMetrics(
            Array.AsReadOnly(values),
            values.Average(m => (double)m.TurnaroundTime),
            values.Average(m => (double)m.WaitingTime),
            values.Average(m => (double)m.ResponseTime),
            elapsed == 0m ? 0 : (double)(busy * 100m / elapsed),
            contextSwitchCount,
            contextSwitchTime);
    }

    private static void AddGantt(
        IList<GanttSegment> gantt,
        string? processId,
        decimal start,
        decimal end,
        GanttSegmentKind kind)
    {
        if (gantt.Count > 0 && gantt[^1].ProcessId == processId && gantt[^1].Kind == kind && gantt[^1].End == start)
        {
            gantt[^1] = gantt[^1] with { End = end };
        }
        else
        {
            gantt.Add(new GanttSegment(processId, start, end) { Kind = kind });
        }
    }

    private static SimulationError Invalid(string message) => new(SimulationErrorCode.InvalidInput, message);

    private sealed class MutableProcess
    {
        public required string Id { get; init; }
        public required long ArrivalTime { get; init; }
        public required long BurstTime { get; init; }
        public required long RemainingTime { get; set; }
        public required int Priority { get; init; }
        public required int InputOrder { get; init; }
        public CpuProcessState State { get; set; } = CpuProcessState.New;
        public decimal? FirstStartTime { get; set; }
        public decimal? CompletionTime { get; set; }
    }
}
