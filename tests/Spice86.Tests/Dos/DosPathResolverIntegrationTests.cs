namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using System;
using System.IO;

using Xunit;

public class DosPathResolverIntegrationTests {
    [Fact]
    public void ResolveProgramWithoutExtension_UsesDosExecutionPriority_BatThenComThenExe() {
        using TempFile tempFile = new("dos_path_resolver");

        ILoggerService loggerService = Substitute.For<ILoggerService>();
        DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(loggerService, tempFile.Path);
        DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);

        string batPath = Path.Join(tempFile.Path, "TOOL.BAT");
        string comPath = Path.Join(tempFile.Path, "TOOL.COM");
        string exePath = Path.Join(tempFile.Path, "TOOL.EXE");

        File.WriteAllText(batPath, "@ECHO OFF\r\n");
        File.WriteAllBytes(comPath, [0xC3]);
        File.WriteAllBytes(exePath, [0x4D, 0x5A]);

        string? resolvedWithAllExtensions = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
        resolvedWithAllExtensions.Should().NotBeNull();
        Path.GetFileName(resolvedWithAllExtensions ?? string.Empty).Should().Be("TOOL.BAT");

        File.Delete(batPath);
        string? resolvedWithoutBat = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
        resolvedWithoutBat.Should().NotBeNull();
        Path.GetFileName(resolvedWithoutBat ?? string.Empty).Should().Be("TOOL.COM");

        File.Delete(comPath);
        string? resolvedOnlyExe = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
        resolvedOnlyExe.Should().NotBeNull();
        Path.GetFileName(resolvedOnlyExe ?? string.Empty).Should().Be("TOOL.EXE");
    }

    [Fact]
    public void GetFullHostExecutablePathFromDosOrDefault_ExactMatch_PreferredOver83Truncation() {
        // Arrange
        using TempFile tempFile = new("dos_path_resolver_exe_83");
        DosPathResolver dosPathResolver = new DosPathResolver(
            DosTestHelpers.CreateDriveManager(tempFile.Path));

        // Both names truncate to BIOS_INT.COM under 8.3 rules.
        // Querying without extension triggers TryResolveExecutableWithoutExtension;
        // the exact match must still win over the 8.3 collision candidate.
        string biosInt1aPath = Path.Join(tempFile.Path, "bios_int1a.com");
        string biosInt70WaitPath = Path.Join(tempFile.Path, "bios_int70_wait.com");
        File.WriteAllBytes(biosInt1aPath, [0xC3]);
        File.WriteAllBytes(biosInt70WaitPath, [0xC3]);

        // Act
        string? resolved1a = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault(@"C:\BIOS_INT1A");
        string? resolved70 = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault(@"C:\BIOS_INT70_WAIT");

        // Assert
        resolved1a.Should().NotBeNull();
        Path.GetFileName(resolved1a ?? string.Empty).Should().Be("bios_int1a.com");

        resolved70.Should().NotBeNull();
        Path.GetFileName(resolved70 ?? string.Empty).Should().Be("bios_int70_wait.com");
    }

    [Fact]
    public void GetFullHostPathFromDosOrDefault_ExactMatch_PreferredOver83Truncation() {
        // Arrange
        using TempFile tempFile = new("dos_path_resolver_83");
        DosPathResolver dosPathResolver = new DosPathResolver(
            DosTestHelpers.CreateDriveManager(tempFile.Path));

        // Both names truncate to BIOS_INT.COM under 8.3 rules.
        // The exact match must win.
        string biosInt1aPath = Path.Join(tempFile.Path, "bios_int1a.com");
        string biosInt70WaitPath = Path.Join(tempFile.Path, "bios_int70_wait.com");
        File.WriteAllBytes(biosInt1aPath, [0xC3]);
        File.WriteAllBytes(biosInt70WaitPath, [0xC3]);

        // Act
        string? resolved1a = dosPathResolver.GetFullHostPathFromDosOrDefault(@"C:\BIOS_INT1A.COM");
        string? resolved70 = dosPathResolver.GetFullHostPathFromDosOrDefault(@"C:\BIOS_INT70_WAIT.COM");

        // Assert
        resolved1a.Should().NotBeNull();
        Path.GetFileName(resolved1a ?? string.Empty).Should().Be("bios_int1a.com");

        resolved70.Should().NotBeNull();
        Path.GetFileName(resolved70 ?? string.Empty).Should().Be("bios_int70_wait.com");
    }

    /// <summary>
    /// Issue #2148: a relative DOS path must resolve under the active drive's current
    /// directory, not under the drive root. <c>CD SUBDIR</c> followed by writing
    /// <c>OUT.TXT</c> must place the file under <c>C:\SUBDIR\OUT.TXT</c>.
    /// </summary>
    [Fact]
    public void ResolveNewFilePath_RelativePath_HonorsDriveCurrentDirectory() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_path_relcwd_{Guid.NewGuid():N}");
        string subDir = Path.Join(tempDir, "SUBDIR");
        Directory.CreateDirectory(subDir);

        try {
            DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempDir);
            DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);
            dosPathResolver.SetCurrentDir("SUBDIR");

            string? resolved = dosPathResolver.ResolveNewFilePath("OUT.TXT");

            resolved.Should().NotBeNull();
            string actual = (resolved ?? string.Empty).Replace('\\', '/');
            actual.Should().EndWith("SUBDIR/OUT.TXT");
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// A drive-root-relative path (one starting with '\') must ignore the drive's current
    /// directory and resolve directly under the drive root.
    /// </summary>
    [Fact]
    public void ResolveNewFilePath_RootedPath_IgnoresCurrentDirectory() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_path_rooted_{Guid.NewGuid():N}");
        string subDir = Path.Join(tempDir, "SUBDIR");
        Directory.CreateDirectory(subDir);

        try {
            DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempDir);
            DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);
            dosPathResolver.SetCurrentDir("SUBDIR");

            string? resolved = dosPathResolver.ResolveNewFilePath("\\OUT.TXT");

            resolved.Should().NotBeNull();
            string actual = (resolved ?? string.Empty).Replace('\\', '/');
            actual.Should().EndWith("/OUT.TXT");
            actual.Should().NotContain("SUBDIR/OUT.TXT");
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// '..' must pop a single segment off the resolved path (proper traversal),
    /// not skip the next path element.
    /// </summary>
    [Fact]
    public void ResolveNewFilePath_DotDot_PopsOneSegment() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_path_dotdot_{Guid.NewGuid():N}");
        string aDir = Path.Join(tempDir, "A");
        Directory.CreateDirectory(aDir);

        try {
            DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempDir);
            DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);
            dosPathResolver.SetCurrentDir("A");

            string? resolved = dosPathResolver.ResolveNewFilePath("..\\OUT.TXT");

            resolved.Should().NotBeNull();
            string actual = (resolved ?? string.Empty).Replace('\\', '/');
            actual.Should().EndWith("/OUT.TXT");
            actual.Should().NotContain("/A/");
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// After CD CHILD then CD .., the drive's CurrentDosDirectory must be empty (root)
    /// and a relative path must resolve to the drive root.
    /// </summary>
    [Fact]
    public void SetCurrentDir_CdChildThenCdDotDot_ReturnsToRoot() {
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_path_cdback_{Guid.NewGuid():N}");
        string childDir = Path.Join(tempDir, "CHILD");
        Directory.CreateDirectory(childDir);

        try {
            DosDriveManager dosDriveManager = DosTestHelpers.CreateDriveManager(tempDir);
            DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);

            dosPathResolver.SetCurrentDir("CHILD");
            dosDriveManager['C'].CurrentDosDirectory.Should().Be("CHILD");

            dosPathResolver.SetCurrentDir("..");
            dosDriveManager['C'].CurrentDosDirectory.Should().Be("");

            string? resolved = dosPathResolver.ResolveNewFilePath("OUT.TXT");
            resolved.Should().NotBeNull();
            string actual = (resolved ?? string.Empty).Replace('\\', '/');
            actual.Should().EndWith("/OUT.TXT");
            actual.Should().NotContain("/CHILD/");
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

