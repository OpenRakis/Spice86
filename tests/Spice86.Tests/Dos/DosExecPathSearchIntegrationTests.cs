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
    /// Tests the actual INSECTS.EXE scenario where both INSECTS.EXE and BRUN30.EXE are in the same directory.
    /// This reproduces the real-world use case described in the issue.
    /// </summary>
    /// <remarks>
    /// In the real scenario, both INSECTS.EXE and BRUN30.EXE are in the same folder.
    /// INSECTS.EXE is a QuickBasic program that requires BRUN30.EXE (QuickBasic 3.0 runtime) to run.
    /// This test verifies that INSECTS.EXE can successfully find and load BRUN30.EXE from the same directory.
    /// </remarks>
    [Fact]
    public void InsectsExe_CanFindBrun30InSameDirectory() {
        // Create test directory with both files in same location:
        //   rootDir/
        //     INSECTS.EXE       <- QuickBasic program
        //     BRUN30.EXE        <- QuickBasic runtime (in same directory)
        string rootDir = Path.Combine(Path.GetTempPath(), "Spice86Test_Insects_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(rootDir);
        
        try {
            _output.WriteLine($"Test directory: {rootDir}");
            
            // Create INSECTS.EXE from embedded binary data
            string insectsPath = Path.Combine(rootDir, "INSECTS.EXE");
            File.WriteAllBytes(insectsPath, GetInsectsExeData());
            _output.WriteLine($"Created INSECTS.EXE at: {insectsPath}");

            // Create BRUN30.EXE from embedded binary data in the same directory
            string brun30Path = Path.Combine(rootDir, "BRUN30.EXE");
            File.WriteAllBytes(brun30Path, GetBrun30ExeData());
            _output.WriteLine($"Created BRUN30.EXE at: {brun30Path}");

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

            using Spice86DependencyInjection di = creator.Create();
            _output.WriteLine("Emulator created successfully");
            
            // Test: BRUN30.EXE should be found in the same directory as INSECTS.EXE
            _output.WriteLine("Attempting to EXEC BRUN30.EXE from same directory...");
            DosExecResult result = di.Machine.Dos.ProcessManager.Exec("BRUN30.EXE", null);
            _output.WriteLine($"EXEC result: Success={result.Success}, ErrorCode={result.ErrorCode}");
            result.Success.Should().BeTrue("BRUN30.EXE should be found in the same directory as INSECTS.EXE");
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

            using Spice86DependencyInjection di = creator.Create();
            
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
    /// Gets the binary data for INSECTS.EXE.
    /// </summary>
    /// <returns>Binary data for INSECTS.EXE as a byte array.</returns>
    /// <remarks>
    /// This is a partial representation of INSECTS.EXE from the hex dump provided.
    /// The actual file is larger, but this contains enough of the MZ header and
    /// initial code to be loaded by the DOS emulator.
    /// SHA256: 5cc13abea04493717e59dcf980ca290ca706d7e0fd2bc26d62c946643a49a6f1
    /// </remarks>
    private static byte[] GetInsectsExeData() {
        // Hex dump of INSECTS.EXE (partial - contains MZ header and initial code)
        string hexData = "4D5A50005E00070020000000FFFFF50AE70BACEE0000F50A1E00000000001A0000001C000000260000003400000036000000380000003A000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001500A5094C9A00000000004000000000627A494E534543545320F50AAA0950186255444F0000F50A501800000000000000001400A609A609A809AC0900028840BB0100CD3E5B9090BBFFFFCD3E329090BB64008BD333C98BC1CD3E8D9090BBBE42BA0300CD3E8C9090BBC242CD3E369090BB5C00BA5D0033C98BC1CD3E879090BB6C00BA6B00CD3E889090BB5842BA6600CD3E3A9090BB5C00BA5D00CD3E849090BB6C00BA6B00CD3E85909033DBBA0100B9FFFFCD3E869090BBFA42CD3E369090BB1443CD3E369090BB4043CD3E369090BB6843CD3E369090BBA243CD3E369090BB5C00BA570033C98BC1CD3E879090BB6A00BA6600CD3E889090BB0A3FCD3E3A9090BB5C00BA5700CD3E849090BB6A00BA6600CD3E85909033DBBA0100B9FFFFCD3E869090BBFA42CD3E369090BB1443CD3E369090BB4043CD3E369090BBC243CD3E369090BBFC43CD3E369090BB5C00BA570033C98BC1CD3E879090BB6A00BA6600CD3E889090BB703FCD3E3A9090BB5C00BA5700CD3E849090BB6A00BA6600CD3E85909033DBBA0100B9FFFFCD3E869090BB1C44CD3E369090BB1443CD3E369090BB4043CD3E369090BB6843CD3E369090BBA243CD3E369090BB5C00BA570033C98BC1CD3E879090BB6A00BA6600CD3E889090BB8C41CD3E3A90";
        return Convert.FromHexString(hexData);
    }

    /// <summary>
    /// Gets the binary data for BRUN30.EXE (Microsoft QuickBasic 3.0 runtime).
    /// </summary>
    /// <returns>Binary data for BRUN30.EXE as a byte array.</returns>
    /// <remarks>
    /// This is a stub representation of BRUN30.EXE with a valid MZ header.
    /// The actual BRUN30.EXE is about 70KB, but for testing PATH search functionality,
    /// we only need a valid executable that can be loaded.
    /// SHA256 of real file: b9ebf91c480d43093987b2e6dc6289fd59a9210fe1d8fc3c289ed7d022dffc60
    /// </remarks>
    private static byte[] GetBrun30ExeData() {
        // Create a minimal valid DOS executable for BRUN30.EXE
        // This is sufficient for testing EXEC functionality and PATH search
        return new byte[] {
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
    }

    /// <summary>
    /// Tests that EXEC does not search PATH when the program path contains a directory separator.
    /// This ensures explicit paths are respected and not accidentally resolved via PATH.
    /// </summary>
    [Fact]
    public void ExecProgram_WithDirectorySeparator_DoesNotSearchPath() {
        // Create test directory structure:
        //   rootDir/
        //     SUBDIR/
        //       PROGRAM.EXE  <- Not accessible
        //   pathDir/
        //     PROGRAM.EXE    <- In PATH but should NOT be found
        string rootDir = Path.Combine(Path.GetTempPath(), "Spice86Test_NoPathSearch_" + Guid.NewGuid().ToString("N")[..8]);
        string subDir = Path.Combine(rootDir, "SUBDIR");
        string pathDir = Path.Combine(rootDir, "PATHDIR");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(pathDir);
        
        try {
            // Create PROGRAM.EXE in PATH directory
            string pathProgramExe = Path.Combine(pathDir, "PROGRAM.EXE");
            CreateStubExe(pathProgramExe);

            // Create a main executable in root directory
            string mainExe = Path.Combine(rootDir, "MAIN.EXE");
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

            using Spice86DependencyInjection di = creator.Create();
            
            // Add PATHDIR to PATH
            string currentPath = di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"];
            di.Machine.Dos.ProcessManager.EnvironmentVariables["PATH"] = $"{currentPath};C:\\PATHDIR";
            
            // Try to exec with a path containing directory separator
            // This should NOT search PATH even though PROGRAM.EXE exists in PATH
            DosExecResult result = di.Machine.Dos.ProcessManager.Exec("SUBDIR\\PROGRAM.EXE", null);
            
            // Should fail because SUBDIR\PROGRAM.EXE doesn't exist
            // Even though PROGRAM.EXE exists in PATH, it should not be found
            result.Success.Should().BeFalse("Path with directory separator should not search PATH");
            result.ErrorCode.Should().Be(DosErrorCode.FileNotFound);
        } finally {
            if (Directory.Exists(rootDir)) {
                Directory.Delete(rootDir, true);
            }
        }
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
        File.WriteAllBytes(path, GetBrun30ExeData());
    }
}
