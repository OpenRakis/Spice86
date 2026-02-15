namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for RTC/CMOS and BIOS/DOS time functions.
/// Tests run inline x86 machine code through the emulation stack.
/// </summary>
public class RtcIntegrationTests_New {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests BIOS INT 1A function 00h - Get System Clock Counter
    /// </summary>
    [Fact]
    public void Int1A_GetSystemClockCounter_ShouldWork() {
        // Test INT 1A, AH=00h - Get system clock counter
        // Just verify the interrupt executes without crashing
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h - Get system clock counter
            0xCD, 0x1A,             // int 1Ah
            // Simply report success if we got here
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        RtcTestHandler testHandler = RunRtcTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "INT 1A function 00h should execute without error");
    }

    /// <summary>
    /// Tests DOS INT 21H function 2Ah - Get System Date  
    /// </summary>
    [Fact]
    public void Int21H_GetSystemDate_ShouldWork() {
        // Test INT 21H, AH=2Ah - Get system date
        // Returns: CX=year, DH=month, DL=day, AL=day of week
        byte[] program = new byte[]
        {
            0xB4, 0x2A,             // mov ah, 2Ah - Get system date
            0xCD, 0x21,             // int 21h
            // Validate year is reasonable (>= 1980)
            0x81, 0xF9, 0xBC, 0x07, // cmp cx, 1980 (0x07BC)
            0x72, 0x0C,             // jb failed (year < 1980)
            // Validate month is 1-12
            0x80, 0xFE, 0x01,       // cmp dh, 1
            0x72, 0x07,             // jb failed (month < 1)
            0x80, 0xFE, 0x0C,       // cmp dh, 12
            0x77, 0x02,             // ja failed (month > 12)
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        RtcTestHandler testHandler = RunRtcTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "INT 21H function 2Ah should return valid system date");
    }

    /// <summary>
    /// Tests DOS INT 21H function 2Ch - Get System Time
    /// </summary>
    [Fact]
    public void Int21H_GetSystemTime_ShouldWork() {
        // Test INT 21H, AH=2Ch - Get system time
        // Returns: CH=hour, CL=minutes, DH=seconds, DL=hundredths
        byte[] program = new byte[]
        {
            0xB4, 0x2C,             // mov ah, 2Ch - Get system time
            0xCD, 0x21,             // int 21h
            // Validate hour is 0-23
            0x80, 0xFD, 0x17,       // cmp ch, 23
            0x77, 0x06,             // ja failed (hour > 23)
            // Validate minutes is 0-59
            0x80, 0xF9, 0x3B,       // cmp cl, 59
            0x77, 0x01,             // ja failed (minutes > 59)
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        RtcTestHandler testHandler = RunRtcTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "INT 21H function 2Ch should return valid system time");
    }

    /// <summary>
    /// Runs RTC test program and returns handler with results
    /// </summary>
    private RtcTestHandler RunRtcTest(byte[] program, [CallerMemberName] string unitTestName = "test") {
        // Write program to temp file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: true,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false
        ).Create();

        RtcTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );

        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures RTC test results from designated I/O ports
    /// </summary>
    private class RtcTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();

        public RtcTestHandler(State state, ILoggerService loggerService,
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



