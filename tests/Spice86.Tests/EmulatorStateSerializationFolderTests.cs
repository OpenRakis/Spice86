namespace Spice86.Tests;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using System.IO;
using System.Security.Cryptography;

using Xunit;

public class EmulatorStateSerializationFolderTests {
    private readonly EmulatorStateSerializationFolderFactory _factory = new(Substitute.For<ILoggerService>());

    [Fact]
    public void Constructor_WithValidExePath_ComputesProgramHash() {
        // Arrange
        using TempFile tempFile = new(nameof(Constructor_WithValidExePath_ComputesProgramHash));
        string programPath = CreateTestProgram(tempFile);
        string expectedHash = ComputeHash(programPath);

        // Act
        string folder = _factory.ComputeFolder(programPath, null).Folder;

        // Assert
        folder.Should().Contain(expectedHash);
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException() {
        // Arrange
        string nonExistentFile = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => _factory.ComputeFolder(nonExistentFile, null);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DumpDirectory_WithExplicitDirectory_ReturnsExplicitDirectoryWithProgramHash() {
        // Arrange
        string explicitDir = Path.Join(Path.GetTempPath(), "explicit-dump-dir");
        using TempFile tempFile = new(nameof(DumpDirectory_WithExplicitDirectory_ReturnsExplicitDirectoryWithProgramHash));
        string programPath = CreateTestProgram(tempFile);
        string expectedHash = ComputeHash(programPath);

        // Act
        string folder = _factory.ComputeFolder(programPath, explicitDir).Folder;

        // Assert
        string expectedPath = Path.Join(explicitDir, expectedHash);
        folder.Should().Be(expectedPath);
    }

    [Fact]
    public void DumpDirectory_WithEnvironmentVariable_ReturnsEnvironmentDirectoryWithProgramHash() {
        // Arrange
        string envDir = Path.Join(Path.GetTempPath(), "env-dump-dir");
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        
        try {
            using TempFile tempFile = new(nameof(DumpDirectory_WithEnvironmentVariable_ReturnsEnvironmentDirectoryWithProgramHash));
            string programPath = CreateTestProgram(tempFile);
            string expectedHash = ComputeHash(programPath);
            Directory.CreateDirectory(envDir);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", envDir);

            // Act
            string folder = _factory.ComputeFolder(programPath, null).Folder;

            // Assert
            string expectedPath = Path.Join(envDir, expectedHash);
            folder.Should().Be(expectedPath);
        } finally {
            if (Directory.Exists(envDir)) {
                Directory.Delete(envDir, true);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    [Fact]
    public void DumpDirectory_WithNeitherExplicitNorEnvironment_ReturnsCurrentDirectoryWithProgramHash() {
        // Arrange
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        
        try {
            using TempFile tempFile = new(nameof(DumpDirectory_WithNeitherExplicitNorEnvironment_ReturnsCurrentDirectoryWithProgramHash));
            string programPath = CreateTestProgram(tempFile);
            string expectedHash = ComputeHash(programPath);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", null);

            // Act
            string folder = _factory.ComputeFolder(programPath, null).Folder;

            // Assert
            string expectedPath = Path.Join(Path.GetFullPath("."), expectedHash);
            folder.Should().Be(expectedPath);
        } finally {
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    [Fact]
    public void DumpDirectory_WithNonExistentEnvironmentDirectory_ReturnsCurrentDirectoryWithProgramHash() {
        // Arrange
        string nonExistentDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        
        try {
            using TempFile tempFile = new(nameof(DumpDirectory_WithNonExistentEnvironmentDirectory_ReturnsCurrentDirectoryWithProgramHash));
            string programPath = CreateTestProgram(tempFile);
            string expectedHash = ComputeHash(programPath);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", nonExistentDir);

            // Act
            string folder = _factory.ComputeFolder(programPath, null).Folder;

            // Assert
            string expectedPath = Path.Join(Path.GetFullPath("."), expectedHash);
            folder.Should().Be(expectedPath);
        } finally {
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    private static string CreateTestProgram(TempFile tempFile) {
        return tempFile.CreateTextFile("program.com", "test program");
    }

    private static string ComputeHash(string path) {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
