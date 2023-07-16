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

    public void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    public void MonitorWriteAccess(uint address, byte value) {
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
    }

    public void MonitorRangeReadAccess(uint startAddress, uint endAddress) {
        _readBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    public void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

}