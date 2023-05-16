namespace Spice86.Core.Emulator.Function.Dump;

using System.Text.Json;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// A class that provides functionality for writing various recorded data to files.
/// </summary>
public class RecorderDataWriter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;

    private readonly Machine _machine;
    private readonly Cpu _cpu;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="dumpDirectory">Where to dump the data.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public RecorderDataWriter(string dumpDirectory, Machine machine, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
        _machine = machine;
        _cpu = machine.Cpu;
    }

    /// <summary>
    /// Dumps all recorded data to their respective files.
    /// </summary>
    public void DumpAll() {
        _loggerService.Verbose("Dumping all data to {DumpDirectory}", DumpDirectory);
        DumpCpuRegisters("");
        DumpMemory("");
        DumpGhidraSymbols();
        DumpExecutionFlow();
    }

    /// <summary>
    /// Dumps the Ghidra symbols to the file system.
    /// </summary>
    private void DumpGhidraSymbols() {
        new GhidraSymbolsDumper(_loggerService).Dump(_machine, SymbolsFile);
    }
    
    /// <summary>
    /// Dumps the CPU registers to the file system.
    /// </summary>
    /// <param name="suffix">The suffix to add to the file name.</param>
    public void DumpCpuRegisters(string suffix) {
        string path = GenerateDumpFileName($"CpuRegisters{suffix}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_cpu.State));
    }

    /// <summary>
    /// Dumps the machine's memory to the file system.
    /// </summary>
    /// <param name="suffix">The suffix to add to the file name.</param>
    public void DumpMemory(string suffix) {
        string path = GenerateDumpFileName($"MemoryDump{suffix}.bin");
        File.WriteAllBytes(path, GenerateRelevantRam());
    }

    /// <summary>
    /// Generates a byte array containing relevant RAM data to be written to the memory dump file.
    /// </summary>
    /// <returns>A byte array containing relevant RAM data.</returns>
    private byte[] GenerateRelevantRam() {
        if (_machine.Configuration.InitializeDOS is true) {
            return _machine.CallbackHandler.NopCallbackInstructionInRamCopy();
        }
        return _machine.Memory.Ram;
    }

    /// <summary>
    /// Dumps the execution flow data to the file system.
    /// </summary>
    private void DumpExecutionFlow() {
        new ExecutionFlowDumper(_loggerService).Dump(_machine.Cpu.ExecutionFlowRecorder, ExecutionFlowFile);
    }
}