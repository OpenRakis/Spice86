namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Machine-level checks of the changed-bytes map: real programs mark exactly the bytes they wrote
/// after their loader finished, and nothing else.
/// </summary>
public sealed class ChangedMemoryMapMachineTest {
    [Fact]
    public void DataStringsMarksOnlyTheRuntimeBuiltBuffer() {
        // Arrange
        using AsmFixtureRun run =
            AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.Partition("data_strings"), JitMode.InterpretedOnly);
        ChangedMemoryMapAssertions asserts = new(run);
        LoadedImage image = asserts.Images.Should().ContainSingle().Subject;
        byte[] file = File.ReadAllBytes(Path.GetFullPath("Resources/cpuTests/data_strings.com"));
        int messageOffset = IndexOf(file, "HELLO$"u8.ToArray());
        // fasm's trailing `rb 8` emits no file bytes: the runtime buffer sits right after the
        // loaded image, outside every watched area, so it can never read "unchanged".
        int dynamicMessageOffset = IndexOf(file, [0x01, 0x02, 0x03, 0xFF]) + 4;

        // Act
        uint messageAddress = image.StartAddress + (uint)messageOffset;
        uint dynamicMessageAddress = image.StartAddress + (uint)dynamicMessageOffset;
        dynamicMessageAddress.Should().Be(image.StartAddress + image.SizeInBytes,
            "the runtime buffer begins exactly at the end of the loaded image");

        // Assert
        asserts.Map.IsUnchanged(messageAddress, 6).Should().BeTrue(
            "the $-terminated string the loader wrote is never modified at run time");
        asserts.Map.IsUnchanged(dynamicMessageAddress, 3).Should().BeFalse(
            "the buffer the program fills at run time is outside the watched image");
        asserts.Map.IsUnchanged(image.StartAddress, (int)image.SizeInBytes).Should().BeTrue(
            "the bytes the loader wrote are not marked");
    }

    [Fact]
    public void DataJumpTablesMarksThePatchedEntryOnly() {
        // Arrange / Act
        using AsmFixtureRun run =
            AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.Partition("data_jump_tables"), JitMode.InterpretedOnly);
        ChangedMemoryMapAssertions asserts = new(run);

        // Assert
        // The .bin loads as a BIOS binary at 0xF000 (not a DOS-loaded image, so it only shows up
        // as a watched area) while the program's data segment is 0x2000, so the run-time-patched
        // table entry lives outside the watched image and can never read "unchanged"; the whole
        // loaded image keeps its loaded bytes.
        (uint watchedStart, uint watchedEnd) = asserts.Map.WatchedAreas.Should().ContainSingle().Subject;
        watchedStart.Should().Be(0xF0000, "the BIOS binary loads at 0xF000:0000");
        asserts.Map.IsUnchanged(0x20104, 2).Should().BeFalse(
            "the table entry patched at run time is outside the watched image");
        asserts.Map.IsUnchanged(watchedStart, (int)(watchedEnd - watchedStart)).Should().BeTrue(
            "the code and the loader-written table entries are not marked");
    }

    [Fact]
    public void ExecChildrenAndOverlayAreWatchedAndTheirLoaderWritesUnmarked() {
        // Arrange / Act
        AsmScenario scenario = AsmScenarioCatalog.ExecModesAndOverlays();
        using AsmRunScope scope = scenario.Prepare();
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);
        ChangedMemoryMapAssertions asserts = new(run);

        // Assert
        asserts.Map.WatchedAreas.Should().NotBeEmpty();
        foreach (LoadedImage image in asserts.Images) {
            uint end = image.StartAddress + image.SizeInBytes;
            asserts.Map.WatchedAreas.Should().Contain(
                area => area.Start <= image.StartAddress && area.End >= end,
                $"image {image.Name} is watched");
            if (image.Name.EndsWith("child.com") || image.Name.EndsWith("tsr_hook.com")) {
                asserts.Map.IsUnchanged(image.StartAddress, (int)image.SizeInBytes).Should().BeFalse(
                    $"{image.Name} shares its segment with another loaded program, so the second "
                    + "load into the watched area marks the bytes it changes");
            } else if (image.Name.EndsWith("dos_exec_master.000")) {
                asserts.Map.IsUnchanged(image.StartAddress, (int)image.SizeInBytes).Should().BeTrue(
                    $"the loader writes of {image.Name} are not marked");
            }
        }
    }

    [Fact]
    public void TsrBatchWatchesEveryLoadedProgram() {
        // Arrange / Act
        AsmScenario scenario = AsmScenarioCatalog.TsrBatchContinues();
        using AsmRunScope scope = scenario.Prepare();
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);
        ChangedMemoryMapAssertions asserts = new(run);

        // Assert
        asserts.Images.Should().HaveCountGreaterThanOrEqualTo(2);
        foreach (LoadedImage image in asserts.Images) {
            uint end = image.StartAddress + image.SizeInBytes;
            asserts.Map.WatchedAreas.Should().Contain(
                area => area.Start <= image.StartAddress && area.End >= end,
                $"image {image.Name} is watched");
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle) {
        for (int i = 0; i + needle.Length <= haystack.Length; i++) {
            bool match = true;
            for (int j = 0; j < needle.Length && match; j++) {
                match = haystack[i + j] == needle[j];
            }
            if (match) {
                return i;
            }
        }
        throw new InvalidOperationException("Pattern not found in fixture file");
    }

    private sealed class ChangedMemoryMapAssertions {
        public ChangedMemoryMapAssertions(AsmFixtureRun run) {
            GeneratorInputs inputs = run.DependencyInjection.GeneratorInputsCollector.Capture();
            Map = inputs.ChangedMemory;
            Images = inputs.ProgramImages.Images;
        }

        public ChangedMemorySnapshot Map { get; }

        public IReadOnlyList<LoadedImage> Images { get; }
    }
}
