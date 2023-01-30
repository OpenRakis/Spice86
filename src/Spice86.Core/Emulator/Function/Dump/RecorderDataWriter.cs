using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Text.Json;

namespace Spice86.Core.Emulator.Function.Dump;

public class RecorderDataWriter : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;

    private readonly Machine _machine;
    private readonly Cpu _cpu;

    public RecorderDataWriter(string dumpDirectory, Machine machine, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
        _machine = machine;
        _cpu = machine.Cpu;
    }

    public void DumpAll() {
        _loggerService.Information("Dumping all data to {DumpDirectory}", DumpDirectory);
        DumpCpuRegisters("");
        DumpMemory("");
        DumpGhidraSymbols();
        DumpExecutionFlow();
    }

    private void DumpGhidraSymbols() {
        new GhidraSymbolsDumper(_loggerService).Dump(_machine, GetSymbolsFile());
    }
    
    public void DumpCpuRegisters(string suffix) {
        string path = GenerateDumpFileName($"CpuRegisters{suffix}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_cpu.State));
    }

    public void DumpMemory(string suffix) {
        string path = GenerateDumpFileName($"MemoryDump{suffix}.bin");
        File.WriteAllBytes(path, GenerateRelevantRam());
    }

    private byte[] GenerateRelevantRam() {
        if (_machine.Configuration.InitializeDOS is true) {
            return _machine.CallbackHandler.NopCallbackInstructionInRamCopy();
        }
        return _machine.MainMemory.Ram;
    }

    private void DumpExecutionFlow() {
        new ExecutionFlowDumper(_loggerService).Dump(_machine.Cpu.ExecutionFlowRecorder, GetExecutionFlowFile());
    }
}