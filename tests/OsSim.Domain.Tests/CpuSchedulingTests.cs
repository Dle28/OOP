using OsSim.Domain.Cpu;

namespace OsSim.Domain.Tests;

public sealed class CpuSchedulingTests
{
    [Fact]
    public void Fcfs_builds_timeline_and_metrics()
    {
        var result = RunToEnd(new CpuSchedulingInput(
        [
            new CpuProcessInput("P1", 0, 5),
            new CpuProcessInput("P2", 2, 3)
        ], CpuAlgorithm.Fcfs));

        Assert.Equal(new[] { new GanttSegment("P1", 0, 5), new GanttSegment("P2", 5, 8) }, result.GanttSegments);
        Assert.NotNull(result.Metrics);
        Assert.Equal(0, result.Metrics!.Processes.Single(item => item.ProcessId == "P1").WaitingTime);
        Assert.Equal(3, result.Metrics.Processes.Single(item => item.ProcessId == "P2").WaitingTime);
        Assert.Equal(1.5, result.Metrics.AverageWaitingTime);
        Assert.Equal(100, result.Metrics.CpuUtilizationPercent);
    }

    [Fact]
    public void Srtf_preempts_when_a_shorter_process_arrives()
    {
        var result = RunToEnd(new CpuSchedulingInput(
        [
            new CpuProcessInput("P1", 0, 8),
            new CpuProcessInput("P2", 1, 4)
        ], CpuAlgorithm.Srtf));

        Assert.Equal(new[]
        {
            new GanttSegment("P1", 0, 1),
            new GanttSegment("P2", 1, 5),
            new GanttSegment("P1", 5, 12)
        }, result.GanttSegments);
    }

    [Fact]
    public void Round_robin_enqueues_boundary_arrivals_before_expired_process()
    {
        var result = RunToEnd(new CpuSchedulingInput(
        [
            new CpuProcessInput("P1", 0, 4),
            new CpuProcessInput("P2", 2, 1)
        ], CpuAlgorithm.RoundRobin, TimeQuantum: 2));

        Assert.Equal(new[]
        {
            new GanttSegment("P1", 0, 2),
            new GanttSegment("P2", 2, 3),
            new GanttSegment("P1", 3, 5)
        }, result.GanttSegments);
    }

    [Fact]
    public void Cpu_idle_time_is_shown_and_reduces_utilization()
    {
        var result = RunToEnd(new CpuSchedulingInput(
            [new CpuProcessInput("P1", 2, 1)], CpuAlgorithm.Fcfs));

        Assert.Equal(new[] { new GanttSegment(null, 0, 2), new GanttSegment("P1", 2, 3) }, result.GanttSegments);
        Assert.Equal(100d / 3, result.Metrics!.CpuUtilizationPercent, precision: 5);
    }

    [Fact]
    public void Invalid_cpu_input_returns_errors_without_creating_a_session()
    {
        var module = new CpuSchedulingModule();
        var result = module.CreateSession(new CpuSchedulingInput(
        [
            new CpuProcessInput("P1", 0, 2),
            new CpuProcessInput("P1", -1, 0)
        ], CpuAlgorithm.RoundRobin, TimeQuantum: 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Timeline_session_can_step_back_and_reset_to_the_same_snapshots()
    {
        var creation = new CpuSchedulingModule().CreateSession(new CpuSchedulingInput(
            [new CpuProcessInput("P1", 0, 3)], CpuAlgorithm.Fcfs));
        var session = Assert.IsAssignableFrom<OsSim.Domain.Simulation.ISimulationSession<CpuSimulationSnapshot>>(creation.Session);
        var initial = session.CurrentSnapshot;

        Assert.True(session.StepForward().Succeeded);
        var first = session.CurrentSnapshot;
        Assert.True(session.StepForward().Succeeded);
        Assert.True(session.StepBackward().Succeeded);
        Assert.Same(first, session.CurrentSnapshot);
        Assert.True(session.Reset().Succeeded);
        Assert.Same(initial, session.CurrentSnapshot);
    }

    private static CpuSimulationSnapshot RunToEnd(CpuSchedulingInput input)
    {
        var creation = new CpuSchedulingModule().CreateSession(input);
        Assert.True(creation.Succeeded, string.Join(Environment.NewLine, creation.Errors.Select(error => error.Message)));
        var session = creation.Session!;
        var guard = 0;
        while (session.CanStepForward && guard++ < 20_000)
        {
            var step = session.StepForward();
            Assert.True(step.Succeeded, step.Error?.Message);
        }

        Assert.False(session.CanStepForward);
        return session.CurrentSnapshot;
    }
}
