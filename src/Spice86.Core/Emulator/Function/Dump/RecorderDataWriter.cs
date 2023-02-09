using Spice86.Core.DI;
using Spice86.Logging;

namespace Spice86.Core.Emulator.Function.Dump;

using CPU;

using Serilog;

using Spice86.Core.Emulator.VM;

using System.Text.Json;

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
        DumpFunctionsCsv();
        DumpFunctions();
        DumpCSharpStubs();
    }

    private void DumpFunctions() {
        new FunctionInformationDumper().DumpFunctionHandlers(GenerateDumpFileName("FunctionsDetails.txt"),
            new DetailedFunctionInformationToStringConverter(), _cpu.StaticAddressesRecorder, _cpu.FunctionHandler);
    }

    private void DumpFunctionsCsv() {
        new FunctionInformationDumper().DumpFunctionHandlers(GenerateDumpFileName("Functions.csv"),
            new CsvFunctionInformationToStringConverter(), _cpu.StaticAddressesRecorder, _cpu.FunctionHandler);
    }

    private void DumpGhidraSymbols() {
        new GhidraSymbolsDumper().Dump(_machine, GetSymbolsFile());
    }

    private void DumpCSharpStubs() {
        Cpu cpu = _machine.Cpu;
        new FunctionInformationDumper().DumpFunctionHandlers(GenerateDumpFileName("CSharpStub.cs"),
            new CSharpStubToStringConverter(), cpu.StaticAddressesRecorder, cpu.FunctionHandler);
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
        return _machine.Memory.Ram;
    }

    private void DumpExecutionFlow() {
        new ExecutionFlowDumper(new ServiceProvider().GetService<ILoggerService>()).Dump(_machine.Cpu.ExecutionFlowRecorder, GetExecutionFlowFile());
    }
}