namespace Spice86.Core.Emulator.CPU.CfgCpu.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.StateSerialization;

using System.IO;

/// <summary>
/// Logs every executed CPU instruction to a file for debugging purposes.
/// Format: SegmentedAddress InstructionString
/// </summary>
public sealed class CpuHeavyLogger : IDisposable {
    private readonly StreamWriter _writer;
    private readonly NodeToString _nodeToString;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuHeavyLogger"/> class.
    /// </summary>
    /// <param name="emulatorStateSerializationFolder">Where to write the file.</param>
    /// <param name="customFilePath">Optional custom file path. If null, uses {DumpDirectory}/cpu_heavy.log</param>
    /// <param name="nodeToString">The node renderer to use.</param>
    public CpuHeavyLogger(EmulatorStateSerializationFolder emulatorStateSerializationFolder, string? customFilePath, NodeToString nodeToString) {
        _nodeToString = nodeToString;
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
    /// Format: SegmentedAddress InstructionString
    /// </summary>
    /// <param name="node">The CFG node representing the executed instruction.</param>
    public void LogInstruction(ICfgNode node) {
        if (_disposed) {
            return;
        }

        _writer.WriteLine(_nodeToString.ToAssemblyStringWithAddress(node));
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
