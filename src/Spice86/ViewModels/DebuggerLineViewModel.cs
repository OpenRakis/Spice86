namespace Spice86.ViewModels;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A view model for a line in the disassembly view.
/// </summary>
/// <param name="instruction">Information about the instruction</param>
/// <param name="cpuState">Information about the current state of the CPU</param>
public class DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState) : ViewModelBase {
    private readonly Instruction _info = instruction.Instruction;
    public string ByteString => string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
    public FunctionInformation? Function => instruction.Function;
    public SegmentedAddress SegmentedAddress => instruction.SegmentedAddress;
    public uint Address => _info.IP32;
    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public uint NextAddress => _info.NextIP32;
    public string Disassembly => _info.ToString();
    public bool IsCurrentInstruction => Address == cpuState.IpPhysicalAddress;
    public bool HasBreakpoint => Breakpoints.Count != 0;

    public bool IsSelected { get; set; }
    public List<BreakpointViewModel> Breakpoints => instruction.Breakpoints;
}