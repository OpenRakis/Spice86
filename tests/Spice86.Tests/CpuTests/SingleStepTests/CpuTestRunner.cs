namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Utils;

/// <summary>
/// Executes individual CPU tests and validates results.
/// </summary>
public class CpuTestRunner {
    private readonly CpuTestAsserter _testAsserter;
    private readonly NodeToString _nodeToString = new(AsmRenderingConfig.CreateSpice86Style());

    public CpuTestRunner(CpuTestAsserter testAsserter) {
        _testAsserter = testAsserter;
    }

    /// <summary>
    /// Runs a single CPU test and validates the result.
    /// </summary>
    /// <param name="cpuTest">The test to run</param>
    /// <param name="index">The test index within its file</param>
    /// <param name="fileName">The test file name</param>
    /// <param name="machine">The minimal machine to run the test on</param>
    /// <param name="maxCycles">Maximum number of instruction cycles to execute</param>
    /// <param name="flagsMask">Bitmask applied when comparing EFLAGS. Bits set
    /// to 1 are compared, bits set to 0 are ignored (used to skip flag bits that
    /// are documented as undefined for the executed opcode).</param>
    /// <exception cref="Exception">Thrown when the test fails with detailed error information</exception>
    public void RunTest(CpuTest cpuTest, int index, string fileName, SingleStepTestMinimalMachine machine, int maxCycles, uint flagsMask) {
        List<ICfgNode> nodesEncountered = new();
        try {
            InitializeMemory(cpuTest.Initial.Ram, machine.Memory);
            InitializeRegisters(cpuTest.Initial.Registers, machine.State);

            CfgCpu cfgCpu = machine.Cpu;
            cfgCpu.SignalEntry();
            for (int i = 0; i < maxCycles; i++) {
                nodesEncountered.Add(cfgCpu.ToExecute());
                cfgCpu.ExecuteNext();
                if (!machine.State.IsRunning) {
                    break;
                }
            }

            _testAsserter.AssertRegistersMatch(cpuTest.Final.Registers, machine.State, flagsMask);
            _testAsserter.AssertMemoryMatches(cpuTest.Final.Ram, machine.Memory, cpuTest.Exception, flagsMask);
        } catch (Exception e) {
            throw new Exception(GenerateErrorMessage(cpuTest, index, fileName, machine, e.Message, nodesEncountered), e);
        } finally {
            machine.RestoreMemoryAfterTest();
            machine.Cpu.Clear();
        }
    }

    private string GenerateErrorMessage(CpuTest cpuTest, int index, string fileName, SingleStepTestMinimalMachine machine,
        string message, List<ICfgNode> nodesEncountered) {
        // Create State objects only when there's an error for debugging
        State initialStateSnapshot = new State(machine.State.Flags.CpuModel);
        InitializeRegisters(cpuTest.Initial.Registers, initialStateSnapshot);
        string initialState = initialStateSnapshot.ToString();
        string finalState = machine.State.ToString();
        string instructionBytes = ConvertUtils.ByteArrayToHexString(cpuTest.Bytes);
        string nodeInfo = string.Join('\n', nodesEncountered.Select(node => _nodeToString.ToAssemblyStringWithAddress(node)));

        return @$"
Test File: {fileName}
Test Name: {cpuTest.Name} ({cpuTest.Hash})
Test index: {index}
Instruction Bytes: {instructionBytes}
Initial State:
{initialState}
Final State:
{finalState}
CFG Nodes Encountered:
{nodeInfo}

Error:
{message}
";
    }

    private static void InitializeRegisters(CpuRegisters registers, State state) {
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
        state.Cycles = 0;
        state.IsRunning = true;
    }

    private static void InitializeMemory(RamEntry[] ram, Memory memory) {
        foreach (RamEntry entry in ram) {
            memory.UInt8[entry.Address] = entry.Value;
        }
    }
}
