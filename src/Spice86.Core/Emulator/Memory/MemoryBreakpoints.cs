namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Manages memory breakpoints
/// </summary>
public class MemoryBreakpoints {
    private readonly BreakPointHolder _readBreakPoints = new();
    private readonly BreakPointHolder _writeBreakPoints = new();

    /// <summary>
    ///     Enable or disable a memory breakpoint.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to enable or disable</param>
    /// <param name="on">true to enable a breakpoint, false to disable it</param>
    /// <exception cref="NotSupportedException"></exception>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType type = breakPoint.BreakPointType;
        switch (type) {
            case BreakPointType.READ:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.WRITE:
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.ACCESS:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.EXECUTION:
            case BreakPointType.CYCLES:
            case BreakPointType.MACHINE_STOP:
            default:
                throw new NotSupportedException($"Trying to add unsupported breakpoint of type {type}");
        }
    }

    /// <summary>
    /// Triggers all the breakpoints matching the specified address, if the memory was read.
    /// </summary>
    /// <param name="address">The address to match.</param>
    public void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    /// <summary>
    /// Triggers all the breakpoints matching the specified address, if the memory was written to.
    /// </summary>
    /// <param name="address">The address to match.</param>
    public void MonitorWriteAccess(uint address) {
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
    }

    /// <summary>
    /// Triggers all the read breakpoints matching the specified memory range.
    /// </summary>
    /// <param name="startAddress">The start of the range.</param>
    /// <param name="endAddress">The inclusive end of the range.</param>
    public void MonitorRangeReadAccess(uint startAddress, uint endAddress) {
        _readBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    /// <summary>
    /// Triggers all the write breakpoints matching the specified memory range.
    /// </summary>
    /// <param name="startAddress">The start of the range.</param>
    /// <param name="endAddress">The inclusive end of the range.</param>
    public void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

}