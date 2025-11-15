namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.Function.Dump;

using System.IO;
using System.Security.Cryptography;

using Xunit;

public class DumpContextTests {
    [Fact]
    public void Constructor_WithValidExePath_ComputesProgramHash() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try {
            byte[] testData = "test program content"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));

            // Act
            DumpFolderMetadata context = new(tempFile, null);

            // Assert
            context.ProgramHash.Should().Be(expectedHash);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Constructor_WithNullExePath_ThrowsArgumentException() {
        // Act & Assert
        Action act = () => new DumpFolderMetadata(null, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException() {
        // Arrange
        string nonExistentFile = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => new DumpFolderMetadata(nonExistentFile, null);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DumpDirectory_WithExplicitDirectory_ReturnsExplicitDirectoryWithProgramHash() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string explicitDir = Path.Combine(Path.GetTempPath(), "explicit-dump-dir");
        try {
            byte[] testData = "test"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));

            // Act
            DumpFolderMetadata context = new(tempFile, explicitDir);

            // Assert
            string expectedPath = Path.Combine(explicitDir, expectedHash);
            context.DumpDirectory.Should().Be(expectedPath);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void DumpDirectory_WithEnvironmentVariable_ReturnsEnvironmentDirectoryWithProgramHash() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string envDir = Path.Combine(Path.GetTempPath(), "env-dump-dir");
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");

        try {
            byte[] testData = "test"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));
            Directory.CreateDirectory(envDir);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", envDir);

            // Act
            DumpFolderMetadata context = new(tempFile, null);

            // Assert
            string expectedPath = Path.Combine(envDir, expectedHash);
            context.DumpDirectory.Should().Be(expectedPath);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
            if (Directory.Exists(envDir)) {
                Directory.Delete(envDir, true);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    [Fact]
    public void DumpDirectory_WithNeitherExplicitNorEnvironment_ReturnsCurrentDirectoryWithProgramHash() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");

        try {
            byte[] testData = "test program"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", null);

            // Act
            DumpFolderMetadata context = new(tempFile, null);

            // Assert
            string expectedPath = Path.Combine(".", expectedHash);
            context.DumpDirectory.Should().Be(expectedPath);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    [Fact]
    public void DumpDirectory_WithNonExistentEnvironmentDirectory_ReturnsCurrentDirectoryWithProgramHash() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");

        try {
            byte[] testData = "test program"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", nonExistentDir);

            // Act
            DumpFolderMetadata context = new(tempFile, null);

            // Assert
            string expectedPath = Path.Combine(".", expectedHash);
            context.DumpDirectory.Should().Be(expectedPath);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }
}