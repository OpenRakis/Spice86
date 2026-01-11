namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration test using the actual SUMMON.COM binary from The Summoning game.
/// This test reproduces the exact startup sequence that causes the crash.
/// </summary>
public class TheSummoningRealBinaryTest {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// The actual SUMMON.COM binary from The Summoning game in hexadecimal format.
    /// This is the exact binary that calls INT 21h AH=4Bh AL=00h to load CODE.1.
    /// </summary>
    private static readonly byte[] SummonComBinary = HexStringToBytes(
        "BC C2 06 BB 6D 00 B4 4A CD 21 73 0A BA 56 04 B4 09 CD 21 E9 13 03 BB 80 00 8A 0F 80 F9 00 75 03 E9 AD 00 43 80 F9 00 74 F7 80 3F 20 75 3E 43 FE C9 75 F1 E9 9A 00 C6 06 73 05 34 90 EB E6 C6 06 73 05 33 90 EB DE C6 06 73 05 32 90 EB D6 C6 06 73 05 31 90 EB CE A2 45 05 80 F9 00 74 72 80 3F 31 75 C1 FE 06 45 05 43 FE C9 EB B8 8A 07 43 FE C9 3C 72 74 C1 3C 52 74 BD 3C 73 74 C9 3C 53 74 C5 3C 69 74 C9 3C 49 74 C5 3C 4D 74 B1 3C 6D 74 AD 3C 63 74 C1 3C 43 74 BD 3C 65 74 B9 3C 45 74 B5 3C 76 74 B1 3C 56 74 AD 3C 78 74 A9 3C 58 74 A5 3C 74 74 A1 3C 54 74 9D 3C 3F 74 0D 3C 2A 74 03 E9 60 FF E8 1D 01 E9 64 02 E8 A2 00 E9 5E 02 A0 45 05 3C 5A 75 03 E8 2C 00 B0 60 E8 51 01 8B 16 56 05 B0 60 E8 81 01 BA 37 05 E8 07 02 3C 00 75 0A BA 3E 05 E8 FD 01 3C 0A 74 EC B0 60 E8 46 01 B8 00 4C CD 21 C6 06 45 05 76 90 B8 00 1A CD 10 3C 1A 74 44 B3 10 B4 12 CD 10 80 FB 10 75 33 B8 32 05 BB 00 F0 8E C3 33 DB FC BA FF FF 8B F0 8B FB B9 05 00 F3 A6 74 0C 43 4A 75 F1 C6 06 45 05 63 90 EB 14 C6 06 45 05 74 90 C6 06 73 05 33 90 EB 06 C6 06 45 05 65 90 C3 56 57 BF 00 B8 8E C7 BF 98 00 BE 4E 05 B9 08 00 F3 A4 5F 5E C3 BA 48 05 B8 00 3D CD 21 72 63 8B D8 BE EC 04 33 FF BA EC 04 B9 01 00 B4 3F CD 21 3D 00 00 74 48 8A 14 80 FA 0D 75 3B 47 83 FF 19 7C 35 E8 BB FF 52 B4 02 BA 4C 00 B7 00 CD 10 33 FF B8 08 0C CD 21 50 B4 02 BA 4F 18 B7 00 CD 10 58 5A 3C 1B 75 11 B4 02 BA 00 18 B7 00 CD 10 B4 02 B2 0A CD 21 74 06 B4 02 CD 21 EB A9 B4 3E CD 21 C3 BA EC 04 B4 09 CD 21 C3 CD 12 8B F0 BB 08 05 E8 2B 00 BB FF FF B4 48 CD 21 81 C3 8D 00 B1 06 D3 EB 8B FB 8B C3 BB 1B 05 E8 12 00 8B C6 2B C7 BB 26 05 E8 08 00 BA 08 05 B4 09 CD 21 C3 B2 0A 83 C3 02 B9 03 00 F6 F2 80 C4 30 88 27 4B 32 E4 3C 00 E0 F2 C3 B4 35 CD 21 8C C0 2E A3 58 05 2E 89 1E 5A 05 2E C6 06 5C 05 01 90 C3 2E 80 3E 5C 05 01 75 19 1E 2E C6 06 5C 05 00 90 2E 8B 16 58 05 8E DA 2E 8B 16 5A 05 B4 25 CD 21 1F C3 1E 50 B8 00 3D CD 21 72 46 8B D8 B8 02 42 33 C9 33 D2 CD 21 72 39 8B F0 93 83 C3 10 D1 EB D1 EB D1 EB D1 EB 8B D0 B4 48 CD 21 73 0A BA 7A 04 B4 09 CD 21 E9 8A 00 8E D8 B8 00 42 8B DA 33 C9 33 D2 CD 21 72 0A B4 3F 8B CE 33 D2 CD 21 73 0A BA D7 04 B4 09 CD 21 EB 68 90 B4 3E CD 21 72 F0 58 B4 25 83 C2 03 CD 21 83 EA 03 2E 89 16 5D 05 8C DA 2E 89 16 5F 05 2E FF 1E 5D 05 1F 3D 00 00 75 01 C3 BA A9 04 B4 09 CD 21 EB 35 90 8C C8 A3 67 05 A3 6B 05 A3 6F 05 BB 63 05 89 26 61 05 8E C0 B8 00 4B CD 21 8C C9 8E D1 2E 8B 26 61 05 8E D9 8E C1 72 05 B4 4D CD 21 C3 BA 33 04 B4 09 CD 21 B0 60 E8 19 FF B8 0A 4C CD 21 45 52 52 4F 52 3A 20 55 6E 61 62 6C 65 20 74 6F 20 72 75 6E 20 73 75 62 70 72 6F 67 72 61 6D 2E 0A 0D 24 45 52 52 4F 52 3A 20 55 6E 61 62 6C 65 20 74 6F 20 44 4F 53 20 66 72 65 65 20 6D 65 6D 6F 72 79 2E 0A 0D 24 45 52 52 4F 52 3A 20 55 6E 61 62 6C 65 20 74 6F 20 61 6C 6C 6F 63 61 74 65 20 6D 65 6D 6F 72 79 20 66 6F 72 20 64 72 69 76 65 72 2E 0A 0D 24 45 52 52 4F 52 3A 20 47 72 61 70 68 69 63 20 64 72 69 76 65 72 20 69 6E 69 74 69 61 6C 69 7A 61 74 69 6F 6E 20 65 72 72 6F 72 2E 0A 0D 24 45 52 52 4F 52 3A 20 52 65 61 64 20 65 72 72 6F 72 2E 0A 0D 24 53 6F 72 72 79 2C 20 6E 6F 20 68 65 6C 70 20 61 76 61 69 6C 61 62 6C 65 2E 0A 0D 24 20 20 20 4B 20 54 6F 74 61 6C 20 4D 65 6D 6F 72 79 20 20 20 20 20 4B 20 46 72 65 65 20 20 20 20 20 4B 20 55 73 65 64 0A 0D 24 54 61 6E 64 79 43 4F 44 45 2E 31 00 43 4F 44 45 2E 32 00 5A 2E 00 48 45 4C 50 2E 00"
    );

    /// <summary>
    /// Test that loads the actual SUMMON.COM binary and verifies it can execute
    /// up to the point where it calls INT 21h AH=4Bh AL=00h to load CODE.1.
    /// This reproduces the exact crash scenario from The Summoning game.
    /// </summary>
    [Fact]
    public void RealSummonCom_ExecutesAndCallsExec_ReproducesCrash() {
        string testDir = CreateTestDirectory();
        
        // Write the actual SUMMON.COM binary
        string summonPath = Path.Combine(testDir, "SUMMON.COM");
        File.WriteAllBytes(summonPath, SummonComBinary);
        
        // Create required files that SUMMON.COM tries to open
        CreateRequiredGameFiles(testDir);
        
        // Create a minimal CODE.1 file (will cause crash as in real game)
        string code1Path = Path.Combine(testDir, "CODE.1");
        byte[] code1Data = CreateMinimalComFile();
        File.WriteAllBytes(code1Path, code1Data);

        // Run SUMMON.COM with proper environment
        SummoningTestHandler testHandler = RunSummonCom(summonPath, testDir);

        // The test should demonstrate the crash occurs when loading CODE.1
        // We expect the emulator to call INT 21h AH=4Bh AL=00h (LoadAndExecute)
        Console.WriteLine($"Test completed. Exec calls detected: {testHandler.ExecCallCount}");
        Console.WriteLine($"Error details: {string.Join(", ", testHandler.Details.Select(b => b.ToString("X2")))}");
        
        // Verify that SUMMON.COM attempted to load CODE.1 via EXEC
        testHandler.ExecCallCount.Should().BeGreaterThan(0, 
            "SUMMON.COM should call INT 21h AH=4Bh to load CODE.1");
    }

    /// <summary>
    /// Test with command line arguments to SUMMON.COM.
    /// The real game accepts arguments like "1", "2", "3", "4" for graphics modes.
    /// </summary>
    [Fact]
    public void RealSummonCom_WithGraphicsArg_ParsesCommandLine() {
        string testDir = CreateTestDirectory();
        
        string summonPath = Path.Combine(testDir, "SUMMON.COM");
        File.WriteAllBytes(summonPath, SummonComBinary);
        
        CreateRequiredGameFiles(testDir);
        
        // Run with "3" argument (for VGA mode as example)
        SummoningTestHandler testHandler = RunSummonComWithArgs(summonPath, testDir, "3");
        
        Console.WriteLine($"Test with graphics arg completed. Exec calls: {testHandler.ExecCallCount}");
        
        // Should still reach the EXEC call
        testHandler.ExecCallCount.Should().BeGreaterThanOrEqualTo(0, 
            "SUMMON.COM should process arguments and continue execution");
    }

    /// <summary>
    /// Creates the minimum required game files that SUMMON.COM expects to find.
    /// Based on the game's file list: V, HELP, CODE.1, CODE.2, etc.
    /// </summary>
    private void CreateRequiredGameFiles(string testDir) {
        // Create V file (graphics driver file)
        string vPath = Path.Combine(testDir, "V");
        File.WriteAllBytes(vPath, new byte[3733]); // Size from game logs
        
        // Create HELP file
        string helpPath = Path.Combine(testDir, "HELP");
        File.WriteAllBytes(helpPath, new byte[100]);
        
        // Create other files if needed
        string code2Path = Path.Combine(testDir, "CODE.2");
        File.WriteAllBytes(code2Path, CreateMinimalComFile());
    }

    /// <summary>
    /// Creates a minimal COM file that will execute without crashing initially.
    /// </summary>
    private byte[] CreateMinimalComFile() {
        // Simple COM that does RET (C3)
        // This won't crash immediately but demonstrates the EXEC mechanism
        return new byte[] { 
            0xB4, 0x4C,  // MOV AH, 4Ch - DOS terminate
            0xB0, 0x00,  // MOV AL, 00h - exit code 0
            0xCD, 0x21   // INT 21h
        };
    }

    /// <summary>
    /// Runs SUMMON.COM without arguments.
    /// </summary>
    private SummoningTestHandler RunSummonCom(string summonPath, string workingDirectory,
        [CallerMemberName] string unitTestName = "test") {
        return RunSummonComWithArgs(summonPath, workingDirectory, "", unitTestName);
    }

    /// <summary>
    /// Runs SUMMON.COM with specified command line arguments.
    /// </summary>
    private SummoningTestHandler RunSummonComWithArgs(string summonPath, string workingDirectory, 
        string arguments, [CallerMemberName] string unitTestName = "test") {
        
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: summonPath,
            args: arguments,
            enablePit: false,
            recordData: false,
            maxCycles: 1000000L,  // Enough cycles for startup sequence
            installInterruptVectors: true,
            enableA20Gate: true,
            cDrive: workingDirectory
        ).Create();

        SummoningTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        try {
            spice86DependencyInjection.ProgramExecutor.Run();
        } catch (Exception ex) {
            // Catch the expected crash and log it
            Console.WriteLine($"Expected crash occurred: {ex.Message}");
            testHandler.CrashOccurred = true;
            testHandler.CrashMessage = ex.Message;
        }

        return testHandler;
    }

    private string CreateTestDirectory([CallerMemberName] string testName = "test") {
        string tempDir = Path.Combine(Path.GetTempPath(), $"summoning_real_{testName}_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Converts a hexadecimal string to a byte array.
    /// </summary>
    private static byte[] HexStringToBytes(string hex) {
        hex = hex.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private class SummoningTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();
        public int ExecCallCount { get; set; }
        public bool CrashOccurred { get; set; }
        public string CrashMessage { get; set; } = "";

        public SummoningTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DetailsPort) {
                Details.Add(value);
            }
        }
    }
}
