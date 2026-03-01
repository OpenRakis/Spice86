namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Utils;
using Spice86.Tests.CpuTests.SingleStepTests.TestParsing;
using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Executes individual CPU tests and validates results.
/// </summary>
public class CpuTestRunner {
    private readonly CpuTestInitializer _testInitializer;
    private readonly CpuTestAsserter _testAsserter;

    public CpuTestRunner(CpuTestInitializer stateInitializer, CpuTestAsserter testAsserter) {
        _testInitializer = stateInitializer;
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
    /// <exception cref="Exception">Thrown when the test fails with detailed error information</exception>
    public void RunTest(CpuTest cpuTest, int index, string fileName, SingleStepTestMinimalMachine machine, int maxCycles) {
        try {
            _testInitializer.InitializeMemory(cpuTest.Initial.Ram, machine.Memory);
            _testInitializer.InitializeRegisters(cpuTest.Initial.Registers, machine.State);

            CfgCpu cfgCpu = machine.Cpu;
            cfgCpu.SignalEntry();
            for (int i = 0; i < maxCycles; i++) {
                cfgCpu.ExecuteNext();
            }

            _testAsserter.AssertRegistersMatch(cpuTest.Final.Registers, machine.State);
            _testAsserter.AssertMemoryMatches(cpuTest.Final.Ram, machine.Memory);
        } catch (Exception e) {
            throw new Exception(GenerateErrorMessage(cpuTest, index, fileName, machine, e.Message), e);
        } finally {
            machine.RestoreMemoryAfterTest();
            machine.Cpu.Clear();
        }
    }

    private string GenerateErrorMessage(CpuTest cpuTest, int index, string fileName, SingleStepTestMinimalMachine machine,
        string message) {
        // Create State objects only when there's an error for debugging
        State initialStateSnapshot = new State(machine.State.Flags.CpuModel);
        _testInitializer.InitializeRegisters(cpuTest.Initial.Registers, initialStateSnapshot);
        string initialState = initialStateSnapshot.ToString();
        string finalState = machine.State.ToString();
        string instructionBytes = ConvertUtils.ByteArrayToHexString(cpuTest.Bytes);
        string debugInfo = $"\n\nInitial State:\n{initialState}\n\nFinal State:\n{finalState}";
        return
            $"An error occurred while running test \"{cpuTest.Name}\" ({cpuTest.Hash}) in {fileName} (index {index}) for {machine.State.Flags.CpuModel} (Instruction bytes are {instructionBytes}): {message}{debugInfo}";
    }
}
