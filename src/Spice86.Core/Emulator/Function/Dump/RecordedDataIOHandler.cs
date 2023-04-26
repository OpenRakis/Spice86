namespace Spice86.Core.Emulator.Function.Dump;

using CPU;

using Memory;

using VM;

public abstract class RecordedDataIoHandler {
    public string DumpDirectory { get; }

    public RecordedDataIoHandler(string dumpDirectory) {
        DumpDirectory = dumpDirectory;
    }

    protected string ExecutionFlowFile => GenerateDumpFileName("ExecutionFlow.json");

    protected string SymbolsFile => GenerateDumpFileName("GhidraSymbols.txt");

    protected string GenerateDumpFileName(string suffix) {
        return $"{DumpDirectory}/spice86dump{suffix}";
    }
}