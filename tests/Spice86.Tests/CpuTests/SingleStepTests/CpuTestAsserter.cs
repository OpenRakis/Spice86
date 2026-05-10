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
    /// <param name="flagsMask">Bitmask applied when comparing EFLAGS. Bits set
    /// to 1 are compared, bits set to 0 are ignored (used to skip flag bits that
    /// are documented as undefined for the executed opcode).</param>
    public void AssertRegistersMatch(CpuRegisters expected, State state, uint flagsMask) {
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
        CompareFlags(expected.EFlags, state.Flags.FlagRegister, flagsMask);
    }

    /// <summary>
    /// Compares expected and actual memory values, failing the test if they differ.
    /// </summary>
    /// <param name="ram">The expected RAM entries</param>
    /// <param name="memory">The actual memory state</param>
    /// <param name="exception">Optional exception metadata describing the
    /// linear address at which a 16-bit FLAGS image was pushed by the faulting
    /// instruction. When set, the bytes at that address and the next one are
    /// compared with <paramref name="flagsMask"/> applied to mask out flag bits
    /// that are documented as undefined for the executed opcode.</param>
    /// <param name="flagsMask">Bitmask used when comparing the pushed FLAGS
    /// image bytes. Only the lower 16 bits are relevant.</param>
    public void AssertMemoryMatches(RamEntry[] ram, Memory memory, CpuTestException? exception, uint flagsMask) {
        byte lowFlagMask = (byte)(flagsMask & 0xFF);
        byte highFlagMask = (byte)((flagsMask >> 8) & 0xFF);
        foreach (RamEntry entry in ram) {
            byte actual = memory.UInt8[entry.Address];
            byte byteMask = 0xFF;
            if (exception is not null) {
                if (entry.Address == exception.FlagAddress) {
                    byteMask = lowFlagMask;
                } else if (entry.Address == exception.FlagAddress + 1) {
                    byteMask = highFlagMask;
                }
            }
            if ((entry.Value & byteMask) == (actual & byteMask)) {
                continue;
            }
            string expectedHex = ConvertUtils.ToHex8(entry.Value);
            string actualHex = ConvertUtils.ToHex8(actual);
            string address = ConvertUtils.ToHex32(entry.Address);
            string maskInfo = byteMask == 0xFF ? "" : $" (compared bits mask: {ConvertUtils.ToHex8(byteMask)})";
            Assert.Fail($"Byte at address {address} differs. Expected {expectedHex} Actual {actualHex}{maskInfo}");
        }
    }

    private void CompareReg(string register, uint expected, uint actual) {
        if (expected == actual) {
            return;
        }

        string expectedStr = ConvertUtils.ToHex32(expected);
        string actualStr = ConvertUtils.ToHex32(actual);
        Assert.Fail($"Expected and actual are not the same for register {register}. Expected: {expectedStr} Actual: {actualStr}");
    }

    private void CompareFlags(uint expected, uint actual, uint mask) {
        uint maskedExpected = expected & mask;
        uint maskedActual = actual & mask;
        if (maskedExpected == maskedActual) {
            return;
        }

        string expectedStr = ConvertUtils.ToBin32(expected);
        string actualStr = ConvertUtils.ToBin32(actual);
        string maskStr = ConvertUtils.ToHex32(mask);
        IList<string?> flagsDiffering = [
            CompareFlag(nameof(Flags.Carry), Flags.Carry, expected, actual, mask),
            CompareFlag(nameof(Flags.Parity), Flags.Parity, expected, actual, mask),
            CompareFlag(nameof(Flags.Auxiliary), Flags.Auxiliary, expected, actual, mask),
            CompareFlag(nameof(Flags.Zero), Flags.Zero, expected, actual, mask),
            CompareFlag(nameof(Flags.Sign), Flags.Sign, expected, actual, mask),
            CompareFlag(nameof(Flags.Trap), Flags.Trap, expected, actual, mask),
            CompareFlag(nameof(Flags.Interrupt), Flags.Interrupt, expected, actual, mask),
            CompareFlag(nameof(Flags.Direction), Flags.Direction, expected, actual, mask),
            CompareFlag(nameof(Flags.Overflow), Flags.Overflow, expected, actual, mask),
        ];
        string additionalInfo = ". " + string.Join(",", flagsDiffering.Where(x => x is not null));
        Assert.Fail($"Expected and actual are not the same for register {nameof(Flags)}. Expected: {expectedStr} Actual: {actualStr} (compared bits mask: {maskStr}){additionalInfo}");
    }

    private string? CompareFlag(string flagname, uint flagMask, uint expected, uint actual, uint comparisonMask) {
        if ((flagMask & comparisonMask) == 0) {
            return null;
        }
        if ((expected & flagMask) == (actual & flagMask)) {
            return null;
        }

        return $"{flagname} differs";
    }
}
