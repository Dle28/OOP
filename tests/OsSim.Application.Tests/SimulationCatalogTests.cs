using OsSim.Application;
using OsSim.Domain.Cpu;
using OsSim.Domain.Memory;
using OsSim.Domain.Synchronization;

namespace OsSim.Application.Tests;

public sealed class SimulationCatalogTests
{
    [Fact]
    public void Catalog_creates_sessions_for_each_module()
    {
        var catalog = new SimulationCatalog();

        var cpu = catalog.CreateCpuSession(new CpuSchedulingInput([new CpuProcessInput("P1", 0, 1)], CpuAlgorithm.Fcfs));
        var synchronization = catalog.CreateSynchronizationSession(new SynchronizationInput(SynchronizationScenario.ProducerConsumer));
        var memory = catalog.CreateVirtualMemorySession(new VirtualMemoryInput([0], 8, 4, 1, PageReplacementAlgorithm.Fifo));

        Assert.True(cpu.Succeeded);
        Assert.True(synchronization.Succeeded);
        Assert.True(memory.Succeeded);
    }

    [Fact]
    public void Catalog_preserves_module_validation_errors()
    {
        var result = new SimulationCatalog().CreateCpuSession(
            new CpuSchedulingInput([new CpuProcessInput("P1", 0, 1)], CpuAlgorithm.RoundRobin, TimeQuantum: 0));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Message.Contains("quantum", StringComparison.OrdinalIgnoreCase));
    }
}
