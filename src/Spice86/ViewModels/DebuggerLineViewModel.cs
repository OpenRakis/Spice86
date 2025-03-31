namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

using System.Linq;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

/// <summary>
/// View model for a line in the debugger.
/// </summary>
public partial class DebuggerLineViewModel : ViewModelBase {
    private readonly Instruction _info;
    private readonly State _cpuState;
    private readonly BreakpointsViewModel? _breakpointsViewModel;
    private bool _isCurrentInstruction;

    public string ByteString { get; }
    public FunctionInformation? Function { get; }
    public SegmentedAddress SegmentedAddress { get; }

    /// <summary>
    /// The physical address of this instruction in memory.
    /// </summary>
    public uint Address { get; }

    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public uint NextAddress => (uint)(Address + _info.Length);
    public string Disassembly => _info.ToString();

    /// <summary>
    /// Gets a collection of formatted text segments for the disassembly with syntax highlighting.
    /// </summary>
    public List<FormattedTextSegment> DisassemblySegments { get; private set; } = [];

    public bool IsSelected { get; set; }

    public bool IsCurrentInstruction {
        get => _isCurrentInstruction;
        private set {
            if (_isCurrentInstruction != value) {
                _isCurrentInstruction = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private BreakpointViewModel? _breakpoint;

    public DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState, BreakpointsViewModel? breakpointsViewModel = null) {
        _info = instruction.Instruction;
        _cpuState = cpuState;
        _breakpointsViewModel = breakpointsViewModel;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        // We expect there to be at most 1 execution breakpoint per line, so we use SingleOrDefault.
        Breakpoint = instruction.Breakpoints.SingleOrDefault(breakpoint => breakpoint.Type == BreakPointType.CPU_EXECUTION_ADDRESS);

        // Generate the formatted disassembly text
        GenerateFormattedDisassembly();
    }

    /// <summary>
    /// Updates the IsCurrentInstruction property based on the current CPU state.
    /// </summary>
    public void UpdateIsCurrentInstruction() {
        bool newValue = Address == _cpuState.IpPhysicalAddress;
        IsCurrentInstruction = newValue;
    }

    /// <summary>
    /// Generates a formatted representation of the disassembly with syntax highlighting.
    /// </summary>
    private void GenerateFormattedDisassembly() {
        var output = new ThreadSafeFormatterOutput();
        _formatter.Format(_info, output);
        DisassemblySegments = output.Segments;
    }

    private readonly Formatter _formatter = new MasmFormatter(new FormatterOptions {
        AddLeadingZeroToHexNumbers = false,
        AlwaysShowSegmentRegister = true,
        HexPrefix = "0x",
        MasmSymbolDisplInBrackets = false,
    });

    /// <summary>
    /// Gets the current breakpoint for this line from the BreakpointsViewModel.
    /// </summary>
    /// <returns>The breakpoint if one exists for this address, otherwise null.</returns>
    public BreakpointViewModel? GetBreakpointFromViewModel() {
        // Find a breakpoint in the BreakpointsViewModel that matches this line's address
        return _breakpointsViewModel?.Breakpoints.FirstOrDefault(bp =>
            bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS && 
            (uint)bp.Address == Address);
    }

    /// <summary>
    /// Updates the Breakpoint property with the current value from the BreakpointsViewModel.
    /// </summary>
    public void UpdateBreakpointFromViewModel() {
        if (_breakpointsViewModel != null) {
            Breakpoint = GetBreakpointFromViewModel();
        }
    }
}