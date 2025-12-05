namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests for DOS EXEC path searching functionality.
/// Tests verify that DOS properly searches the PATH environment variable when loading executables.
/// This simulates real DOS behavior where programs can load runtime dependencies (like BRUN30.EXE
/// for QuickBasic programs) from directories specified in the PATH variable.
/// </summary>
public class DosExecPathSearchIntegrationTests {
    private readonly ITestOutputHelper _output;

    public DosExecPathSearchIntegrationTests(ITestOutputHelper output) {
        _output = output;
    }
    /// <summary>
    /// Tests the actual INSECTS.EXE scenario where it needs to find BRUN30.EXE via PATH.
    /// This reproduces the real-world use case described in the issue.
    /// </summary>
    [Fact]
    public void InsectsExe_CanFindBrun30InPath() {
        // Create test directory structure matching the real scenario:
        //   rootDir/
        //     INSECTS.EXE       <- QuickBasic program
        //     INSECTS.001       <- Data file
        //     INSECTS.002       <- Data file
        //     RUNTIME/
        //       BRUN30.EXE      <- QuickBasic runtime (to be found via PATH)
        string rootDir = Path.Combine(Path.GetTempPath(), "Spice86Test_Insects_" + Guid.NewGuid().ToString("N")[..8]);
        string runtimeSubdir = Path.Combine(rootDir, "RUNTIME");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(runtimeSubdir);
        
        try {
            _output.WriteLine($"Test directory: {rootDir}");
            
            // Create BRUN30.EXE in RUNTIME subdirectory
            // This is the Microsoft QuickBasic runtime that INSECTS.EXE needs
            string brun30Path = Path.Combine(runtimeSubdir, "BRUN30.EXE");
            CreateBrun30Stub(brun30Path);
            _output.WriteLine($"Created BRUN30.EXE at: {brun30Path}");

            // Create a stub INSECTS.EXE that will try to load BRUN30.EXE
            // In reality, INSECTS.EXE is compiled with QuickBasic and depends on BRUN30.EXE
            string insectsPath = Path.Combine(rootDir, "INSECTS.EXE");
            CreateInsectsStub(insectsPath);
            _output.WriteLine($"Created INSECTS.EXE at: {insectsPath}");

            // Setup emulator - C: drive will be mounted to rootDir
            _output.WriteLine("Setting up emulator...");
            Spice86Creator creator = new(
                binName: insectsPath,
                enableCfgCpu: true,
                enablePit: false,
                recordData: false,
                maxCycles: 10000L,
                installInterruptVectors: true,
                enableA20Gate: true
            );

            Spice86DependencyInjection di = creator.Create();
            _output.WriteLine("Emulator created successfully");
            
            // Add C:\RUNTIME to PATH - this simulates having BRUN30.EXE in a PATH directory
            string currentPath = di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"];
            di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"] = $"{currentPath};C:\\RUNTIME";
            _output.WriteLine($"PATH set to: {di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"]}");
            
            // Try to exec BRUN30.EXE - this is what INSECTS.EXE would do internally
            _output.WriteLine("Attempting to EXEC BRUN30.EXE...");
            DosExecResult result = di.Machine.Dos.ProcessManager.Exec("BRUN30.EXE", null);
            
            // Verify that BRUN30.EXE was found via PATH
            _output.WriteLine($"EXEC result: Success={result.Success}, ErrorCode={result.ErrorCode}");
            result.Success.Should().BeTrue("BRUN30.EXE should be found in C:\\RUNTIME via PATH, just like in FreeDOS");
        } finally {
            if (Directory.Exists(rootDir)) {
                Directory.Delete(rootDir, true);
            }
        }
    }

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
    /// Creates a stub INSECTS.EXE for testing.
    /// This simulates the QuickBasic program that would load BRUN30.EXE.
    /// </summary>
    /// <param name="path">Path where INSECTS.EXE should be created.</param>
    private void CreateInsectsStub(string path) {
        // Create a minimal EXE that represents INSECTS.EXE
        // In reality, INSECTS.EXE would try to load BRUN30.EXE, but for testing
        // we just need a valid executable
        CreateStubExe(path);
    }

    /// <summary>
    /// Creates a stub BRUN30.EXE (Microsoft QuickBasic runtime) for testing.
    /// </summary>
    /// <param name="path">Path where BRUN30.EXE should be created.</param>
    /// <remarks>
    /// BRUN30.EXE is the runtime library for Microsoft QuickBasic 3.0.
    /// Programs compiled with QuickBasic require this runtime to execute.
    /// The actual BRUN30.EXE is about 70KB, but for testing we only need
    /// a valid executable that can be loaded.
    /// </remarks>
    private void CreateBrun30Stub(string path) {
        CreateStubExe(path);
    }

    /// <summary>
    /// Creates a minimal DOS executable with a valid MZ header for testing.
    /// </summary>
    /// <param name="path">The full path where the executable should be created.</param>
    /// <remarks>
    /// This creates a functional but minimal DOS executable that can be loaded by the DOS emulator.
    /// The executable contains:
    /// <list type="bullet">
    /// <item>MZ signature (0x4D 0x5A) - DOS executable marker</item>
    /// <item>Minimal EXE header with required fields (pages, relocations, etc.)</item>
    /// <item>HLT instruction as the only code - causes the program to halt immediately</item>
    /// </list>
    /// This is sufficient for testing EXEC functionality without needing actual program logic.
    /// </remarks>
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
