namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Function.Dump;

public class CfgCpuFlowDumper : IExecutionDumpFactory {
    private readonly CfgCpu _cfgCpu;

    public CfgCpuFlowDumper(CfgCpu cfgCpu) {
        _cfgCpu = cfgCpu;
    }

    public ExecutionDump Dump() {
        return new ExecutionDump();
    }
}