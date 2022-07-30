namespace Spice86.Core.Emulator.Function.Dump;

using CPU;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Logging;

public class RecorderDataWriter : RecordedDataIoHandler {
    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<RecorderDataWriter>();

    private readonly Machine _machine;
    private readonly Cpu _cpu;

    public RecorderDataWriter(string dumpDirectory, Machine machine) : base(dumpDirectory) {
        _machine = machine;
        _cpu = machine.Cpu;
    }

    public void DumpAll() {
        _logger.Information("Dumping all data to {DumpDirectory}", DumpDirectory);
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


    public void DumpMemory(string suffix) {
        _machine.Memory.DumpToFile(GenerateDumpFileName($"MemoryDump{suffix}.bin"));
    }

    private void DumpExecutionFlow() {
        new ExecutionFlowDumper().Dump(_machine.Cpu.ExecutionFlowRecorder, GetExecutionFlowFile());
    }
}