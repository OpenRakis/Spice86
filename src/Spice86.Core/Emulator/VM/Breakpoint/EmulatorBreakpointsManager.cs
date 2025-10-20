namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A class for managing breakpoints in the emulator.
/// </summary>
public sealed class EmulatorBreakpointsManager : ISerializableBreakpointsSource {
    private readonly BreakPointHolder _cycleBreakPoints;
    private readonly BreakPointHolder _executionBreakPoints;
    private readonly State _state;
    private readonly IPauseHandler _pauseHandler;
    private BreakPoint? _machineStartBreakPoint;
    private BreakPoint? _machineStopBreakPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatorBreakpointsManager"/> class.
    /// </summary>
    /// <param name="pauseHandler">The object responsible for pausing and resuming the emulation.</param>
    /// <param name="state">The CPU registers and flags.</param>
    public EmulatorBreakpointsManager(IPauseHandler pauseHandler, State state) {
        _state = state;
        MemoryReadWriteBreakpoints = new();
        IoReadWriteBreakpoints = new();
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

    /// <summary>
    /// Checks the current breakpoints and triggers them if necessary.
    /// </summary>
    public void CheckExecutionBreakPoints() {
        if (!_executionBreakPoints.IsEmpty) {
            uint address;
            // We do a loop here because if breakpoint action modifies the IP address we may miss other breakpoints.
            bool triggered;
            do {
                address = _state.IpPhysicalAddress;
                triggered = _executionBreakPoints.TriggerMatchingBreakPoints(address);
            } while (triggered && address != _state.IpPhysicalAddress);
        }

        if (!_cycleBreakPoints.IsEmpty) {
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

        foreach (SerializableUserBreakpoint breakpoint in GroupConsecutiveBreakpoints(
            memoryReadWriteBreakpoints.SerializableBreakpoints).Distinct()) {
            serializableBreakpoints.Breakpoints.Add(breakpoint);
        }
    }

    private static IEnumerable<SerializableUserBreakpoint> GroupConsecutiveBreakpoints(
        IEnumerable<AddressBreakPoint> memoryBreakpoints) {

        if (!memoryBreakpoints.Any()) {
            yield break;
        }

        foreach (var group in memoryBreakpoints
            .Select((bp, index) => new {
                Breakpoint = bp,
                Index = index,
                GroupKey = bp.Address - index
            })
            .GroupBy(x => new { x.GroupKey, x.Breakpoint.BreakPointType })) {
            IOrderedEnumerable<AddressBreakPoint> breakpoints = group.
                Select(x => x.Breakpoint).OrderBy(x => x.Address);
            long rangeStart = breakpoints.First().Address;
            long rangeEnd = breakpoints.Last().Address;
            BreakPointType type = group.Key.BreakPointType;

            yield return CreateSerializableMemoryBreakpoint(rangeStart, rangeEnd, type);
        }
    }

    private static SerializableUserBreakpoint CreateSerializableMemoryBreakpoint(
        long start, long end, BreakPointType type) {
        if (start == end) {
            return new SerializableUserBreakpoint {
                Trigger = start,
                Type = type,
                IsEnabled = true
            };
        }

        return new SerializableUserBreakpointRange {
            Trigger = start,
            EndTrigger = end,
            Type = type,
            IsEnabled = true
        };
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
            IsEnabled = true
        };
    }

    public void RestoreBreakpoints(SerializableUserBreakpointCollection serializableUserBreakpointCollection) {
        foreach (SerializableUserBreakpoint serializableBreakpoint in serializableUserBreakpointCollection.Breakpoints) {
            IEnumerable<AddressBreakPoint> breakpoints = FromSerializedBreakpoints(serializableBreakpoint);
            foreach (AddressBreakPoint breakpoint in breakpoints) {
                ToggleBreakPoint(breakpoint, true);
            }
        }
    }

    private IEnumerable<AddressBreakPoint> FromSerializedBreakpoints(SerializableUserBreakpoint serializableBreakpoint) {
        Action<BreakPoint> onReached = b => _pauseHandler.RequestPause($"Breakpoint {b.BreakPointType} reached");
        bool removeOnTrigger = false;

        if (serializableBreakpoint is SerializableUserBreakpointRange rangeBreakpoint) {
            for (long i = rangeBreakpoint.Trigger; i < rangeBreakpoint.EndTrigger; i++) {
                yield return FromSerializable(serializableBreakpoint, onReached, removeOnTrigger);
            }
        }

        yield return FromSerializable(serializableBreakpoint, onReached, removeOnTrigger);
    }

    private static AddressBreakPoint FromSerializable(
        SerializableUserBreakpoint serializableBreakpoint,
        Action<BreakPoint> onReached, bool removeOnTrigger) {
        return serializableBreakpoint.Type switch {
            BreakPointType.CPU_EXECUTION_ADDRESS or BreakPointType.CPU_INTERRUPT or BreakPointType.CPU_CYCLES or
            BreakPointType.MEMORY_ACCESS or BreakPointType.MEMORY_READ or BreakPointType.MEMORY_WRITE or
            BreakPointType.IO_ACCESS or BreakPointType.IO_READ or BreakPointType.IO_WRITE
                => new AddressBreakPoint(serializableBreakpoint.Type,
                    serializableBreakpoint.Trigger, onReached, removeOnTrigger),
            BreakPointType.MACHINE_START => throw new NotSupportedException("Emulator start/stop breakpoints don't need to be serialized"),
            BreakPointType.MACHINE_STOP => throw new NotSupportedException("Machine breakpoint are not serialized"),
            _ => throw new InvalidOperationException("Cannot deserialize unrecognized BreakpointType"),
        };
    }
}