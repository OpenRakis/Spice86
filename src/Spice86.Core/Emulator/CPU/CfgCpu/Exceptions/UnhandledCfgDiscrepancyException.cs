namespace Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;

public class UnhandledCfgDiscrepancyException : Exception {
    public UnhandledCfgDiscrepancyException(string message) : base(message) {
    }
}