namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

using System.Linq;

/// <summary>
/// A class for managing breakpoints in the emulator.
/// </summary>
public sealed class EmulatorBreakpointsManager : ISerializableBreakpointsSource {
    private readonly BreakPointHolder _cycleBreakPoints;
    private readonly BreakPointHolder _executionBreakPoints;
    private readonly State _state;
    private readonly IPauseHandler _pauseHandler;
    private readonly Memory.IMemory _memory;
    private BreakPoint? _machineStartBreakPoint;
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatorBreakpointsManager"/> class.
    /// </summary>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="memory">The memory interface for expression evaluation in conditional breakpoints.</param>
    /// <param name="memoryReadWriteBreakpoints">The breakpoint holder for memory read/write breakpoints.</param>
    /// <param name="ioReadWriteBreakpoints">The breakpoint holder for I/O read/write breakpoints.</param>
    public EmulatorBreakpointsManager(IPauseHandler pauseHandler, State state, Memory.IMemory memory,
        AddressReadWriteBreakpoints memoryReadWriteBreakpoints, AddressReadWriteBreakpoints ioReadWriteBreakpoints) {
        _state = state;
        _memory = memory;
        MemoryReadWriteBreakpoints = memoryReadWriteBreakpoints;
        IoReadWriteBreakpoints = ioReadWriteBreakpoints;
        InterruptBreakPoints = new();
        _cycleBreakPoints = new();
        _executionBreakPoints = new();
        _pauseHandler = pauseHandler;
    }

    public AddressReadWriteBreakpoints MemoryReadWriteBreakpoints { get; }

    public AddressReadWriteBreakpoints IoReadWriteBreakpoints { get; }

    public BreakPointHolder InterruptBreakPoints { get; }

    /// <summary>
    /// Called when the machine starts.
    /// </summary>
    public void OnMachineStart() {
        WaitSingleBreakpoint(_machineStartBreakPoint);
    }

    /// <summary>
    /// Called when the machine stops.
    /// </summary>
    public void OnMachineStop() {
        WaitSingleBreakpoint(_machineStopBreakPoint);
    }

    private void WaitSingleBreakpoint(BreakPoint? breakPoint) {
        if (breakPoint is not null) {
            breakPoint.Trigger();
            _pauseHandler.WaitIfPaused();
        }
    }

    /// <summary>
    /// Toggles a breakpoint on or off.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="on">True to turn the breakpoint on, false to turn it off.</param>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType breakPointType = breakPoint.BreakPointType;
        switch (breakPointType) {
            case BreakPointType.CPU_EXECUTION_ADDRESS:
                _executionBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.CPU_CYCLES:
                _cycleBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.CPU_INTERRUPT:
                InterruptBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.MACHINE_START:
                _machineStartBreakPoint = breakPoint;
                break;
            case BreakPointType.MACHINE_STOP:
                _machineStopBreakPoint = breakPoint;
                break;
            case BreakPointType.MEMORY_READ:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, on);
                break;
            case BreakPointType.MEMORY_WRITE:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.WRITE, on);
                break;
            case BreakPointType.MEMORY_ACCESS:
                MemoryReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.ACCESS, on);
                break;
            case BreakPointType.IO_READ:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, on);
                break;
            case BreakPointType.IO_WRITE:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.WRITE, on);
                break;
            case BreakPointType.IO_ACCESS:
                IoReadWriteBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.ACCESS, on);
                break;
        }
    }

    public bool HasActiveBreakpoints =>
        _executionBreakPoints.HasActiveBreakpoints || _cycleBreakPoints.HasActiveBreakpoints;

    /// <summary>
    /// Checks the current breakpoints and triggers them if necessary.
    /// </summary>
    public void TriggerBreakpoints() {
        if (_executionBreakPoints.HasActiveBreakpoints) {
            uint address;
            // We do a loop here because if breakpoint action modifies the IP address we may miss other breakpoints.
            bool triggered;
            do {
                address = _state.IpPhysicalAddress;
                triggered = _executionBreakPoints.TriggerMatchingBreakPoints(address);
            } while (triggered && address != _state.IpPhysicalAddress);
        }

        if (_cycleBreakPoints.HasActiveBreakpoints) {
            long cycles = _state.Cycles;
            _cycleBreakPoints.TriggerMatchingBreakPoints(cycles);
        }
    }

    public SerializableUserBreakpointCollection CreateSerializableBreakpoints() {
        var serializableBreakpoints = new SerializableUserBreakpointCollection();
        AddBreakpointsToCollection(serializableBreakpoints, _executionBreakPoints.SerializableBreakpoints);
        AddBreakpointsToCollection(serializableBreakpoints, InterruptBreakPoints.SerializableBreakpoints);
        AddBreakpointsToCollection(serializableBreakpoints, _cycleBreakPoints.SerializableBreakpoints);
        AddBreakpointsToCollection(serializableBreakpoints, MemoryReadWriteBreakpoints);
        AddBreakpointsToCollection(serializableBreakpoints, IoReadWriteBreakpoints.SerializableBreakpoints);
        return serializableBreakpoints;
    }

    private void AddBreakpointsToCollection(SerializableUserBreakpointCollection serializableBreakpoints,
        AddressReadWriteBreakpoints memoryReadWriteBreakpoints) {
        if (!memoryReadWriteBreakpoints.SerializableBreakpoints.Any()) {
            return;
        }

        foreach (SerializableUserBreakpoint breakpoint in MergeConsecutiveMemoryBreakpoints(
            memoryReadWriteBreakpoints.SerializableBreakpoints)) {
            serializableBreakpoints.Breakpoints.Add(breakpoint);
        }
    }

    private static List<SerializableUserBreakpoint> MergeConsecutiveMemoryBreakpoints(
        IEnumerable<AddressBreakPoint> memoryBreakpoints) {
        List<AddressBreakPoint> sorted = memoryBreakpoints
            .GroupBy(bp => bp.Address)
            .Select(g => g.First())
            .OrderBy(bp => bp.Address)
            .ToList();

        List<SerializableUserBreakpoint> result = [];

        for (int i = 0; i < sorted.Count; i++) {
            long start = sorted[i].Address;
            long end = start;
            BreakPointType type = sorted[i].BreakPointType;

            while (i + 1 < sorted.Count && 
                   sorted[i + 1].Address == end + 1 && 
                   sorted[i + 1].BreakPointType == type) {
                end = sorted[++i].Address;
            }

            SerializableUserBreakpoint item = new SerializableUserBreakpoint {
                Trigger = start,
                EndTrigger = end,
                Type = type,
                IsEnabled = sorted[i].IsEnabled,
                ConditionExpression = sorted[i].ConditionExpression
            };
            result.Add(item);
        }
        return result;
    }

    private static void AddBreakpointsToCollection(SerializableUserBreakpointCollection collection,
        IEnumerable<AddressBreakPoint> serializableBreakpoints) {
        foreach (AddressBreakPoint bp in serializableBreakpoints) {
            SerializableUserBreakpoint? serializableUserBreakpoint = ToSerializable(bp);
            if (serializableUserBreakpoint is not null &&
                !collection.Breakpoints.Any(x => x == serializableUserBreakpoint)) {
                collection.Breakpoints.Add(serializableUserBreakpoint);
            }
        }
    }

    private static SerializableUserBreakpoint? ToSerializable(AddressBreakPoint breakpoint) {
        return new SerializableUserBreakpoint {
            Trigger = breakpoint.Address,
            Type = breakpoint.BreakPointType,
            IsEnabled = breakpoint.IsEnabled,
            ConditionExpression = breakpoint.ConditionExpression
        };
    }
    public void RestoreBreakpoints(SerializableUserBreakpointCollection serializableUserBreakpointCollection) {
        foreach (SerializableUserBreakpoint serializableBreakpoint in serializableUserBreakpointCollection.Breakpoints) {
            IEnumerable<AddressBreakPoint> breakpoints = FromSerializedBreakpoints(serializableBreakpoint);
            foreach (AddressBreakPoint breakpoint in breakpoints) {
                ToggleBreakPoint(breakpoint, serializableBreakpoint.IsEnabled);
            }
        }
    }

    private IEnumerable<AddressBreakPoint> FromSerializedBreakpoints(SerializableUserBreakpoint serializableBreakpoint) {
        Action<BreakPoint> onReached = b => _pauseHandler.RequestPause($"Breakpoint {b.BreakPointType} reached");
        bool removeOnTrigger = false;

        // If it's a range (end != start), expand inclusively; otherwise single
        if (serializableBreakpoint.EndTrigger != serializableBreakpoint.Trigger) {
            for (long i = serializableBreakpoint.Trigger; i <= serializableBreakpoint.EndTrigger; i++) {
                SerializableUserBreakpoint single = serializableBreakpoint with { Trigger = i, EndTrigger = i };
                yield return FromSerializable(single, onReached, removeOnTrigger);
            }
        } else {
            yield return FromSerializable(serializableBreakpoint, onReached, removeOnTrigger);
        }
    }

    private AddressBreakPoint FromSerializable(
        SerializableUserBreakpoint serializableBreakpoint,
        Action<BreakPoint> onReached, bool removeOnTrigger) {
        Func<long, bool>? condition = null;
        string? conditionExpression = serializableBreakpoint.ConditionExpression;
        
        // Compile the condition expression if present
        if (!string.IsNullOrWhiteSpace(conditionExpression)) {
            try {
                Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser parser = new();
                Shared.Emulator.VM.Breakpoint.Expression.IExpressionNode ast = parser.Parse(conditionExpression);
                condition = (address) => {
                    BreakpointExpressionContext context = new(_state, _memory, address);
                    return ast.Evaluate(context) != 0;
                };
            } catch (ArgumentException) {
                // If parsing fails, treat as unconditional
                conditionExpression = null;
            }
        }
        
        return serializableBreakpoint.Type switch {
            BreakPointType.CPU_EXECUTION_ADDRESS or BreakPointType.CPU_INTERRUPT or BreakPointType.CPU_CYCLES or
            BreakPointType.MEMORY_ACCESS or BreakPointType.MEMORY_READ or BreakPointType.MEMORY_WRITE or
            BreakPointType.IO_ACCESS or BreakPointType.IO_READ or BreakPointType.IO_WRITE
                => new AddressBreakPoint(serializableBreakpoint.Type,
                    serializableBreakpoint.Trigger, onReached, removeOnTrigger, condition, conditionExpression) {
                    IsEnabled = serializableBreakpoint.IsEnabled,
                    IsUserBreakpoint = true
                },
            BreakPointType.MACHINE_START => throw new NotSupportedException("Emulator start/stop breakpoints don't need to be serialized"),
            BreakPointType.MACHINE_STOP => throw new NotSupportedException("Machine breakpoint are not serialized"),
            _ => throw new InvalidOperationException("Cannot deserialize unrecognized BreakpointType"),
        };
    }

    public void RemoveUserBreakpoint(BreakPoint breakPoint) {
        ToggleBreakPoint(breakPoint, false);
    }
}