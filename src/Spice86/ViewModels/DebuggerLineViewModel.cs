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
    public uint NextAddress => _info.NextIP32;
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

    partial void OnBreakpointChanged(BreakpointViewModel? oldValue, BreakpointViewModel? newValue) {
        // Unsubscribe from the old breakpoint
        if (oldValue != null) {
            oldValue.PropertyChanged -= Breakpoint_PropertyChanged;
        }

        // Subscribe to the new breakpoint
        if (newValue != null) {
            newValue.PropertyChanged += Breakpoint_PropertyChanged;
        }
    }

    private void Breakpoint_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        // When the IsEnabled property changes, notify that the Breakpoint property has changed
        // This will cause the UI to re-evaluate the binding and update the color
        if (e.PropertyName == nameof(BreakpointViewModel.IsEnabled)) {
            OnPropertyChanged(nameof(Breakpoint));
        }
    }

    public DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState) {
        _info = instruction.Instruction;
        _cpuState = cpuState;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = instruction.Instruction.IP32; // This is the full 32bit physical/linear address of the instruction, not the offset part of a segment:offset pair.
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
        SignedImmediateOperands = true,
    });

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    public void Cleanup() {
        // Unsubscribe from breakpoint property changes
        if (Breakpoint != null) {
            Breakpoint.PropertyChanged -= Breakpoint_PropertyChanged;
        }
    }
}