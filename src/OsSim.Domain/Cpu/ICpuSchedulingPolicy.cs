namespace OsSim.Domain.Cpu;

public interface ICpuSchedulingPolicy
{
    CpuAlgorithm Algorithm { get; }

    bool IsPreemptive { get; }

    bool UsesRoundRobinQueue { get; }

    CpuProcessCandidate SelectNext(IReadOnlyList<CpuProcessCandidate> ready);

    bool ShouldPreempt(CpuProcessCandidate running, CpuProcessCandidate bestReady);
}
