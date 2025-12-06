namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

using Configuration = Spice86.Core.CLI.Configuration;
using EmulatorBreakpointsManager = Spice86.Core.Emulator.VM.Breakpoint.EmulatorBreakpointsManager;
using PauseHandler = Spice86.Core.Emulator.VM.PauseHandler;
using State = Spice86.Core.Emulator.CPU.State;

/// <summary>
/// Verifies that the DOS PSP command line is correctly encoded.
/// </summary>
public class DosProgramSegmentPrefixCmdTests {
    // The instance of the DosProgramSegmentPrefix class that we're testing
    private readonly DosProgramSegmentPrefix _psp;

    private void TestCommandLineParameter(string? spiceArguments, byte[] expected) {
        string preparedCommand = DosCommandTail.PrepareCommandlineString(spiceArguments);
        _psp.DosCommandTail.Command = preparedCommand;
        _psp.DosCommandTail.Command.Should().Be(preparedCommand, "command should round-trip correctly");
        _psp.DosCommandTail.Length.Should().Be((byte)preparedCommand.Length, "length should match command string length");
        expected.Length.Should().Be(_psp.DosCommandTail.Length + 2, "not enough expected bytes");
        for (int i = 0; i < expected.Length; ++i) {
            byte v = _psp.DosCommandTail.UInt8[i];
            v.Should().Be(expected[i], $"byte at index {i} should match expected value");
        }
        for (int i = _psp.DosCommandTail.Length + 2; i < DosCommandTail.MaxSize; ++i) {
            byte v = _psp.DosCommandTail.UInt8[i];
            v.Should().Be(0, $"byte at index {i} should be zero-filled");
        }
    }

    /// <summary>
    /// Creates the DosProgramSegmentPrefixInstance for each test case.
    /// </summary>
    public DosProgramSegmentPrefixCmdTests() {
        Ram ram = new(A20Gate.EndOfHighMemoryArea);

        A20Gate a20Gate = new(false);

        AddressReadWriteBreakpoints memoryReadWriteBreakpoints = new();
        Memory memory = new(memoryReadWriteBreakpoints,
            ram, a20Gate);

        _psp = new(memory, 12345);
    }

    /// <summary>
    /// Test some variants
    /// </summary>
    [Fact]
    public void CommandLineEncoding_VariousInputs_MatchesDosFormat() {
        // Prepare argument-string tests
        DosCommandTail.PrepareCommandlineString(null).Length.Should().Be(0);
        DosCommandTail.PrepareCommandlineString("").Length.Should().Be(0);
        DosCommandTail.PrepareCommandlineString("    ").Length.Should().Be(0);
        DosCommandTail.PrepareCommandlineString("4").Should().Be(" 4");
        DosCommandTail.PrepareCommandlineString(" 4").Should().Be(" 4");
        DosCommandTail.PrepareCommandlineString("  4").Should().Be("  4");
        DosCommandTail.PrepareCommandlineString("  4  ").Should().Be("  4");
        DosCommandTail.PrepareCommandlineString(" " + new string('*', 256)).Length.Should().Be(DosCommandTail.MaxCharacterLength);

        // Command bytes test
        // empty
        TestCommandLineParameter("", new byte[] { 0x00, 0x0D });
        // same as empty
        TestCommandLineParameter("  ", new byte[] { 0x00, 0x0D });
        TestCommandLineParameter("4", new byte[] { 0x02, 0x20, 0x34, 0x0D });
        // the same as "4"
        TestCommandLineParameter(" 4", new byte[] { 0x02, 0x20, 0x34, 0x0D });
        // Input "  4  " becomes "  4"
        TestCommandLineParameter("  4  ", new byte[] { 0x03, 0x20, 0x20, 0x34, 0x0D });
        // Windows: Spice86.exe -e test.exe -a "   ""ab""  cd"
        // same as (but DOS does not removes the outer apostrophes and no quoting needed)
        // DOS: show80h.exe   "ab"  cd
        TestCommandLineParameter("   \"ab\"  cd", new byte[] { 0x0B, 0x20, 0x20, 0x20, 0x22, 0x61, 0x62, 0x22, 0x20, 0x20, 0x63, 0x64, 0x0D });
    }
}