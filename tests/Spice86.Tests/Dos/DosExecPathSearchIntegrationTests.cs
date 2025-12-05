namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

/// <summary>
/// Integration tests for DOS EXEC path searching functionality.
/// Tests verify that DOS properly searches the PATH environment variable when loading executables.
/// This simulates real DOS behavior where programs can load runtime dependencies (like BRUN30.EXE
/// for QuickBasic programs) from directories specified in the PATH variable.
/// </summary>
public class DosExecPathSearchIntegrationTests {
    /// <summary>
    /// Tests that DOS EXEC fails when an executable is not in the current directory
    /// and PATH searching is not yet implemented.
    /// This test demonstrates the bug described in the issue where INSECTS.EXE cannot find BRUN30.EXE.
    /// </summary>
    [Fact]
    public void ExecProgram_NotInCurrentDirectory_FailsWithoutPathSearch() {
        // Create test directories
        string mainDir = Path.Combine(Path.GetTempPath(), "Spice86Test_Main_" + Guid.NewGuid().ToString("N")[..8]);
        string runtimeDir = Path.Combine(Path.GetTempPath(), "Spice86Test_Runtime_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(mainDir);
        Directory.CreateDirectory(runtimeDir);
        
        try {
            // Create a stub runtime executable in runtime directory (simulating BRUN30.EXE)
            string runtimeExe = Path.Combine(runtimeDir, "RUNTIME.EXE");
            CreateStubExe(runtimeExe);

            // Create a main executable in main directory that would try to load the runtime
            string mainExe = Path.Combine(mainDir, "MAIN.EXE");
            CreateStubExe(mainExe);

            // Setup emulator
            Spice86Creator creator = new(
                binName: mainExe,
                enableCfgCpu: true,
                enablePit: false,
                recordData: false,
                maxCycles: 10000L,
                installInterruptVectors: true,
                enableA20Gate: true
            );

            Spice86DependencyInjection di = creator.Create();
            
            // Try to exec the runtime from a different directory
            // This simulates what INSECTS.EXE does when trying to load BRUN30.EXE
            DosExecResult result = di.Machine.Dos.ProcessManager.Exec("RUNTIME.EXE", null);
            
            // Currently this FAILS because PATH search is not implemented
            // The file is not in the current directory, so it cannot be found
            result.Success.Should().BeFalse("PATH search is not yet implemented - this test demonstrates the bug");
            result.ErrorCode.Should().Be(DosErrorCode.FileNotFound);
        } finally {
            if (Directory.Exists(mainDir)) {
                Directory.Delete(mainDir, true);
            }
            if (Directory.Exists(runtimeDir)) {
                Directory.Delete(runtimeDir, true);
            }
        }
    }

    /// <summary>
    /// Creates a minimal DOS executable (stub with MZ header).
    /// </summary>
    private void CreateStubExe(string path) {
        // Minimal EXE with MZ header
        byte[] stubExe = new byte[] {
            0x4D, 0x5A, // MZ signature
            0x90, 0x00, // Bytes in last page
            0x03, 0x00, // Pages in file
            0x00, 0x00, // Relocations
            0x04, 0x00, // Size of header in paragraphs
            0x00, 0x00, // Minimum extra paragraphs
            0xFF, 0xFF, // Maximum extra paragraphs
            0x00, 0x00, // Initial SS
            0xB8, 0x00, // Initial SP
            0x00, 0x00, // Checksum
            0x00, 0x00, // Initial IP
            0x00, 0x00, // Initial CS
            0x40, 0x00, // Relocation table offset
            0x00, 0x00, // Overlay number
            // Code section - just HLT
            0xF4 // HLT
        };
        File.WriteAllBytes(path, stubExe);
    }
}
