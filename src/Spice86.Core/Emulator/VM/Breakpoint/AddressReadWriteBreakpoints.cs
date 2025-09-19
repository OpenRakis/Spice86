namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Linq;

/// <summary>
/// Manages breakpoints for read / write access to memory
/// </summary>
public class AddressReadWriteBreakpoints {
    private readonly BreakPointHolder _readBreakPoints = new();
    private readonly BreakPointHolder _writeBreakPoints = new();

    /// <summary>
    ///     Enable or disable a memory breakpoint.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to enable or disable</param>
    /// <param name="trigger">Trigger for the breakpoint</param>
    /// <param name="on">true to enable a breakpoint, false to disable it</param>
    /// <exception cref="NotSupportedException"></exception>
    public void ToggleBreakPoint(BreakPoint breakPoint, AddressOperation trigger, bool on) {
        switch (trigger) {
            case AddressOperation.READ:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case AddressOperation.WRITE:
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case AddressOperation.ACCESS:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
        }
    }

    internal IEnumerable<AddressBreakPoint> SerializableBreakpoints => _readBreakPoints.
        SerializableBreakpoints.Concat(_writeBreakPoints.SerializableBreakpoints);

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

}