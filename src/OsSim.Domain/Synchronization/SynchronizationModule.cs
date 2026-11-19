using OsSim.Domain.Simulation;

namespace OsSim.Domain.Synchronization;

public sealed class SynchronizationModule : ISimulationModule<SynchronizationInput, SynchronizationSnapshot>
{
    private const int MaxItemsOrMeals = 100;
    private const int MaxPhilosophers = 20;
    private const int MaxProducerConsumerActors = 20;

    public ValidationResult Validate(SynchronizationInput input)
    {
        var errors = new List<SimulationError>();
        if (!Enum.IsDefined(input.Scenario))
        {
            errors.Add(Invalid("Hãy chọn kịch bản đồng bộ được hỗ trợ."));
            return ValidationResult.From(errors);
        }

        if (input.Scenario == SynchronizationScenario.ProducerConsumer)
        {
            if (input.BufferCapacity < 1)
            {
                errors.Add(Invalid("Sức chứa buffer phải lớn hơn 0."));
            }

            if (input.ProducerCount is < 1 or > MaxProducerConsumerActors)
            {
                errors.Add(Invalid($"Số nhà sản xuất phải từ 1 đến {MaxProducerConsumerActors}."));
            }

            if (input.ConsumerCount is < 1 or > MaxProducerConsumerActors)
            {
                errors.Add(Invalid($"Số người tiêu dùng phải từ 1 đến {MaxProducerConsumerActors}."));
            }

            if (input.ItemsPerProducer is < 1 or > MaxItemsOrMeals || input.ItemsPerConsumer is < 1 or > MaxItemsOrMeals)
            {
                errors.Add(Invalid($"Số sản phẩm mỗi actor phải từ 1 đến {MaxItemsOrMeals}."));
            }

            if (input.ProducerCount is > 0 and <= MaxProducerConsumerActors &&
                input.ConsumerCount is > 0 and <= MaxProducerConsumerActors &&
                input.ItemsPerProducer is > 0 and <= MaxItemsOrMeals &&
                input.ItemsPerConsumer is > 0 and <= MaxItemsOrMeals &&
                (long)input.ProducerCount * input.ItemsPerProducer != (long)input.ConsumerCount * input.ItemsPerConsumer)
            {
                errors.Add(Invalid("Tổng số sản phẩm tạo ra phải bằng tổng số sản phẩm được tiêu thụ."));
            }
        }
        else
        {
            if (input.PhilosopherCount is < 3 or > MaxPhilosophers)
            {
                errors.Add(Invalid($"Bài toán triết gia ăn tối cần từ 3 đến {MaxPhilosophers} triết gia."));
            }

            if (input.MealsPerPhilosopher is < 1 or > MaxItemsOrMeals)
            {
                errors.Add(Invalid($"Số bữa mỗi triết gia phải từ 1 đến {MaxItemsOrMeals}."));
            }

            if (!Enum.IsDefined(input.DiningMode))
            {
                errors.Add(Invalid("Hãy chọn cách lấy đũa được hỗ trợ."));
            }
        }

        return ValidationResult.From(errors);
    }

    public SimulationCreationResult<SynchronizationSnapshot> CreateSession(SynchronizationInput input)
    {
        var validation = Validate(input);
        if (!validation.IsValid)
        {
            return SimulationCreationResult<SynchronizationSnapshot>.Failed(validation.Errors);
        }

        var initial = CreateInitialSnapshot(input);
        var session = new ReplayableSimulationSession<SynchronizationSnapshot>(
            initial,
            (snapshot, command) => Advance(input, snapshot, command),
            snapshot => !snapshot.IsCompleted && !snapshot.IsDeadlocked,
            snapshot => snapshot.IsDeadlocked
                ? new SimulationError(SimulationErrorCode.Deadlocked, "Không còn actor sẵn sàng chạy; đồ thị chờ đang deadlock.")
                : new SimulationError(SimulationErrorCode.BoundaryReached, "Kịch bản đồng bộ đã hoàn tất."),
            snapshot => snapshot.IsCompleted);
        return SimulationCreationResult<SynchronizationSnapshot>.Created(session);
    }

    private static SynchronizationSnapshot CreateInitialSnapshot(SynchronizationInput input)
    {
        var actors = input.Scenario == SynchronizationScenario.ProducerConsumer
            ? Enumerable.Range(1, input.ProducerCount).Select(index => NewActor($"Producer-{index}", "Producer"))
                .Concat(Enumerable.Range(1, input.ConsumerCount).Select(index => NewActor($"Consumer-{index}", "Consumer")))
                .ToArray()
            : Enumerable.Range(0, input.PhilosopherCount)
                .Select(index => NewActor($"Philosopher-{index}", "Philosopher"))
                .ToArray();

        var resources = input.Scenario == SynchronizationScenario.ProducerConsumer
            ? new[]
            {
                new MutableResource("empty", input.BufferCapacity, false),
                new MutableResource("full", 0, false),
                new MutableResource("mutex", 1, true)
            }
            : Enumerable.Range(0, input.PhilosopherCount)
                .Select(index => new MutableResource($"fork-{index}", 1, true))
                .ToArray();

        return ToSnapshot(0, actors, resources, [], [], 0, isDeadlocked: false);
    }

    private static SimulationTransition<SynchronizationSnapshot> Advance(
        SynchronizationInput input,
        SynchronizationSnapshot snapshot,
        SimulationCommand? command)
    {
        var actors = snapshot.Actors.Select(MutableActor.From).ToList();
        var resources = snapshot.Resources.Select(MutableResource.From).ToList();
        var buffer = snapshot.Buffer.ToList();
        var events = snapshot.Events.ToList();
        if (command is { AutomaticDispatch: false } && string.IsNullOrWhiteSpace(command.ActorId))
        {
            return Failure(snapshot, SimulationErrorCode.InvalidCommand, "Bước chạy thủ công cần mã của một actor đang sẵn sàng.");
        }

        var actorIndex = ChooseActor(snapshot, command, actors);
        if (actorIndex < 0)
        {
            var code = string.IsNullOrWhiteSpace(command?.ActorId) ? SimulationErrorCode.Deadlocked : SimulationErrorCode.InvalidCommand;
            var message = code == SimulationErrorCode.Deadlocked
                ? "Không có actor nào sẵn sàng chạy."
                : $"Actor '{ActorLabel(command!.ActorId!)}' chưa sẵn sàng chạy.";
            return Failure(snapshot, code, message);
        }

        var actor = actors[actorIndex];
        var automaticIndex = (actorIndex + 1) % actors.Count;
        var nextTime = snapshot.StepNumber + 1;

        if (input.Scenario == SynchronizationScenario.ProducerConsumer)
        {
            ExecuteProducerConsumer(input, actor, actors, resources, buffer, events, nextTime);
        }
        else
        {
            ExecutePhilosopher(input, actor, actors, resources, events, nextTime);
        }

        var isCompleted = actors.All(candidate => candidate.State == VirtualActorState.Terminated);
        var isDeadlocked = !isCompleted && actors.All(candidate => candidate.State != VirtualActorState.Ready);
        var next = ToSnapshot(nextTime, actors, resources, buffer, events, automaticIndex, isDeadlocked, actor.Id);
        return new SimulationTransition<SynchronizationSnapshot>(true, next, events[^1], null);
    }

    private static int ChooseActor(
        SynchronizationSnapshot snapshot,
        SimulationCommand? command,
        IReadOnlyList<MutableActor> actors)
    {
        if (command is { AutomaticDispatch: false })
        {
            if (string.IsNullOrWhiteSpace(command.ActorId))
            {
                return -1;
            }

            for (var index = 0; index < actors.Count; index++)
            {
                if (actors[index].Id == command.ActorId && actors[index].State == VirtualActorState.Ready)
                {
                    return index;
                }
            }

            return -1;
        }

        for (var offset = 0; offset < actors.Count; offset++)
        {
            var index = (snapshot.NextAutomaticActorIndex + offset) % actors.Count;
            if (actors[index].State == VirtualActorState.Ready)
            {
                return index;
            }
        }

        return -1;
    }

    private static void ExecuteProducerConsumer(
        SynchronizationInput input,
        MutableActor actor,
        IList<MutableActor> actors,
        IList<MutableResource> resources,
        IList<string> buffer,
        IList<SimulationEvent> events,
        long time)
    {
        if (actor.Role == "Producer")
        {
            switch (actor.ProgramCounter)
            {
                case 0: Acquire(actor, FindResource(resources, "empty"), actors, events, time); break;
                case 1: Acquire(actor, FindResource(resources, "mutex"), actors, events, time); break;
                case 2:
                    if (buffer.Count >= input.BufferCapacity)
                    {
                        throw new InvalidOperationException("Actor nhà sản xuất vượt semaphore empty khi buffer đã đầy.");
                    }
                    buffer.Add($"Item-{actor.Id}-{actor.CompletedWork + 1}");
                    actor.ProgramCounter++;
                    AddEvent(events, time, "PRODUCE", $"{ActorLabel(actor.Id)} đưa sản phẩm {actor.CompletedWork + 1} vào buffer ({buffer.Count}/{input.BufferCapacity}).", actor.Id);
                    break;
                case 3: Release(actor, FindResource(resources, "mutex"), actors, events, time); break;
                case 4: Signal(FindResource(resources, "full"), actors, events, time, actor.Id); actor.ProgramCounter++; break;
                case 5: FinishUnit(actor, input.ItemsPerProducer, events, time, "item"); break;
            }
        }
        else
        {
            switch (actor.ProgramCounter)
            {
                case 0: Acquire(actor, FindResource(resources, "full"), actors, events, time); break;
                case 1: Acquire(actor, FindResource(resources, "mutex"), actors, events, time); break;
                case 2:
                    if (buffer.Count == 0)
                    {
                        throw new InvalidOperationException("Actor tiêu dùng vượt semaphore full khi buffer đang rỗng.");
                    }
                    var item = buffer[0];
                    buffer.RemoveAt(0);
                    actor.ProgramCounter++;
                    AddEvent(events, time, "CONSUME", $"{ActorLabel(actor.Id)} lấy {ItemLabel(item)} khỏi buffer ({buffer.Count}/{input.BufferCapacity}).", actor.Id);
                    break;
                case 3: Release(actor, FindResource(resources, "mutex"), actors, events, time); break;
                case 4: Signal(FindResource(resources, "empty"), actors, events, time, actor.Id); actor.ProgramCounter++; break;
                case 5: FinishUnit(actor, input.ItemsPerConsumer, events, time, "item"); break;
            }
        }
    }

    private static void ExecutePhilosopher(
        SynchronizationInput input,
        MutableActor actor,
        IList<MutableActor> actors,
        IList<MutableResource> resources,
        IList<SimulationEvent> events,
        long time)
    {
        var philosopherIndex = int.Parse(actor.Id.AsSpan("Philosopher-".Length), System.Globalization.CultureInfo.InvariantCulture);
        var left = philosopherIndex;
        var right = (philosopherIndex + 1) % input.PhilosopherCount;
        var firstFork = input.DiningMode == DiningPhilosophersMode.ResourceOrdering ? Math.Min(left, right) : left;
        var secondFork = input.DiningMode == DiningPhilosophersMode.ResourceOrdering ? Math.Max(left, right) : right;

        switch (actor.ProgramCounter)
        {
            case 0:
                actor.ProgramCounter++;
                AddEvent(events, time, "THINK", $"{ActorLabel(actor.Id)} đang suy nghĩ.", actor.Id);
                break;
            case 1: Acquire(actor, FindResource(resources, $"fork-{firstFork}"), actors, events, time); break;
            case 2: Acquire(actor, FindResource(resources, $"fork-{secondFork}"), actors, events, time); break;
            case 3:
                actor.ProgramCounter++;
                AddEvent(events, time, "EAT", $"{ActorLabel(actor.Id)} đang ăn bữa {actor.CompletedWork + 1}.", actor.Id);
                break;
            case 4: Release(actor, FindResource(resources, $"fork-{secondFork}"), actors, events, time); break;
            case 5: Release(actor, FindResource(resources, $"fork-{firstFork}"), actors, events, time); break;
            case 6: FinishUnit(actor, input.MealsPerPhilosopher, events, time, "meal"); break;
        }
    }

    private static void Acquire(MutableActor actor, MutableResource resource, IList<MutableActor> actors, IList<SimulationEvent> events, long time)
    {
        var available = resource.IsMutex ? resource.OwnerActorId is null : resource.Count > 0;
        if (available)
        {
            if (resource.IsMutex)
            {
                resource.Count = 0;
                resource.OwnerActorId = actor.Id;
                actor.HeldResources.Add(resource.Id);
            }
            else
            {
                resource.Count--;
            }

            actor.ProgramCounter++;
            AddEvent(events, time, "ACQUIRE", $"{ActorLabel(actor.Id)} lấy được {ResourceLabel(resource.Id)}.", actor.Id);
            return;
        }

        actor.State = VirtualActorState.Blocked;
        actor.WaitingFor = resource.Id;
        resource.WaitQueue.Add(actor.Id);
        AddEvent(events, time, "BLOCK", $"{ActorLabel(actor.Id)} bị chặn khi chờ {ResourceLabel(resource.Id)}.", actor.Id);
    }

    private static void Release(MutableActor actor, MutableResource resource, IList<MutableActor> actors, IList<SimulationEvent> events, long time)
    {
        if (resource.OwnerActorId != actor.Id)
        {
            throw new InvalidOperationException($"Actor {actor.Id} không sở hữu tài nguyên {resource.Id}.");
        }

        actor.HeldResources.Remove(resource.Id);
        resource.OwnerActorId = null;
        resource.Count = 1;
        WakeNext(resource, actors, events, time, grantMutex: true);
        actor.ProgramCounter++;
        AddEvent(events, time, "RELEASE", $"{ActorLabel(actor.Id)} nhả {ResourceLabel(resource.Id)}.", actor.Id);
    }

    private static void Signal(MutableResource resource, IList<MutableActor> actors, IList<SimulationEvent> events, long time, string actorId)
    {
        if (resource.WaitQueue.Count > 0)
        {
            WakeNext(resource, actors, events, time, grantMutex: false);
        }
        else
        {
            resource.Count++;
        }

        AddEvent(events, time, "SIGNAL", $"{ActorLabel(actorId)} phát tín hiệu cho {ResourceLabel(resource.Id)}.", actorId);
    }

    private static void WakeNext(MutableResource resource, IList<MutableActor> actors, IList<SimulationEvent> events, long time, bool grantMutex)
    {
        if (resource.WaitQueue.Count == 0)
        {
            return;
        }

        var actorId = resource.WaitQueue[0];
        resource.WaitQueue.RemoveAt(0);
        var actor = actors.Single(candidate => candidate.Id == actorId);
        actor.State = VirtualActorState.Ready;
        actor.WaitingFor = null;
        actor.ProgramCounter++;
        if (grantMutex)
        {
            resource.OwnerActorId = actor.Id;
            resource.Count = 0;
            actor.HeldResources.Add(resource.Id);
        }

        AddEvent(events, time, "WAKE", $"{ActorLabel(actor.Id)} được đánh thức theo FIFO khỏi hàng đợi {ResourceLabel(resource.Id)}.", actor.Id);
    }

    private static void FinishUnit(MutableActor actor, int target, IList<SimulationEvent> events, long time, string unit)
    {
        actor.CompletedWork++;
        if (actor.CompletedWork >= target)
        {
            actor.State = VirtualActorState.Terminated;
            AddEvent(events, time, "TERMINATED", $"{ActorLabel(actor.Id)} hoàn tất {actor.CompletedWork} {UnitLabel(unit)} và kết thúc.", actor.Id);
        }
        else
        {
            actor.ProgramCounter = 0;
            AddEvent(events, time, "UNIT_COMPLETED", $"{ActorLabel(actor.Id)} hoàn tất {UnitLabel(unit)} thứ {actor.CompletedWork}.", actor.Id);
        }
    }

    private static MutableResource FindResource(IList<MutableResource> resources, string id) =>
        resources.Single(resource => resource.Id == id);

    private static MutableActor NewActor(string id, string role) => new()
    {
        Id = id,
        Role = role,
        State = VirtualActorState.Ready
    };

    private static string ActorLabel(string id)
    {
        if (id.StartsWith("Producer-", StringComparison.Ordinal)) return $"Nhà sản xuất {id["Producer-".Length..]}";
        if (id.StartsWith("Consumer-", StringComparison.Ordinal)) return $"Người tiêu dùng {id["Consumer-".Length..]}";
        if (id.StartsWith("Philosopher-", StringComparison.Ordinal) &&
            int.TryParse(id["Philosopher-".Length..], out var philosopherIndex))
        {
            return $"Triết gia {philosopherIndex + 1}";
        }

        return id;
    }

    private static string ResourceLabel(string id)
    {
        if (id == "empty") return "ô buffer trống";
        if (id == "full") return "ô buffer có dữ liệu";
        if (id == "mutex") return "mutex";
        if (id.StartsWith("fork-", StringComparison.Ordinal) && int.TryParse(id["fork-".Length..], out var forkIndex))
        {
            return $"đũa {forkIndex + 1}";
        }

        return id;
    }

    private static string ItemLabel(string id)
    {
        const string prefix = "Item-";
        var lastSeparator = id.LastIndexOf('-');
        if (!id.StartsWith(prefix, StringComparison.Ordinal) || lastSeparator <= prefix.Length)
        {
            return id;
        }

        return $"sản phẩm {id[(lastSeparator + 1)..]} của {ActorLabel(id[prefix.Length..lastSeparator])}";
    }

    private static string UnitLabel(string unit) => unit == "item" ? "sản phẩm" : "bữa ăn";

    private static SynchronizationSnapshot ToSnapshot(
        long step,
        IReadOnlyList<MutableActor> actors,
        IReadOnlyList<MutableResource> resources,
        IReadOnlyList<string> buffer,
        IReadOnlyList<SimulationEvent> events,
        int automaticIndex,
        bool isDeadlocked,
        string? lastDispatchedActorId = null)
    {
        var actorSnapshots = actors.Select(actor => new VirtualActorSnapshot(
            actor.Id, actor.Role, actor.State, actor.ProgramCounter, actor.CompletedWork, actor.WaitingFor,
            Array.AsReadOnly(actor.HeldResources.ToArray()))).ToArray();
        var resourceSnapshots = resources.Select(resource => new SynchronizationResourceSnapshot(
            resource.Id, resource.Count, resource.OwnerActorId, Array.AsReadOnly(resource.WaitQueue.ToArray()))).ToArray();
        var graph = actors.Where(actor => actor.State == VirtualActorState.Blocked && actor.WaitingFor is not null)
            .Select(actor => new WaitForEdge(
                actor.Id,
                actor.WaitingFor!,
                resources.Single(resource => resource.Id == actor.WaitingFor).OwnerActorId))
            .ToArray();
        var complete = actors.All(actor => actor.State == VirtualActorState.Terminated);
        return new SynchronizationSnapshot(
            step,
            Array.AsReadOnly(actorSnapshots),
            Array.AsReadOnly(resourceSnapshots),
            Array.AsReadOnly(buffer.ToArray()),
            Array.AsReadOnly(events.ToArray()),
            Array.AsReadOnly(graph),
            FindCycles(graph),
            automaticIndex,
            complete,
            !complete && isDeadlocked)
        {
            LastDispatchedActorId = lastDispatchedActorId
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(IReadOnlyList<WaitForEdge> edges)
    {
        var nextActor = edges.Where(edge => edge.OwnerActorId is not null)
            .GroupBy(edge => edge.WaiterActorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().OwnerActorId!, StringComparer.Ordinal);
        var cycles = new List<IReadOnlyList<string>>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in nextActor.Keys)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = start;
            while (nextActor.TryGetValue(current, out var next))
            {
                if (positions.TryGetValue(current, out var cycleStart))
                {
                    var cycle = path.Skip(cycleStart).ToArray();
                    var signature = string.Join("|", cycle.Order(StringComparer.Ordinal));
                    if (signatures.Add(signature))
                    {
                        cycles.Add(Array.AsReadOnly(cycle));
                    }

                    break;
                }

                positions[current] = path.Count;
                path.Add(current);
                current = next;
            }
        }

        return Array.AsReadOnly(cycles.ToArray());
    }

    private static SimulationTransition<SynchronizationSnapshot> Failure(
        SynchronizationSnapshot snapshot,
        SimulationErrorCode code,
        string message) => new(false, snapshot, null, new SimulationError(code, message));

    private static void AddEvent(IList<SimulationEvent> events, long time, string code, string description, string actorId) =>
        events.Add(new SimulationEvent(events.Count + 1, time, code, description, actorId));

    private static SimulationError Invalid(string message) => new(SimulationErrorCode.InvalidInput, message);

    private sealed class MutableActor
    {
        public required string Id { get; init; }
        public required string Role { get; init; }
        public VirtualActorState State { get; set; }
        public int ProgramCounter { get; set; }
        public int CompletedWork { get; set; }
        public string? WaitingFor { get; set; }
        public List<string> HeldResources { get; } = [];

        public static MutableActor From(VirtualActorSnapshot snapshot)
        {
            var actor = new MutableActor
            {
                Id = snapshot.Id,
                Role = snapshot.Role,
                State = snapshot.State,
                ProgramCounter = snapshot.ProgramCounter,
                CompletedWork = snapshot.CompletedWork,
                WaitingFor = snapshot.WaitingFor
            };
            actor.HeldResources.AddRange(snapshot.HeldResources);
            return actor;
        }
    }

    private sealed class MutableResource(string id, int count, bool isMutex)
    {
        public string Id { get; } = id;
        public int Count { get; set; } = count;
        public bool IsMutex { get; } = isMutex;
        public string? OwnerActorId { get; set; }
        public List<string> WaitQueue { get; } = [];

        public static MutableResource From(SynchronizationResourceSnapshot snapshot)
        {
            var resource = new MutableResource(snapshot.Id, snapshot.Count, snapshot.Id == "mutex" || snapshot.Id.StartsWith("fork-", StringComparison.Ordinal))
            {
                OwnerActorId = snapshot.OwnerActorId
            };
            resource.WaitQueue.AddRange(snapshot.WaitQueue);
            return resource;
        }
    }
}
