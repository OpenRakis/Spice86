namespace Spice86.Core.Emulator.CPU.CfgCpu.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.StateSerialization;

using System.IO;

/// <summary>
/// Logs every encountered CPU instruction to a file for debugging purposes with registers values before instruction
/// execution.
/// Format: SegmentedAddress InstructionString EAX:... EBX:... ... C0 Z0 S0 O0 I1
/// </summary>
public sealed class CpuHeavyLogger : IDisposable {
    private readonly StreamWriter _writer;
    private readonly NodeToString _nodeToString;
    private readonly State _state;
    private readonly AsmRenderingConfig _config;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuHeavyLogger"/> class.
    /// </summary>
    /// <param name="emulatorStateSerializationFolder">Where to write the file.</param>
    /// <param name="customFilePath">Optional custom file path. If null, uses {DumpDirectory}/cpu_heavy.log</param>
    /// <param name="nodeToString">The node renderer to use.</param>
    /// <param name="state">The CPU state to access registers and flags.</param>
    /// <param name="config">The ASM rendering config to control formatting options.</param>
    public CpuHeavyLogger(EmulatorStateSerializationFolder emulatorStateSerializationFolder, string? customFilePath, NodeToString nodeToString, State state, AsmRenderingConfig config) {
        _nodeToString = nodeToString;
        _state = state;
        _config = config;
        string logFilePath = customFilePath ?? Path.Join(emulatorStateSerializationFolder.Folder, "cpu_heavy.log");
        
        // Ensure directory exists
        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        // Create StreamWriter with buffering for performance
        // AutoFlush is set to true to ensure data is written even if the program crashes
        _writer = new StreamWriter(logFilePath, append: false, System.Text.Encoding.UTF8, bufferSize: 65536) {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    /// <summary>
    /// Logs an executed instruction to the file.
    /// Format: SegmentedAddress InstructionString EAX:... EBX:... ... C0 Z0 S0 O0 I1
    /// </summary>
    /// <param name="node">The CFG node representing the executed instruction.</param>
    public void LogInstruction(ICfgNode node) {
        if (_disposed) {
            return;
        }

        string addressAndInstruction = _nodeToString.ToAssemblyStringWithAddress(node).PadRight(_config.AddressAndInstructionRightPadding);
        string registersAndFlags = GenerateRegistersAndFlagsString();
        _writer.WriteLine($"{addressAndInstruction}{registersAndFlags}");
    }

    /// <summary>
    /// Generates the registers and flags part of the log.
    /// </summary>
    /// <returns>Formatted string with registers and flags.</returns>
    private string GenerateRegistersAndFlagsString() {
        string registers = $"EAX:{_state.EAX:X8} " +
                           $"EBX:{_state.EBX:X8} " +
                           $"ECX:{_state.ECX:X8} " +
                           $"EDX:{_state.EDX:X8} " +
                           $"ESI:{_state.ESI:X8} " +
                           $"EDI:{_state.EDI:X8} " +
                           $"EBP:{_state.EBP:X8} " +
                           $"ESP:{_state.ESP:X8}";
        
        string segmentRegisters = _config.ShowAllSegmentRegisters
            ? $"DS:{_state.DS:X4} " +
              $"ES:{_state.ES:X4} " +
              $"SS:{_state.SS:X4} " +
              $"CS:{_state.CS:X4} " +
              $"FS:{_state.FS:X4} " +
              $"GS:{_state.GS:X4}"
            : $"DS:{_state.DS:X4} " +
              $"ES:{_state.ES:X4} " +
              $"SS:{_state.SS:X4}";
        
        return  $"{registers} {segmentRegisters} {GenerateFlags()}";
    }

    /// <summary>
    /// Generates the flags part.
    /// </summary>
    /// <returns>Formatted flags string like "C0 Z0 S0 O0 I1"</returns>
    private string GenerateFlags() {
        // Dosbox format shows: C Z S O I (when ShowAllFlags is false)
        // Full format shows: C Z S O I D T A P (when ShowAllFlags is true)
        string flags = $"C{(_state.CarryFlag ? '1' : '0')} " +
                       $"Z{(_state.ZeroFlag ? '1' : '0')} " +
                       $"S{(_state.SignFlag ? '1' : '0')} " +
                       $"O{(_state.OverflowFlag ? '1' : '0')} " +
                       $"I{(_state.InterruptFlag ? '1' : '0')}";
        
        if (_config.ShowAllFlags) {
            flags += $" D{(_state.DirectionFlag ? '1' : '0')} " +
                     $"T{(_state.TrapFlag ? '1' : '0')} " +
                     $"A{(_state.AuxiliaryFlag ? '1' : '0')} " +
                     $"P{(_state.ParityFlag ? '1' : '0')}";
        }
        
        return flags;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (!_disposed) {
            _writer.Flush();
            _writer.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
