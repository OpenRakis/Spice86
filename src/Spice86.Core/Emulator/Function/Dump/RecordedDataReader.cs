namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Shared.Interfaces;
using Memory;

using Spice86.Core.Emulator.Function;
using Spice86.Shared;

public class RecordedDataReader : RecordedDataIoHandler {
    private readonly ILoggerService _loggerService;
    public RecordedDataReader(string dumpDirectory, ILoggerService loggerService) : base(dumpDirectory) {
        _loggerService = loggerService;
    }

    public ExecutionFlowRecorder ReadExecutionFlowRecorderFromFileOrCreate(bool recordData) {
        ExecutionFlowRecorder executionFlowRecorder =
            new ExecutionFlowDumper(
                _loggerService)
                    .ReadFromFileOrCreate(ExecutionFlowFile);
        executionFlowRecorder.RecordData = recordData;
        return executionFlowRecorder;
    }

    public IDictionary<SegmentedAddress, FunctionInformation> ReadGhidraSymbolsFromFileOrCreate() {
        return new GhidraSymbolsDumper(_loggerService).ReadFromFileOrCreate(SymbolsFile);
    }
}