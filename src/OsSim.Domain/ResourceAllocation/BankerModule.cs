using OsSim.Domain.Simulation;

namespace OsSim.Domain.ResourceAllocation;

public sealed class BankerModule : IBankerModule
{
    private const int MaximumResources = 8;
    private const int MaximumProcesses = 20;
    private const long MaximumUnitsPerResource = 1_000_000;

    public ValidationResult Validate(BankerInput input)
    {
        if (input is null)
        {
            return ValidationResult.From([Invalid("Thiếu trạng thái tài nguyên để chạy Banker.")]);
        }

        var errors = new List<SimulationError>();
        if (input.ResourceNames is null || input.ResourceNames.Count is < 1 or > MaximumResources)
        {
            errors.Add(Invalid($"Cần từ 1 đến {MaximumResources} loại tài nguyên."));
            return ValidationResult.From(errors);
        }

        var resourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in input.ResourceNames)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add(Invalid("Tên tài nguyên không được để trống."));
            }
            else if (!resourceNames.Add(resourceName.Trim()))
            {
                errors.Add(Invalid($"Tên tài nguyên '{resourceName}' bị trùng."));
            }
        }

        if (input.Available is null || input.Available.Count != input.ResourceNames.Count)
        {
            errors.Add(Invalid("Vector Available phải có một giá trị cho mỗi loại tài nguyên."));
        }
        else if (input.Available.Any(value => value < 0))
        {
            errors.Add(Invalid("Available không được chứa số âm."));
        }

        if (input.Processes is null || input.Processes.Count is < 1 or > MaximumProcesses)
        {
            errors.Add(Invalid($"Cần từ 1 đến {MaximumProcesses} tiến trình."));
            return ValidationResult.From(errors);
        }

        var processIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in input.Processes)
        {
            if (process is null)
            {
                errors.Add(Invalid("Danh sách tiến trình không được chứa phần tử rỗng."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(process.Id))
            {
                errors.Add(Invalid("Mỗi tiến trình cần có mã không rỗng."));
            }
            else if (!processIds.Add(process.Id.Trim()))
            {
                errors.Add(Invalid($"Mã tiến trình '{process.Id}' bị trùng."));
            }

            if (process.Allocation is null || process.Allocation.Count != input.ResourceNames.Count ||
                process.MaxDemand is null || process.MaxDemand.Count != input.ResourceNames.Count)
            {
                errors.Add(Invalid($"Allocation và Max của '{process.Id}' phải khớp số loại tài nguyên."));
                continue;
            }

            for (var resourceIndex = 0; resourceIndex < input.ResourceNames.Count; resourceIndex++)
            {
                var allocated = process.Allocation[resourceIndex];
                var maximum = process.MaxDemand[resourceIndex];
                if (allocated < 0 || maximum < 0)
                {
                    errors.Add(Invalid($"Allocation và Max của '{process.Id}' không được âm."));
                }
                else if (allocated > maximum)
                {
                    errors.Add(Invalid($"Allocation của '{process.Id}' vượt Max tại tài nguyên {input.ResourceNames[resourceIndex]}."));
                }
            }
        }

        if (input.Available is not null && input.Available.Count == input.ResourceNames.Count &&
            input.Processes.All(process => process is not null && process.Allocation is not null && process.Allocation.Count == input.ResourceNames.Count))
        {
            for (var resourceIndex = 0; resourceIndex < input.ResourceNames.Count; resourceIndex++)
            {
                var total = (long)input.Available[resourceIndex] + input.Processes.Sum(process => (long)process.Allocation[resourceIndex]);
                if (total > MaximumUnitsPerResource)
                {
                    errors.Add(Invalid($"Tổng số đơn vị của {input.ResourceNames[resourceIndex]} vượt giới hạn {MaximumUnitsPerResource}."));
                }
            }
        }

        return ValidationResult.From(errors);
    }

    public SimulationCreationResult<BankerSnapshot> CreateSession(BankerInput input)
    {
        var validation = Validate(input);
        if (!validation.IsValid)
        {
            return SimulationCreationResult<BankerSnapshot>.Failed(validation.Errors);
        }

        var trace = BuildTrace(input);
        return SimulationCreationResult<BankerSnapshot>.Created(
            new TimelineSimulationSession<BankerSnapshot>(trace.Initial, trace.Frames));
    }

    public BankerRequestResult TestRequest(BankerInput currentState, BankerResourceRequest request)
    {
        var validation = Validate(currentState);
        if (!validation.IsValid)
        {
            return FailedRequest(validation.Errors);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ProcessId))
        {
            return FailedRequest([Invalid("Chọn tiến trình cần kiểm tra request.")]);
        }

        if (request.Amounts is null || request.Amounts.Count != currentState.ResourceNames.Count || request.Amounts.Any(amount => amount < 0))
        {
            return FailedRequest([Invalid("Vector request phải khớp số tài nguyên và không được chứa số âm.")]);
        }

        var processIndex = -1;
        for (var index = 0; index < currentState.Processes.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(currentState.Processes[index].Id.Trim(), request.ProcessId.Trim()))
            {
                processIndex = index;
                break;
            }
        }

        if (processIndex < 0)
        {
            return FailedRequest([Invalid($"Không tìm thấy tiến trình '{request.ProcessId}'.")]);
        }

        var process = currentState.Processes[processIndex];
        var need = process.MaxDemand.Select((maximum, index) => maximum - process.Allocation[index]).ToArray();
        for (var index = 0; index < request.Amounts.Count; index++)
        {
            if (request.Amounts[index] > need[index])
            {
                return FailedRequest([Invalid($"Request của '{process.Id}' vượt Need tại tài nguyên {currentState.ResourceNames[index]}.")]);
            }
        }

        if (request.Amounts.Where((amount, index) => amount > currentState.Available[index]).Any())
        {
            var currentSafety = FinalSnapshot(currentState);
            return new BankerRequestResult(
                true,
                BankerRequestDisposition.WaitForResources,
                currentSafety.SafeSequence,
                Array.AsReadOnly(currentState.Available.ToArray()),
                Array.Empty<SimulationError>(),
                $"Request của {process.Id} vượt Available hiện tại; tiến trình phải chờ.");
        }

        var availableAfter = currentState.Available
            .Select((amount, index) => amount - request.Amounts[index])
            .ToArray();
        var tentativeProcesses = currentState.Processes.Select((item, index) => index == processIndex
            ? item with
            {
                Allocation = Array.AsReadOnly(item.Allocation
                    .Select((amount, resourceIndex) => amount + request.Amounts[resourceIndex])
                    .ToArray())
            }
            : item).ToArray();
        var tentativeState = currentState with
        {
            Available = Array.AsReadOnly(availableAfter),
            Processes = Array.AsReadOnly(tentativeProcesses)
        };
        var safety = FinalSnapshot(tentativeState);
        var granted = safety.State == BankerRunState.Safe;
        return new BankerRequestResult(
            true,
            granted ? BankerRequestDisposition.Granted : BankerRequestDisposition.WouldBeUnsafe,
            safety.SafeSequence,
            Array.AsReadOnly(availableAfter),
            Array.Empty<SimulationError>(),
            granted
                ? $"Request được cấp thử nghiệm; trạng thái vẫn an toàn: {string.Join(" → ", safety.SafeSequence)}."
                : "Request bị từ chối: trạng thái giả định sau cấp phát không an toàn.");
    }

    private static (BankerSnapshot Initial, IReadOnlyList<SimulationFrame<BankerSnapshot>> Frames) BuildTrace(BankerInput input)
    {
        input = Normalize(input);
        var work = input.Available.ToArray();
        var finished = new bool[input.Processes.Count];
        var safeSequence = new List<string>();
        var events = new List<SimulationEvent>();
        var frames = new List<SimulationFrame<BankerSnapshot>>();
        var initial = CreateSnapshot(0, input, work, finished, safeSequence, BankerRunState.Running, null, events);

        while (safeSequence.Count < input.Processes.Count)
        {
            var candidateIndex = FindRunnableProcess(input, work, finished);
            if (candidateIndex < 0)
            {
                var blocked = input.Processes.Where((_, index) => !finished[index]).Select(process => process.Id);
                var blockedLabel = string.Join(", ", blocked);
                var unsafeEvent = new SimulationEvent(
                    events.Count + 1,
                    safeSequence.Count,
                    "UNSAFE_STATE",
                    $"Không tìm được tiến trình có Need ≤ Work. Chưa hoàn tất: {blockedLabel}.");
                events.Add(unsafeEvent);
                var unsafeSnapshot = CreateSnapshot(
                    safeSequence.Count + 1, input, work, finished, safeSequence, BankerRunState.Unsafe, null, events);
                frames.Add(new SimulationFrame<BankerSnapshot>(unsafeSnapshot, unsafeEvent));
                break;
            }

            var completed = input.Processes[candidateIndex];
            finished[candidateIndex] = true;
            for (var resourceIndex = 0; resourceIndex < work.Length; resourceIndex++)
            {
                work[resourceIndex] += completed.Allocation[resourceIndex];
            }

            safeSequence.Add(completed.Id);
            var state = safeSequence.Count == input.Processes.Count ? BankerRunState.Safe : BankerRunState.Running;
            var stepEvent = new SimulationEvent(
                events.Count + 1,
                safeSequence.Count,
                "SAFE_STEP",
                $"{completed.Id} có thể hoàn tất; trả Allocation về Work.",
                completed.Id);
            events.Add(stepEvent);
            var snapshot = CreateSnapshot(
                safeSequence.Count, input, work, finished, safeSequence, state, completed.Id, events);
            frames.Add(new SimulationFrame<BankerSnapshot>(snapshot, stepEvent));
        }

        return (initial, Array.AsReadOnly(frames.ToArray()));
    }

    private static int FindRunnableProcess(BankerInput input, IReadOnlyList<int> work, IReadOnlyList<bool> finished)
    {
        for (var processIndex = 0; processIndex < input.Processes.Count; processIndex++)
        {
            if (finished[processIndex])
            {
                continue;
            }

            var process = input.Processes[processIndex];
            var canFinish = true;
            for (var resourceIndex = 0; resourceIndex < work.Count; resourceIndex++)
            {
                var need = process.MaxDemand[resourceIndex] - process.Allocation[resourceIndex];
                if (need > work[resourceIndex])
                {
                    canFinish = false;
                    break;
                }
            }

            if (canFinish)
            {
                return processIndex;
            }
        }

        return -1;
    }

    private static BankerSnapshot CreateSnapshot(
        int stepNumber,
        BankerInput input,
        IReadOnlyList<int> work,
        IReadOnlyList<bool> finished,
        IReadOnlyList<string> safeSequence,
        BankerRunState state,
        string? lastCompletedProcessId,
        IReadOnlyList<SimulationEvent> events)
    {
        var processes = input.Processes.Select((process, index) => new BankerProcessSnapshot(
            process.Id,
            Array.AsReadOnly(process.Allocation.ToArray()),
            Array.AsReadOnly(process.MaxDemand.ToArray()),
            Array.AsReadOnly(process.MaxDemand.Select((maximum, resourceIndex) => maximum - process.Allocation[resourceIndex]).ToArray()),
            finished[index])).ToArray();
        return new BankerSnapshot(
            stepNumber,
            Array.AsReadOnly(input.ResourceNames.Select(name => name.Trim()).ToArray()),
            Array.AsReadOnly(input.Available.ToArray()),
            Array.AsReadOnly(work.ToArray()),
            Array.AsReadOnly(processes),
            Array.AsReadOnly(safeSequence.ToArray()),
            state,
            lastCompletedProcessId,
            Array.AsReadOnly(events.ToArray()),
            state != BankerRunState.Running);
    }

    private static BankerSnapshot FinalSnapshot(BankerInput input)
    {
        var trace = BuildTrace(input);
        return trace.Frames.Count == 0 ? trace.Initial : trace.Frames[^1].Snapshot;
    }

    private static BankerRequestResult FailedRequest(IEnumerable<SimulationError> errors) => new(
        false,
        null,
        Array.Empty<string>(),
        Array.Empty<int>(),
        Array.AsReadOnly(errors.ToArray()),
        string.Join(Environment.NewLine, errors.Select(error => error.Message)));

    private static SimulationError Invalid(string message) => new(SimulationErrorCode.InvalidInput, message);

    private static BankerInput Normalize(BankerInput input) => new(
        Array.AsReadOnly(input.ResourceNames.Select(name => name.Trim()).ToArray()),
        Array.AsReadOnly(input.Available.ToArray()),
        Array.AsReadOnly(input.Processes.Select(process => process with
        {
            Id = process.Id.Trim(),
            Allocation = Array.AsReadOnly(process.Allocation.ToArray()),
            MaxDemand = Array.AsReadOnly(process.MaxDemand.ToArray())
        }).ToArray()));
}
