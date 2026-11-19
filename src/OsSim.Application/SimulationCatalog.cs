using OsSim.Domain.Cpu;
using OsSim.Domain.Memory;
using OsSim.Domain.ResourceAllocation;
using OsSim.Domain.Simulation;
using OsSim.Domain.Synchronization;

namespace OsSim.Application;

public sealed class SimulationCatalog
{
    private readonly ISimulationModule<CpuSchedulingInput, CpuSimulationSnapshot> _cpu;
    private readonly ISimulationModule<SynchronizationInput, SynchronizationSnapshot> _synchronization;
    private readonly ISimulationModule<VirtualMemoryInput, VirtualMemorySnapshot> _memory;
    private readonly IBankerModule _banker;

    public SimulationCatalog()
        : this(new CpuSchedulingModule(), new SynchronizationModule(), new VirtualMemoryModule())
    {
    }

    public SimulationCatalog(
        ISimulationModule<CpuSchedulingInput, CpuSimulationSnapshot> cpu,
        ISimulationModule<SynchronizationInput, SynchronizationSnapshot> synchronization,
        ISimulationModule<VirtualMemoryInput, VirtualMemorySnapshot> memory,
        IBankerModule? banker = null)
    {
        _cpu = cpu;
        _synchronization = synchronization;
        _memory = memory;
        _banker = banker ?? new BankerModule();
    }

    public SimulationCreationResult<CpuSimulationSnapshot> CreateCpuSession(CpuSchedulingInput input) =>
        _cpu.CreateSession(input);

    public SimulationCreationResult<SynchronizationSnapshot> CreateSynchronizationSession(SynchronizationInput input) =>
        _synchronization.CreateSession(input);

    public SimulationCreationResult<VirtualMemorySnapshot> CreateVirtualMemorySession(VirtualMemoryInput input) =>
        _memory.CreateSession(input);

    public SimulationCreationResult<BankerSnapshot> CreateBankerSession(BankerInput input) =>
        _banker.CreateSession(input);

    public BankerRequestResult TestBankerRequest(BankerInput input, BankerResourceRequest request) =>
        _banker.TestRequest(input, request);
}
