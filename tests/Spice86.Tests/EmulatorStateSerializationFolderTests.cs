namespace Spice86.Tests;

using FluentAssertions;

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
        TempFile tempFile = new("test program");
        string expectedHash = Convert.ToHexString(SHA256.HashData(tempFile.Data));

        // Act
        string folder = _factory.ComputeFolder(tempFile.Path, null).Folder;

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
        TempFile tempFile = new("test program");
        string expectedHash = Convert.ToHexString(SHA256.HashData(tempFile.Data));

        // Act
        string folder = _factory.ComputeFolder(tempFile.Path, explicitDir).Folder;

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
            TempFile tempFile = new("test program");
            string expectedHash = Convert.ToHexString(SHA256.HashData(tempFile.Data));
            Directory.CreateDirectory(envDir);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", envDir);

            // Act
            string folder = _factory.ComputeFolder(tempFile.Path, null).Folder;

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
            TempFile tempFile = new("test program");
            string expectedHash = Convert.ToHexString(SHA256.HashData(tempFile.Data));
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", null);

            // Act
            string folder = _factory.ComputeFolder(tempFile.Path, null).Folder;

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
            TempFile tempFile = new("test program");
            string expectedHash = Convert.ToHexString(SHA256.HashData(tempFile.Data));
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", nonExistentDir);

            // Act
            string folder = _factory.ComputeFolder(tempFile.Path, null).Folder;

            // Assert
            string expectedPath = Path.Join(Path.GetFullPath("."), expectedHash);
            folder.Should().Be(expectedPath);
        } finally {
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }
}




