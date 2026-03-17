namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

using System;
using System.IO;

using Xunit;

public class DosPathResolverIntegrationTests {
    [Fact]
    public void ResolveProgramWithoutExtension_UsesDosExecutionPriority_BatThenComThenExe() {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dos_path_resolver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try {
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            DosDriveManager dosDriveManager = new DosDriveManager(loggerService, tempDir, null);
            DosPathResolver dosPathResolver = new DosPathResolver(dosDriveManager);

            string batPath = Path.Combine(tempDir, "TOOL.BAT");
            string comPath = Path.Combine(tempDir, "TOOL.COM");
            string exePath = Path.Combine(tempDir, "TOOL.EXE");

            File.WriteAllText(batPath, "@ECHO OFF\r\n");
            File.WriteAllBytes(comPath, [0xC3]);
            File.WriteAllBytes(exePath, [0x4D, 0x5A]);

            string? resolvedWithAllExtensions = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
            resolvedWithAllExtensions.Should().NotBeNull();
            Path.GetFileName(resolvedWithAllExtensions!).Should().Be("TOOL.BAT");

            File.Delete(batPath);
            string? resolvedWithoutBat = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
            resolvedWithoutBat.Should().NotBeNull();
            Path.GetFileName(resolvedWithoutBat!).Should().Be("TOOL.COM");

            File.Delete(comPath);
            string? resolvedOnlyExe = dosPathResolver.GetFullHostExecutablePathFromDosOrDefault("TOOL");
            resolvedOnlyExe.Should().NotBeNull();
            Path.GetFileName(resolvedOnlyExe!).Should().Be("TOOL.EXE");
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
