namespace Spice86.Tests.Dos.Xms;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for XMS functionality that run machine code through the emulation stack,
/// similar to how real programs like HITEST.ASM interact with the XMS driver.
/// </summary>
public class XmsIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests XMS installation check via INT 2Fh, AH=43h, AL=00h
    /// </summary>
    [Fact]
    public void XmsInstallationCheck_ShouldBeInstalled() {
        // This test checks if the XMS driver is installed by calling INT 2Fh, AH=43h, AL=00h
        // If AL returns 80h, XMS is installed
        byte[] program = new byte[]
        {
            0xB8, 0x00, 0x43,       // mov ax, 4300h - XMS installation check
            0xCD, 0x2F,             // int 2Fh
            0x3C, 0x80,             // cmp al, 80h - is XMS installed?
            0x75, 0x04,             // jne notInstalled
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // notInstalled:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests XMS entry point retrieval via INT 2Fh, AH=43h, AL=10h
    /// </summary>
    [Fact]
    public void GetXmsEntryPoint_ShouldReturnValidAddress() {
        // This test checks if we can get the XMS entry point
        // Result should be non-zero ES:BX
        byte[] program = new byte[]
        {
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            0x26, 0x81, 0xFB, 0x00, 0x00, // cmp es:bx, 0 - check if we got a valid address
            0x74, 0x04,             // je failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Runs the XMS test program and returns a test handler with results
    /// </summary>
    private XmsTestHandler RunXmsTest(byte[] program, bool enableA20Gate,
        [CallerMemberName] string unitTestName = "test") {
        byte[] comFile = new byte[program.Length + 0x100];
        Array.Copy(program, 0, comFile, 0x100, program.Length);


        // Use program bytes directly without any padding
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with .com extension
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: enableA20Gate,
            enableXms: true
        ).Create();

        XmsTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures XMS test results from designated I/O ports
    /// </summary>
    private class XmsTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public XmsTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            }
        }
    }
}