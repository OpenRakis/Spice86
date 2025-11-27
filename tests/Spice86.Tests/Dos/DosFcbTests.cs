namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for DOS File Control Block (FCB) operations.
/// </summary>
public class DosFcbTests {
    private readonly ILoggerService _loggerService;
    private readonly IMemory _memory;

    public DosFcbTests() {
        _loggerService = Substitute.For<ILoggerService>();
        
        // Create backing memory
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Core.Emulator.VM.PauseHandler pauseHandler = new(_loggerService);
        State cpuState = new(CpuModel.INTEL_80286);
        EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, cpuState);
        A20Gate a20Gate = new(enabled: false);
        _memory = new Memory(emulatorBreakpointsManager.MemoryReadWriteBreakpoints, ram, a20Gate,
            initializeResetVector: true);
    }

    /// <summary>
    /// Tests that the DosFileControlBlock structure correctly reads and writes drive numbers.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_DriveNumber_ReadsAndWritesCorrectly() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);

        // Act
        fcb.DriveNumber = 3; // C: drive

        // Assert
        fcb.DriveNumber.Should().Be(3);
    }

    /// <summary>
    /// Tests that the DosFileControlBlock structure correctly handles file names with space padding.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_FileName_IsSpacePadded() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);

        // Act
        fcb.FileName = "TEST";

        // Assert
        fcb.FileName.Should().Be("TEST    "); // Padded to 8 characters
    }

    /// <summary>
    /// Tests that the DosFileControlBlock structure correctly handles file extensions.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_FileExtension_IsSpacePadded() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);

        // Act
        fcb.FileExtension = "TXT";

        // Assert
        fcb.FileExtension.Should().Be("TXT");
    }

    /// <summary>
    /// Tests that the DosFileControlBlock correctly calculates the full file name.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_FullFileName_CombinesNameAndExtension() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);

        // Act
        fcb.FileName = "TEST";
        fcb.FileExtension = "TXT";

        // Assert
        fcb.FullFileName.Should().Be("TEST.TXT");
    }

    /// <summary>
    /// Tests that the DosFileControlBlock correctly handles file size.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_FileSize_ReadsAndWritesCorrectly() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);

        // Act
        fcb.FileSize = 12345;

        // Assert
        fcb.FileSize.Should().Be(12345);
    }

    /// <summary>
    /// Tests that the DosFileControlBlock correctly handles record operations.
    /// </summary>
    [Fact]
    public void DosFileControlBlock_NextRecord_AdvancesCorrectly() {
        // Arrange
        DosFileControlBlock fcb = new(_memory, 0x1000);
        fcb.CurrentBlock = 0;
        fcb.CurrentRecord = 127;

        // Act
        fcb.NextRecord();

        // Assert
        fcb.CurrentRecord.Should().Be(0);
        fcb.CurrentBlock.Should().Be(1);
    }

    /// <summary>
    /// Tests that the DosExtendedFileControlBlock correctly identifies extended FCBs.
    /// </summary>
    [Fact]
    public void DosExtendedFileControlBlock_IsExtendedFcb_ReturnsTrueWhenFlagIsSet() {
        // Arrange
        DosExtendedFileControlBlock xfcb = new(_memory, 0x1000);

        // Act
        xfcb.Flag = DosExtendedFileControlBlock.ExtendedFcbFlag;

        // Assert
        xfcb.IsExtendedFcb.Should().BeTrue();
    }

    /// <summary>
    /// Tests that the DosFcbManager correctly parses a simple filename.
    /// </summary>
    [Fact]
    public void DosFcbManager_ParseFilename_ParsesSimpleFilename() {
        // Arrange
        string cDrivePath = Path.GetTempPath();
        string executablePath = Path.Combine(cDrivePath, "test.exe");
        DosDriveManager driveManager = new(_loggerService, cDrivePath, executablePath);
        DosStringDecoder stringDecoder = new(_memory, null!);
        DosFileManager dosFileManager = new(_memory, stringDecoder, driveManager, _loggerService, 
            new List<IVirtualDevice>());
        
        DosFcbManager fcbManager = new(_memory, dosFileManager, driveManager, _loggerService);
        
        // Set up the filename string at address 0x1000
        string filename = "TEST.TXT\0";
        for (int i = 0; i < filename.Length; i++) {
            _memory.UInt8[0x1000 + (uint)i] = (byte)filename[i];
        }
        
        // Set up the FCB at address 0x2000
        for (int i = 0; i < DosFileControlBlock.StructureSize; i++) {
            _memory.UInt8[0x2000 + (uint)i] = (byte)' ';
        }
        _memory.UInt8[0x2000] = 0; // Drive = default

        // Act
        byte result = fcbManager.ParseFilename(0x1000, 0x2000, 0);

        // Assert
        result.Should().Be(DosFcbManager.FcbSuccess); // No wildcards
        
        DosFileControlBlock fcb = new(_memory, 0x2000);
        fcb.FileName.TrimEnd().Should().Be("TEST");
        fcb.FileExtension.TrimEnd().Should().Be("TXT");
    }

    /// <summary>
    /// Tests that the DosFcbManager correctly detects wildcards during parsing.
    /// </summary>
    [Fact]
    public void DosFcbManager_ParseFilename_DetectsWildcards() {
        // Arrange
        string cDrivePath = Path.GetTempPath();
        string executablePath = Path.Combine(cDrivePath, "test.exe");
        DosDriveManager driveManager = new(_loggerService, cDrivePath, executablePath);
        DosStringDecoder stringDecoder = new(_memory, null!);
        DosFileManager dosFileManager = new(_memory, stringDecoder, driveManager, _loggerService, 
            new List<IVirtualDevice>());
        
        DosFcbManager fcbManager = new(_memory, dosFileManager, driveManager, _loggerService);
        
        // Set up the filename string "*.TXT" at address 0x1000
        string filename = "*.TXT\0";
        for (int i = 0; i < filename.Length; i++) {
            _memory.UInt8[0x1000 + (uint)i] = (byte)filename[i];
        }
        
        // Set up the FCB at address 0x2000
        for (int i = 0; i < DosFileControlBlock.StructureSize; i++) {
            _memory.UInt8[0x2000 + (uint)i] = (byte)' ';
        }
        _memory.UInt8[0x2000] = 0; // Drive = default

        // Act
        byte result = fcbManager.ParseFilename(0x1000, 0x2000, 0);

        // Assert
        result.Should().Be(0x01); // Wildcards present
        
        DosFileControlBlock fcb = new(_memory, 0x2000);
        // Filename should be all '?' (wildcards expanded from *)
        fcb.FileName.Should().Be("????????");
    }

    /// <summary>
    /// Tests that the DosFcbManager correctly handles drive letters.
    /// </summary>
    [Fact]
    public void DosFcbManager_ParseFilename_ParsesDriveLetter() {
        // Arrange
        string cDrivePath = Path.GetTempPath();
        string executablePath = Path.Combine(cDrivePath, "test.exe");
        DosDriveManager driveManager = new(_loggerService, cDrivePath, executablePath);
        DosStringDecoder stringDecoder = new(_memory, null!);
        DosFileManager dosFileManager = new(_memory, stringDecoder, driveManager, _loggerService, 
            new List<IVirtualDevice>());
        
        DosFcbManager fcbManager = new(_memory, dosFileManager, driveManager, _loggerService);
        
        // Set up the filename string "C:TEST.TXT" at address 0x1000
        string filename = "C:TEST.TXT\0";
        for (int i = 0; i < filename.Length; i++) {
            _memory.UInt8[0x1000 + (uint)i] = (byte)filename[i];
        }
        
        // Set up the FCB at address 0x2000
        for (int i = 0; i < DosFileControlBlock.StructureSize; i++) {
            _memory.UInt8[0x2000 + (uint)i] = (byte)' ';
        }
        _memory.UInt8[0x2000] = 0; // Drive = default

        // Act
        byte result = fcbManager.ParseFilename(0x1000, 0x2000, 0);

        // Assert
        result.Should().Be(DosFcbManager.FcbSuccess); // No wildcards
        
        DosFileControlBlock fcb = new(_memory, 0x2000);
        fcb.DriveNumber.Should().Be(3); // C: = 3
        fcb.FileName.TrimEnd().Should().Be("TEST");
        fcb.FileExtension.TrimEnd().Should().Be("TXT");
    }
}
