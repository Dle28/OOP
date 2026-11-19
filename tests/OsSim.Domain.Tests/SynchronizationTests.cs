using OsSim.Domain.Simulation;
using OsSim.Domain.Synchronization;

namespace OsSim.Domain.Tests;

public sealed class SynchronizationTests
{
    [Fact]
    public void Producer_consumer_completes_with_a_bounded_buffer()
    {
        var session = Create(new SynchronizationInput(
            SynchronizationScenario.ProducerConsumer,
            BufferCapacity: 1,
            ProducerCount: 2,
            ConsumerCount: 2,
            ItemsPerProducer: 2,
            ItemsPerConsumer: 2));
        RunToEnd(session);

        Assert.True(session.CurrentSnapshot.IsCompleted);
        Assert.False(session.CurrentSnapshot.IsDeadlocked);
        Assert.Empty(session.CurrentSnapshot.Buffer);
        Assert.Equal(4, session.CurrentSnapshot.Actors.Count(actor => actor.State == VirtualActorState.Terminated));
        Assert.Equal(1, session.CurrentSnapshot.Resources.Single(resource => resource.Id == "empty").Count);
        Assert.Equal(0, session.CurrentSnapshot.Resources.Single(resource => resource.Id == "full").Count);
    }

    [Fact]
    public void Semaphore_wait_queue_wakes_consumers_in_fifo_order()
    {
        var session = Create(new SynchronizationInput(
            SynchronizationScenario.ProducerConsumer,
            BufferCapacity: 1,
            ProducerCount: 1,
            ConsumerCount: 2,
            ItemsPerProducer: 2,
            ItemsPerConsumer: 1));

        Step(session, "Consumer-1");
        Step(session, "Consumer-2");
        Assert.Equal(new[] { "Consumer-1", "Consumer-2" }, session.CurrentSnapshot.Resources.Single(resource => resource.Id == "full").WaitQueue);

        Step(session, "Producer-1"); // wait empty
        Step(session, "Producer-1"); // wait mutex
        Step(session, "Producer-1"); // produce
        Step(session, "Producer-1"); // release mutex
        Step(session, "Producer-1"); // signal full and wake the first waiter

        Assert.Equal(VirtualActorState.Ready, session.CurrentSnapshot.Actors.Single(actor => actor.Id == "Consumer-1").State);
        Assert.Equal(VirtualActorState.Blocked, session.CurrentSnapshot.Actors.Single(actor => actor.Id == "Consumer-2").State);
        Assert.Equal(new[] { "Consumer-2" }, session.CurrentSnapshot.Resources.Single(resource => resource.Id == "full").WaitQueue);
    }

    [Fact]
    public void Naive_dining_philosophers_reports_a_deadlock_cycle()
    {
        var session = Create(new SynchronizationInput(
            SynchronizationScenario.DiningPhilosophers,
            PhilosopherCount: 5,
            MealsPerPhilosopher: 1,
            DiningMode: DiningPhilosophersMode.NaiveLeftFirst));
        RunToEnd(session, 500);

        Assert.True(session.CurrentSnapshot.IsDeadlocked);
        Assert.Equal(5, session.CurrentSnapshot.WaitForGraph.Count);
        Assert.Single(session.CurrentSnapshot.DeadlockCycles);
        Assert.Equal(5, session.CurrentSnapshot.DeadlockCycles[0].Count);
    }

    [Fact]
    public void Resource_ordering_allows_philosophers_to_finish()
    {
        var session = Create(new SynchronizationInput(
            SynchronizationScenario.DiningPhilosophers,
            PhilosopherCount: 5,
            MealsPerPhilosopher: 2,
            DiningMode: DiningPhilosophersMode.ResourceOrdering));
        RunToEnd(session, 1_000);

        Assert.True(session.CurrentSnapshot.IsCompleted);
        Assert.False(session.CurrentSnapshot.IsDeadlocked);
        Assert.All(session.CurrentSnapshot.Actors, actor => Assert.Equal(2, actor.CompletedWork));
    }

    [Fact]
    public void Invalid_manual_actor_does_not_mutate_session()
    {
        var session = Create(new SynchronizationInput(SynchronizationScenario.ProducerConsumer));
        var before = session.CurrentSnapshot;

        var result = session.StepForward(new SimulationCommand("missing-actor", AutomaticDispatch: false));

        Assert.False(result.Succeeded);
        Assert.Equal(SimulationErrorCode.InvalidCommand, result.Error!.Code);
        Assert.Same(before, session.CurrentSnapshot);
    }

    [Fact]
    public void Rewinding_and_choosing_a_new_actor_discards_the_old_future_branch()
    {
        var session = Create(new SynchronizationInput(SynchronizationScenario.ProducerConsumer));
        Step(session, "Producer-1");
        Step(session, "Producer-1");
        Step(session, "Producer-1");
        Assert.Single(session.CurrentSnapshot.Buffer);
        Assert.True(session.StepBackward().Succeeded);

        Step(session, "Consumer-1");

        Assert.Empty(session.CurrentSnapshot.Buffer);
        Assert.Equal("BLOCK", session.CurrentSnapshot.Events[^1].Code);
        Assert.Equal(3, session.CurrentSnapshot.StepNumber);
    }

    [Fact]
    public void Producer_consumer_rejects_unequal_production_and_consumption_totals()
    {
        var result = new SynchronizationModule().CreateSession(new SynchronizationInput(
            SynchronizationScenario.ProducerConsumer,
            ProducerCount: 1,
            ConsumerCount: 2,
            ItemsPerProducer: 1,
            ItemsPerConsumer: 1));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Message.Contains("phải bằng", StringComparison.Ordinal));
    }

    private static ISimulationSession<SynchronizationSnapshot> Create(SynchronizationInput input)
    {
        var result = new SynchronizationModule().CreateSession(input);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        return result.Session!;
    }

    private static void Step(ISimulationSession<SynchronizationSnapshot> session, string actorId)
    {
        var result = session.StepForward(new SimulationCommand(actorId, AutomaticDispatch: false));
        Assert.True(result.Succeeded, result.Error?.Message);
    }

    private static void RunToEnd(ISimulationSession<SynchronizationSnapshot> session, int maxSteps = 10_000)
    {
        var steps = 0;
        while (session.CanStepForward && steps++ < maxSteps)
        {
            var result = session.StepForward(new SimulationCommand(AutomaticDispatch: true));
            Assert.True(result.Succeeded, result.Error?.Message);
        }

        Assert.False(session.CanStepForward);
    }
}
