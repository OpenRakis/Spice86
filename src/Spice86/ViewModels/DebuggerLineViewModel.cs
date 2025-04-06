namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

using System.Text.Json;

/// <summary>
///     View model for a line in the debugger.
/// </summary>
public partial class DebuggerLineViewModel : ViewModelBase {
    private readonly BreakpointsViewModel? _breakpointsViewModel;
    private readonly State _cpuState;

    private readonly Formatter _formatter = new MasmFormatter(new FormatterOptions {
        AddLeadingZeroToHexNumbers = false,
        AlwaysShowSegmentRegister = true,
        HexPrefix = "0x",
        MasmSymbolDisplInBrackets = false
    });

    private readonly Instruction _info;

    [ObservableProperty]
    private BreakpointViewModel? _breakpoint;

    [ObservableProperty]
    private bool _isCurrentInstruction;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool? _willJump;

    public DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState, BreakpointsViewModel? breakpointsViewModel = null) {
        _info = instruction.Instruction;
        _cpuState = cpuState;
        _breakpointsViewModel = breakpointsViewModel;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        NextAddress = (uint)(Address + _info.Length);

        // We expect there to be at most 1 execution breakpoint per line, so we use SingleOrDefault.
        Breakpoint = instruction.Breakpoints.SingleOrDefault(breakpoint => breakpoint.Type == BreakPointType.CPU_EXECUTION_ADDRESS);

        // Generate the formatted disassembly text
        GenerateFormattedDisassembly();
    }

    public string ByteString { get; }
    public FunctionInformation? Function { get; }
    public SegmentedAddress SegmentedAddress { get; }

    /// <summary>
    ///     The physical address of this instruction in memory.
    /// </summary>
    public uint Address { get; }

    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public uint NextAddress { get; private set; }

    public string Disassembly => _info.ToString();

    /// <summary>
    ///     Gets a collection of formatted text segments for the disassembly with syntax highlighting.
    /// </summary>
    public List<FormattedTextSegment> DisassemblySegments { get; private set; } = [];

    public void ApplyCpuState() {
        // Initialize WillJump to null for non-conditional branches
        WillJump = null;

        if (_info.FlowControl == FlowControl.ConditionalBranch) {
            WillJump = _info.ConditionCode switch {
                ConditionCode.None => false,
                ConditionCode.o => _cpuState.OverflowFlag,
                ConditionCode.no => !_cpuState.OverflowFlag,
                ConditionCode.b => _cpuState.CarryFlag,
                ConditionCode.ae => !_cpuState.CarryFlag,
                ConditionCode.e => _cpuState.ZeroFlag,
                ConditionCode.ne => !_cpuState.ZeroFlag,
                ConditionCode.be => _cpuState.CarryFlag || _cpuState.ZeroFlag,
                ConditionCode.a => !_cpuState.CarryFlag && !_cpuState.ZeroFlag,
                ConditionCode.s => _cpuState.SignFlag,
                ConditionCode.ns => !_cpuState.SignFlag,
                ConditionCode.p => _cpuState.ParityFlag,
                ConditionCode.np => !_cpuState.ParityFlag,
                ConditionCode.l => _cpuState.SignFlag != _cpuState.OverflowFlag,
                ConditionCode.ge => _cpuState.SignFlag == _cpuState.OverflowFlag,
                ConditionCode.le => _cpuState.ZeroFlag || _cpuState.SignFlag != _cpuState.OverflowFlag,
                ConditionCode.g => !_cpuState.ZeroFlag && _cpuState.SignFlag == _cpuState.OverflowFlag,
                _ => throw new ArgumentOutOfRangeException()
            };
            if (WillJump.HasValue && WillJump.Value) {
                if (_info.IsJccShortOrNear) {
                    // If the instruction is a conditional branch, calculate the next address based on the jump target
                    ushort segment = _cpuState.CS;
                    uint offset = (uint)_info.NearBranchTarget;
                    NextAddress = (uint)((segment << 4) + offset);
                }
            }
        } else if (_info.FlowControl == FlowControl.UnconditionalBranch) {
            // If the instruction is an unconditional branch, set the next address to the target address
            if (_info.IsJmpShortOrNear) {
                ushort segment = _cpuState.CS;
                uint offset = (uint)_info.NearBranchTarget;
                NextAddress = (uint)((segment << 4) + offset);
            } else if (_info.IsJmpFar) {
                Console.WriteLine("***** DEBUG *****");
                Console.WriteLine("Far jump detected");
                Console.WriteLine(JsonSerializer.Serialize(_info));
                Console.WriteLine("****************");
            }
        }
    }

    /// <summary>
    ///     Updates the IsCurrentInstruction property based on the current CPU state.
    /// </summary>
    public void UpdateIsCurrentInstruction() {
        bool newValue = Address == _cpuState.IpPhysicalAddress;
        IsCurrentInstruction = newValue;
    }

    /// <summary>
    ///     Generates a formatted representation of the disassembly with syntax highlighting.
    /// </summary>
    private void GenerateFormattedDisassembly() {
        var output = new FormattedTextSegmentsOutput();
        _formatter.Format(_info, output);
        DisassemblySegments = output.Segments;
    }

    /// <summary>
    ///     Gets the current breakpoint for this line from the BreakpointsViewModel.
    /// </summary>
    /// <returns>The breakpoint if one exists for this address, otherwise null.</returns>
    public BreakpointViewModel? GetBreakpointFromViewModel() {
        // Find a breakpoint in the BreakpointsViewModel that matches this line's address
        return _breakpointsViewModel?.Breakpoints.FirstOrDefault(bp => bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS && (uint)bp.Address == Address);
    }

    /// <summary>
    ///     Updates the Breakpoint property with the current value from the BreakpointsViewModel.
    /// </summary>
    public void UpdateBreakpointFromViewModel() {
        if (_breakpointsViewModel != null) {
            Breakpoint = GetBreakpointFromViewModel();
        }
    }

    public override string ToString() {
        return $"{SegmentedAddress} {Disassembly} [{ByteString}]";
    }
}