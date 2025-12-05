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
    /// Tests that DOS EXEC succeeds when an executable is found via PATH environment variable.
    /// This verifies the fix for the issue where INSECTS.EXE cannot find BRUN30.EXE.
    /// The runtime is placed in a directory that is added to PATH, and EXEC should find it.
    /// </summary>
    [Fact]
    public void ExecProgram_NotInCurrentDirectory_SucceedsWithPathSearch() {
        // Create test directory structure
        // The C: drive will be mounted to rootDir (parent of where MAIN.EXE is)
        // Structure:
        //   rootDir/
        //     MAIN.EXE       <- main program, current directory
        //     RUNTIME/
        //       RUNTIME.EXE  <- runtime dependency to be found via PATH
        string rootDir = Path.Combine(Path.GetTempPath(), "Spice86Test_" + Guid.NewGuid().ToString("N")[..8]);
        string runtimeSubdir = Path.Combine(rootDir, "RUNTIME");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(runtimeSubdir);
        
        try {
            // Create a stub runtime executable in RUNTIME subdirectory (simulating BRUN30.EXE)
            string runtimeExe = Path.Combine(runtimeSubdir, "RUNTIME.EXE");
            CreateStubExe(runtimeExe);

            // Create a main executable in root directory
            string mainExe = Path.Combine(rootDir, "MAIN.EXE");
            CreateStubExe(mainExe);

            // Setup emulator - C: drive will be mounted to rootDir (parent of MAIN.EXE)
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
            
            // C: drive is mounted to rootDir
            // RUNTIME subdirectory is accessible as C:\RUNTIME
            // Add C:\RUNTIME to PATH environment variable
            string currentPath = di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"];
            di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"] = $"{currentPath};C:\\RUNTIME";
            
            // Try to exec the runtime from the current directory (C:\)
            // This simulates what INSECTS.EXE does when trying to load BRUN30.EXE
            DosExecResult result = di.Machine.Dos.ProcessManager.Exec("RUNTIME.EXE", null);
            
            // With PATH search implemented, this should now SUCCEED
            result.Success.Should().BeTrue("PATH search should find RUNTIME.EXE in C:\\RUNTIME via PATH");
        } finally {
            if (Directory.Exists(rootDir)) {
                Directory.Delete(rootDir, true);
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
