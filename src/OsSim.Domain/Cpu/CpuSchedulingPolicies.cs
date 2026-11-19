namespace OsSim.Domain.Cpu;

public abstract class CpuSchedulingPolicy : ICpuSchedulingPolicy
{
    protected CpuSchedulingPolicy(CpuAlgorithm algorithm, bool isPreemptive, bool usesRoundRobinQueue = false)
    {
        Algorithm = algorithm;
        IsPreemptive = isPreemptive;
        UsesRoundRobinQueue = usesRoundRobinQueue;
    }

    public CpuAlgorithm Algorithm { get; }

    public bool IsPreemptive { get; }

    public bool UsesRoundRobinQueue { get; }

    public abstract CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready);

    public virtual bool ShouldPreempt(CpuProcessCandidate running, CpuProcessCandidate bestReady) => false;

    protected static CpuProcessCandidate First(IReadOnlyList<CpuProcessCandidate> ready) =>
        ready.Count > 0 ? ready[0] : throw new ArgumentException("A ready candidate is required.", nameof(ready));
}

public sealed class FcfsSchedulingPolicy : CpuSchedulingPolicy
{
    public FcfsSchedulingPolicy() : base(CpuAlgorithm.Fcfs, isPreemptive: false) { }

    public override CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready) =>
        ready.OrderBy(p => p.ArrivalTime).ThenBy(p => p.InputOrder).First();
}

public sealed class SjfSchedulingPolicy : CpuSchedulingPolicy
{
    public SjfSchedulingPolicy() : base(CpuAlgorithm.Sjf, isPreemptive: false) { }

    public override CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready) =>
        ready.OrderBy(p => p.BurstTime).ThenBy(p => p.ArrivalTime).ThenBy(p => p.InputOrder).First();
}

public sealed class SrtfSchedulingPolicy : CpuSchedulingPolicy
{
    public SrtfSchedulingPolicy() : base(CpuAlgorithm.Srtf, isPreemptive: true) { }

    public override CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready) =>
        ready.OrderBy(p => p.RemainingTime).ThenBy(p => p.ArrivalTime).ThenBy(p => p.InputOrder).First();

    public override bool ShouldPreempt(CpuProcessCandidate running, CpuProcessCandidate bestReady) =>
        bestReady.RemainingTime < running.RemainingTime;
}

public sealed class PrioritySchedulingPolicy : CpuSchedulingPolicy
{
    public PrioritySchedulingPolicy(bool preemptive)
        : base(preemptive ? CpuAlgorithm.PreemptivePriority : CpuAlgorithm.Priority, preemptive) { }

    public override CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready) =>
        ready.OrderBy(p => p.Priority).ThenBy(p => p.ArrivalTime).ThenBy(p => p.InputOrder).First();

    public override bool ShouldPreempt(CpuProcessCandidate running, CpuProcessCandidate bestReady) =>
        IsPreemptive && bestReady.Priority < running.Priority;
}

public sealed class RoundRobinSchedulingPolicy : CpuSchedulingPolicy
{
    public RoundRobinSchedulingPolicy() : base(CpuAlgorithm.RoundRobin, isPreemptive: true, usesRoundRobinQueue: true) { }

    public override CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready) => First(ready);

    public override bool ShouldPreempt(CpuProcessCandidate running, CpuProcessCandidate bestReady) => false;
}
