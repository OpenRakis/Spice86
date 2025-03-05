namespace Spice86.ViewModels;

using Iced.Intel;
using System.Linq;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;
using System.Collections.Generic;

/// <summary>
/// View model for a line in the debugger.
/// </summary>
public class DebuggerLineViewModel : ViewModelBase {
    private readonly Instruction _info;
    private readonly State _cpuState;
    private bool _isCurrentInstruction;

    public string ByteString { get; }
    public FunctionInformation? Function { get; }
    public SegmentedAddress SegmentedAddress { get; }
    public long Address { get; }
    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public long NextAddress => _info.NextIP32;
    public string Disassembly => _info.ToString();
    public bool IsSelected { get; set; }
    public List<BreakpointViewModel> Breakpoints { get; }
    public bool HasBreakpoint => Breakpoints.Count != 0;

    public bool IsCurrentInstruction {
        get => _isCurrentInstruction;
        private set {
            if (_isCurrentInstruction != value) {
                _isCurrentInstruction = value;
                OnPropertyChanged();
            }
        }
    }

    public DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState) {
        _info = instruction.Instruction;
        _cpuState = cpuState;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = instruction.Instruction.IP32;
        Breakpoints = instruction.Breakpoints;
    }
    
    /// <summary>
    /// Updates the IsCurrentInstruction property based on the current CPU state.
    /// </summary>
    public void UpdateIsCurrentInstruction() {
        // Get the current state from the CPU
        bool newValue = Address == _cpuState.IpPhysicalAddress;
        
        // Log the current and new values
        Console.WriteLine($"DebuggerLine at {Address:X8}: IsCurrentInstruction changing from {_isCurrentInstruction} to {newValue} (CPU IP: {_cpuState.IpPhysicalAddress:X8})");
        
        // Update the property
        IsCurrentInstruction = newValue;
    }
}