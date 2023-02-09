
using Spice86.Logging;

namespace Spice86.Core.Emulator.Function.Dump;

using Memory;

using Spice86.Core.DI;
using Spice86.Core.Emulator.Function;

public class RecordedDataReader : RecordedDataIoHandler {
    public RecordedDataReader(string dumpDirectory) : base(dumpDirectory) {
    }

    public ExecutionFlowRecorder ReadExecutionFlowRecorderFromFileOrCreate(bool recordData) {
        ExecutionFlowRecorder executionFlowRecorder =
            new ExecutionFlowDumper(
                new ServiceProvider().GetService<ILoggerService>())
                    .ReadFromFileOrCreate(GetExecutionFlowFile());
        executionFlowRecorder.RecordData = recordData;
        return executionFlowRecorder;
    }

    public IDictionary<SegmentedAddress, FunctionInformation> ReadGhidraSymbolsFromFileOrCreate() {
        return new GhidraSymbolsDumper().ReadFromFileOrCreate(GetSymbolsFile());
    }
}