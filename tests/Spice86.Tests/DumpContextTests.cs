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
            DumpContext context = new(tempFile, null);

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
        Action act = () => new DumpContext(null, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException() {
        // Arrange
        string nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Action act = () => new DumpContext(nonExistentFile, null);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DumpDirectory_WithExplicitDirectory_ReturnsExplicitDirectory() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string explicitDir = Path.Combine(Path.GetTempPath(), "explicit-dump-dir");
        try {
            File.WriteAllBytes(tempFile, "test"u8.ToArray());

            // Act
            DumpContext context = new(tempFile, explicitDir);

            // Assert
            context.DumpDirectory.Should().Be(explicitDir);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void DumpDirectory_WithEnvironmentVariable_ReturnsEnvironmentDirectory() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string envDir = Path.Combine(Path.GetTempPath(), "env-dump-dir");
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        
        try {
            File.WriteAllBytes(tempFile, "test"u8.ToArray());
            Directory.CreateDirectory(envDir);
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", envDir);

            // Act
            DumpContext context = new(tempFile, null);

            // Assert
            context.DumpDirectory.Should().Be(envDir);
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
    public void DumpDirectory_WithNeitherExplicitNorEnvironment_ReturnsProgramHashDirectory() {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string? oldEnvValue = Environment.GetEnvironmentVariable("SPICE86_DUMPS_FOLDER");
        
        try {
            byte[] testData = "test program"u8.ToArray();
            File.WriteAllBytes(tempFile, testData);
            string expectedHash = Convert.ToHexString(SHA256.HashData(testData));
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", null);

            // Act
            DumpContext context = new(tempFile, null);

            // Assert
            context.DumpDirectory.Should().Be(expectedHash);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }

    [Fact]
    public void DumpDirectory_WithNonExistentEnvironmentDirectory_ReturnsProgramHashDirectory() {
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
            DumpContext context = new(tempFile, null);

            // Assert
            context.DumpDirectory.Should().Be(expectedHash);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
            Environment.SetEnvironmentVariable("SPICE86_DUMPS_FOLDER", oldEnvValue);
        }
    }
}
