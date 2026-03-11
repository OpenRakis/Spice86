namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;
using Xunit;

/// <summary>
/// Asserts that CPU test results match expected values (registers and memory).
/// </summary>
public class CpuTestAsserter {
    /// <summary>
    /// Compares expected and actual register values, failing the test if they differ.
    /// </summary>
    /// <param name="expected">The expected register values</param>
    /// <param name="state">The actual CPU state</param>
    public void AssertRegistersMatch(CpuRegisters expected, State state) {
        CompareReg(nameof(state.EAX), expected.EAX, state.EAX);
        CompareReg(nameof(state.EBX), expected.EBX, state.EBX);
        CompareReg(nameof(state.ECX), expected.ECX, state.ECX);
        CompareReg(nameof(state.EDX), expected.EDX, state.EDX);
        CompareReg(nameof(state.EBP), expected.EBP, state.EBP);
        CompareReg(nameof(state.ESP), expected.ESP, state.ESP);
        CompareReg(nameof(state.ESI), expected.ESI, state.ESI);
        CompareReg(nameof(state.EDI), expected.EDI, state.EDI);

        CompareReg(nameof(state.CS), expected.CS, state.CS);
        CompareReg(nameof(state.DS), expected.DS, state.DS);
        CompareReg(nameof(state.ES), expected.ES, state.ES);
        CompareReg(nameof(state.SS), expected.SS, state.SS);
        CompareReg(nameof(state.FS), expected.FS, state.FS);
        CompareReg(nameof(state.GS), expected.GS, state.GS);

        CompareReg(nameof(state.IP), expected.EIP, state.IP);
        CompareReg(nameof(state.Flags), expected.EFlags, state.Flags.FlagRegister, isFlags: true);
    }

    /// <summary>
    /// Compares expected and actual memory values, failing the test if they differ.
    /// </summary>
    /// <param name="ram">The expected RAM entries</param>
    /// <param name="memory">The actual memory state</param>
    public void AssertMemoryMatches(RamEntry[] ram, Memory memory) {
        foreach (RamEntry entry in ram) {
            byte actual = memory.UInt8[entry.Address];
            if (entry.Value == actual) {
                continue;
            }
            string expectedHex = ConvertUtils.ToHex8(entry.Value);
            string actualHex = ConvertUtils.ToHex8(actual);
            string address = ConvertUtils.ToHex32(entry.Address);
            Assert.Fail($"Byte at address {address} differs. Expected {expectedHex} Actual {actualHex}");
        }
    }

    private void CompareReg(string register, uint expected, uint actual, bool isFlags = false) {
        if (expected == actual) {
            return;
        }

        string expectedStr;
        string actualStr;
        string additionalInfo = "";
        if (isFlags) {
            expectedStr = ConvertUtils.ToBin32(expected);
            actualStr = ConvertUtils.ToBin32(actual);
            IList<string?> flagsDiffering = [
                CompareFlag(nameof(Flags.Carry), Flags.Carry, expected, actual),
                CompareFlag(nameof(Flags.Parity), Flags.Parity, expected, actual),
                CompareFlag(nameof(Flags.Auxiliary), Flags.Auxiliary, expected, actual),
                CompareFlag(nameof(Flags.Zero), Flags.Zero, expected, actual),
                CompareFlag(nameof(Flags.Sign), Flags.Sign, expected, actual),
                CompareFlag(nameof(Flags.Trap), Flags.Trap, expected, actual),
                CompareFlag(nameof(Flags.Interrupt), Flags.Interrupt, expected, actual),
                CompareFlag(nameof(Flags.Direction), Flags.Direction, expected, actual),
                CompareFlag(nameof(Flags.Overflow), Flags.Overflow, expected, actual),
            ];
            additionalInfo = ". " + string.Join(",", flagsDiffering.Where(x => x is not null));
        } else {
            expectedStr = ConvertUtils.ToHex32(expected);
            actualStr = ConvertUtils.ToHex32(actual);
        }
        Assert.Fail($"Expected and actual are not the same for register {register}. Expected: {expectedStr} Actual: {actualStr}{additionalInfo}");
    }

    private string? CompareFlag(string flagname, uint mask, uint expected, uint actual) {
        if ((expected & mask) == (actual & mask)) {
            return null;
        }

        return $"{flagname} differs";
    }
}
