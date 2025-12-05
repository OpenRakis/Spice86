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

    void TestCommandLineParameter(string? spiceArguments, byte[] expected) {

        string test = DosCommandTail.PrepareCommandlineString(spiceArguments);
        _psp.DosCommandTail.Command = test;
        if (_psp.DosCommandTail.Command != test) {
            throw new UnrecoverableException("Command result different");
        }
        if (_psp.DosCommandTail.Length != test.Length) {
            throw new UnrecoverableException("unexpected length");
        }
        for (int i = 0; i < expected.Length; ++i) {
            byte v = _psp.DosCommandTail.UInt8[i];
            if (v != expected[i]) {
                throw new UnrecoverableException("v != expected");
            }
        }
        for (int i = _psp.DosCommandTail.Length + 2; i < DosCommandTail.MaxSize; ++i) {
            byte v = _psp.DosCommandTail.UInt8[i];
            if (v != 0) {
                throw new UnrecoverableException("not 0");
            }
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
    public void DoSomeTests() {
        // Assert
        TestCommandLineParameter("", new byte[] { 0x00, 0x0D });
        // same as empty
        TestCommandLineParameter("  ", new byte[] { 0x00, 0x0D });
        TestCommandLineParameter("4", new byte[] { 0x02, 0x20, 0x34, 0x0D });
        // the same as "4"
        TestCommandLineParameter(" 4", new byte[] { 0x02, 0x20, 0x34, 0x0D });
        // the same as "4" - trailing whitespaces getting stripped
        TestCommandLineParameter("  4  ", new byte[] { 0x03, 0x20, 0x20, 0x34, 0x0D });
        // Windows: Spice86.exe -e test.exe -a "   ""ab""  cd"
        // same as (but DOS does not removes the outer apostrophs and no quoting needed)
        // DOS: show80h.exe   "ab"  cd
        TestCommandLineParameter("   \"ab\"  cd", new byte[] { 0x0B, 0x20, 0x20, 0x20, 0x22, 0x61, 0x62, 0x22, 0x20, 0x20, 0x63, 0x64, 0x0D });
    }
}