namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Initializes CPU test state (registers and memory) from test data.
/// </summary>
public class CpuTestInitializer {
    /// <summary>
    /// Initializes CPU registers from test data.
    /// </summary>
    /// <param name="registers">The register values from the test</param>
    /// <param name="state">The CPU state to initialize</param>
    public void InitializeRegisters(CpuRegisters registers, State state) {
        state.EAX = registers.EAX;
        state.EBX = registers.EBX;
        state.ECX = registers.ECX;
        state.EDX = registers.EDX;
        state.EBP = registers.EBP;
        state.ESP = registers.ESP;
        state.ESI = registers.ESI;
        state.EDI = registers.EDI;

        state.CS = registers.CS;
        state.DS = registers.DS;
        state.ES = registers.ES;
        state.SS = registers.SS;
        state.FS = registers.FS;
        state.GS = registers.GS;

        state.IP = registers.EIP;
        state.Flags.FlagRegister = registers.EFlags;
    }

    /// <summary>
    /// Initializes memory from test data.
    /// </summary>
    /// <param name="ram">The RAM entries from the test</param>
    /// <param name="memory">The memory to initialize</param>
    public void InitializeMemory(RamEntry[] ram, Memory memory) {
        foreach (RamEntry entry in ram) {
            memory.UInt8[entry.Address] = entry.Value;
        }
    }
}
