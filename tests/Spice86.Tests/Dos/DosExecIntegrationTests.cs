namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using Xunit;

/// <summary>
/// Integration tests for DOS EXEC function (INT 21h, AH=4Bh) that validate
/// environment block format and different EXEC modes.
/// </summary>
public class DosExecIntegrationTests {
    /// <summary>
    /// Tests that DOS EXEC environment block format is correctly validated.
    /// The test program writes characters to video memory (B800:0000) to indicate
    /// which tests pass. Expected output is "SEMJCTLOAV" when all tests pass.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void ExecModesAndOverlays_ShouldReportSuccessViaVideoMemory(bool enableCfgCpu) {
        // Arrange
        string programPath = "Resources/NativeDosTests/exec_modes_overlay.com";
        
        if (!File.Exists(programPath)) {
            throw new FileNotFoundException($"Test program not found: {programPath}");
        }

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: Path.GetFullPath(programPath),
            enableCfgCpu: enableCfgCpu,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        // Act
        spice86DependencyInjection.ProgramExecutor.Run();

        // Assert
        // Read output from video memory at B800:0000
        IMemory memory = spice86DependencyInjection.Machine.Memory;
        const int videoMemorySegment = 0xB800;
        const int videoMemoryOffset = 0x0000;
        const int videoMemoryAddress = (videoMemorySegment << 4) + videoMemoryOffset;
        
        // Read 10 characters (each character is 2 bytes: char + attribute)
        StringBuilder output = new StringBuilder();
        for (int i = 0; i < 10; i++) {
            int charOffset = videoMemoryAddress + (i * 2);
            byte charByte = memory.UInt8[charOffset];
            output.Append((char)charByte);
        }

        // Expected: "SEMJCTLOAV"
        // S = Startup success
        // E = Environment block format correct
        // M = Memory layout validation
        // J = Junction/double null verification  
        // C = Count word verification
        // T = Terminator verification
        // L = Length validation
        // O = Offset calculations
        // A = All validations pass
        // V = Verification complete
        const string expectation = "SEMJCTLOAV";
        output.ToString().Should().MatchRegex(expectation,
            "All DOS EXEC environment block validation tests should pass");
    }

    /// <summary>
    /// Provides test configurations for both regular CPU and CfgCpu modes.
    /// </summary>
    public static TheoryData<bool> GetCfgCpuConfigurations() {
        return new TheoryData<bool> {
            false,  // Regular CPU
            true    // CfgCpu
        };
    }
}
