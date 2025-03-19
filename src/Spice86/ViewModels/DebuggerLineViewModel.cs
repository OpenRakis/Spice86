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

    public List<BreakpointViewModel> Breakpoints { get; }
    public bool HasBreakpoint => Breakpoints.Count != 0;

    public DebuggerLineViewModel(EnrichedInstruction instruction, State cpuState) {
        _info = instruction.Instruction;
        _cpuState = cpuState;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = instruction.Instruction.IP32; // This is the full 32bit physical/linear address of the instrcution, not the offset part of a segment:offset pair.
        Breakpoints = instruction.Breakpoints;

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
}

/// <summary>
/// Represents a segment of formatted text with its kind.
/// </summary>
public class FormattedTextSegment {
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of text (used for formatting).
    /// </summary>
    public FormatterTextKind Kind { get; set; }
}

/// <summary>
/// Thread-safe formatter output that doesn't create UI elements.
/// </summary>
public class ThreadSafeFormatterOutput : FormatterOutput {
    /// <summary>
    /// Gets the list of formatted text segments.
    /// </summary>
    public List<FormattedTextSegment> Segments { get; } = [];

    /// <summary>
    /// Writes a segment of text with the specified kind.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="kind">The kind of text.</param>
    public override void Write(string text, FormatterTextKind kind) {
        Segments.Add(new FormattedTextSegment {
            Text = text,
            Kind = kind
        });
    }
}