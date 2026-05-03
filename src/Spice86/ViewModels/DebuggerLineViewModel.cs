namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
using Spice86.DebuggerKnowledgeBase;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;
using Spice86.ViewModels.TextPresentation;
using Spice86.ViewModels.ValueViewModels.Debugging;

/// <summary>
///     View model for a line in the debugger.
/// </summary>
public partial class DebuggerLineViewModel : ViewModelBase {
    private readonly BreakpointsViewModel? _breakpointsViewModel;
    private readonly DebuggerDecoderService? _debuggerDecoderService;

    private readonly Formatter _formatter = new MasmFormatter(new FormatterOptions {
        AddLeadingZeroToHexNumbers = false,
        AlwaysShowSegmentRegister = true,
        HexPrefix = "0x",
        MasmSymbolDisplInBrackets = false
    });

    private readonly Instruction _info;
    private readonly List<FormattedTextToken>? _customFormattedInstruction;

    [ObservableProperty]
    private BreakpointViewModel? _breakpoint;

    [ObservableProperty]
    private bool _isCurrentInstruction;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private List<FormattedTextToken>? _evaluatedOperands;

    [ObservableProperty]
    private List<JumpArcSegment> _jumpArcSegments = [];

    [ObservableProperty]
    private int _maxJumpLanes;

    [ObservableProperty]
    private bool? _willJump;

    public DebuggerLineViewModel(EnrichedInstruction instruction, BreakpointsViewModel? breakpointsViewModel = null, DebuggerDecoderService? debuggerDecoderService = null) {
        _info = instruction.Instruction;
        _breakpointsViewModel = breakpointsViewModel;
        _debuggerDecoderService = debuggerDecoderService;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = MemoryUtils.ToPhysicalAddress(SegmentedAddress.Segment, SegmentedAddress.Offset);
        ushort nextOffset = (ushort)(SegmentedAddress.Offset + _info.Length);
        NextAddress = MemoryUtils.ToPhysicalAddress(SegmentedAddress.Segment, nextOffset);
        _customFormattedInstruction = instruction.InstructionFormatOverride;

        // We expect there to be at most 1 execution breakpoint per line, so we use SingleOrDefault.
        Breakpoint = instruction.Breakpoints.SingleOrDefault(breakpoint => breakpoint.Type == BreakPointType.CPU_EXECUTION_ADDRESS);

        // Compute branch target for direct branches/calls
        BranchTarget = ComputeBranchTarget();
        BranchTargetText = FormatBranchTargetText();

        // Generate the formatted disassembly text
        GenerateFormattedDisassembly();

        // Decode high-level call info and emulator-provided status
        (DecodedCall, IsEmulatorProvided, EmulatorProvidedFunctionName) = ComputeDecodedInfo();
    }

    public string ByteString { get; }
    public FunctionInformation? Function { get; }
    public SegmentedAddress SegmentedAddress { get; }

    /// <summary>
    ///     Exposes the Iced.Intel instruction for operand evaluation.
    /// </summary>
    public Instruction InstructionInfo => _info;

    /// <summary>
    ///     The physical address of this instruction in memory.
    /// </summary>
    public uint Address { get; }

    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public bool IsConditionalBranch => _info.FlowControl == FlowControl.ConditionalBranch;
    public bool IsUnconditionalBranch => _info.FlowControl == FlowControl.UnconditionalBranch;
    public uint NextAddress { get; private set; }

    /// <summary>
    ///     The segmented address of the branch target for direct branches/calls, or null if not applicable.
    /// </summary>
    public SegmentedAddress? BranchTarget { get; }

    /// <summary>
    ///     Formatted text showing branch direction and target address (e.g. "↓ 1000:0042").
    /// </summary>
    public string? BranchTargetText { get; }

    public string Disassembly => _customFormattedInstruction != null
        ? string.Join(' ', _customFormattedInstruction.Select(textOffset => textOffset.Text))
        : _info.ToString();

    /// <summary>
    ///     Gets a collection of formatted text offsets for the disassembly with syntax highlighting.
    /// </summary>
    public List<FormattedTextToken> DisassemblyTextOffsets { get; private set; } = [];

    /// <summary>
    ///     Generates a formatted representation of the disassembly with syntax highlighting.
    /// </summary>
    private void GenerateFormattedDisassembly() {
        if (_customFormattedInstruction != null) {
            // Use custom formatting for special opcodes
            DisassemblyTextOffsets = _customFormattedInstruction;
        } else {
            // Use standard Iced formatting for normal instructions
            var output = new FormattedTextTokensOutput();
            _formatter.Format(_info, output);
            DisassemblyTextOffsets = output.TextOffsets;
        }
    }

    /// <summary>
    ///     Computes the branch target segmented address for direct branches and calls.
    /// </summary>
    private SegmentedAddress? ComputeBranchTarget() {
        switch (_info.FlowControl) {
            case FlowControl.ConditionalBranch:
            case FlowControl.UnconditionalBranch:
            case FlowControl.Call:
                if (_info.Op0Kind == OpKind.NearBranch16) {
                    return new SegmentedAddress(SegmentedAddress.Segment, _info.NearBranch16);
                }
                if (_info.Op0Kind == OpKind.FarBranch16) {
                    return new SegmentedAddress(_info.FarBranchSelector, _info.FarBranch16);
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    ///     Formats the branch target text with a direction arrow.
    /// </summary>
    private string? FormatBranchTargetText() {
        if (BranchTarget is not { } target) {
            return null;
        }
        uint targetPhysical = MemoryUtils.ToPhysicalAddress(target.Segment, target.Offset);
        char arrow = targetPhysical < Address ? '\u2191' : '\u2193';
        return $"{arrow} {target}";
    }

    /// <summary>
    ///     Updates the Breakpoint property with the current value from the BreakpointsViewModel.
    /// </summary>
    public void UpdateBreakpointFromViewModel() {
        if (_breakpointsViewModel != null) {
            Breakpoint = _breakpointsViewModel.GetExecutionBreakPointsAtAddress(Address).FirstOrDefault();
        }
    }

    /// <summary>
    /// Decoded high-level call information when this instruction is decodable (INT, routine entry).
    /// Null for plain instructions.
    /// </summary>
    public DecodedCall? DecodedCall { get; }

    /// <summary>
    /// True when this instruction's address is the entry point of an emulator-provided function.
    /// </summary>
    public bool IsEmulatorProvided { get; }

    /// <summary>
    /// Function name when <see cref="IsEmulatorProvided"/> is true; null otherwise.
    /// </summary>
    public string? EmulatorProvidedFunctionName { get; }

    /// <summary>
    /// Computes decoded call info and emulator-provided status for this instruction.
    /// </summary>
    private (DecodedCall? decodedCall, bool isEmulatorProvided, string? emulatorProvidedFunctionName) ComputeDecodedInfo() {
        if (_debuggerDecoderService == null) {
            return (null, false, null);
        }

        DecodedCall? decoded = null;

        // Try decoding INT instructions
        if (_info.Mnemonic == Mnemonic.Int && _info.Immediate8 is byte vector) {
            _debuggerDecoderService.TryDecodeInterrupt(vector, out decoded);
        }

        // Try decoding emulator-provided routine entry points
        if (decoded == null) {
            _debuggerDecoderService.TryDecodeAsmRoutine(SegmentedAddress, out decoded);
        }

        // Check if this is an emulator-provided function entry point
        bool isEmulatorProvided = _debuggerDecoderService.IsEmulatorProvidedEntryPoint(SegmentedAddress);
        string? functionName = null;
        if (isEmulatorProvided && _debuggerDecoderService.TryGetEmulatorProvidedFunction(SegmentedAddress, out FunctionInformation? info)) {
            functionName = info.Name;
        }

        return (decoded, isEmulatorProvided, functionName);
    }

    public override string ToString() {
        return $"{SegmentedAddress} {Disassembly} [{ByteString}]";
    }
}