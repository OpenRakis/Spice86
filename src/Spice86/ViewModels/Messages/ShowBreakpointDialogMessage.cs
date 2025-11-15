namespace Spice86.ViewModels.Messages;

using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels.ValueViewModels.Debugging;

/// <summary>
/// Message to request showing the breakpoint creation dialog.
/// </summary>
/// <param name="DebuggerLine">The debugger line where the breakpoint should be created.</param>
/// <param name="Address">The address where the breakpoint should be created.</param>
public record ShowBreakpointDialogMessage(DebuggerLineViewModel DebuggerLine, SegmentedAddress Address);
