namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Tests.Dos.FileSystem;
using Spice86.Tests.Utility;

using System;
using System.IO;

using Xunit;

public sealed class MountStatusBatchCommandTests : IDisposable {
    private readonly TempFile _tempFile;
    private readonly DosTestFixture _fixture;

    public MountStatusBatchCommandTests() {
        _tempFile = new TempFile("MountStatusTest");
        _fixture = new DosTestFixture(_tempFile.Path);
    }

    public void Dispose() {
        _fixture.Dispose();
        _tempFile.Dispose();
    }

    [Fact]
    public void Mount_NoArguments_ListsDriveLabels() {
        VirtualDrive cDrive = _fixture.DriveManager.GetDrive<VirtualDrive>('C');
        cDrive.Label = "HOSTDRV";

        string output = ExecuteAndReadOutput("MOUNT", "MOUNT.TXT");
        string expectedMountedPath = _tempFile.Path.Replace('\\', '/');

        output.Should().Contain("Label");
        output.Should().Contain("HOSTDRV");
        output.Should().Contain(expectedMountedPath);
    }

    [Fact]
    public void ImgMount_NoArguments_ListsMountedImageSetAndLabels() {
        string imagePath1 = Path.Join(_tempFile.Path, "DISK1.IMG");
        string imagePath2 = Path.Join(_tempFile.Path, "DISK2.IMG");
        byte[] imageData1 = new Fat12ImageBuilder().Build();
        byte[] imageData2 = new Fat12ImageBuilder().Build();
        File.WriteAllBytes(imagePath1, imageData1);
        File.WriteAllBytes(imagePath2, imageData2);

        _fixture.DriveManager.MountFloppyImage('A', imageData1, imagePath1);
        _fixture.DriveManager.AddFloppyImage('A', imageData2, imagePath2);

        FloppyDiskDrive floppyDrive = _fixture.DriveManager.GetDrive<FloppyDiskDrive>('A');
        floppyDrive.Label = "DISKSET";

        string output = ExecuteAndReadOutput("IMGMOUNT", "IMGMOUNT.TXT");

        output.Should().Contain("Label");
        output.Should().Contain("DISKSET");
        output.Should().Contain(imagePath1);
        output.Should().Contain(imagePath2);
    }

    [Fact]
    public void ImgMount_CdromAlias_WithAbsoluteCuePath_MountsCdRomDrive() {
        byte[] discBytes = CdRomTestFixture.CreateAudioDiscBytes(300);
        string imageDirectory = Path.Join(Path.GetTempPath(), $"Spice86_ImgMountCdromAlias_{Guid.NewGuid():N}");
        Directory.CreateDirectory(imageDirectory);
        string binPath = Path.Join(imageDirectory, "disc.bin");
        string cuePath = Path.Join(imageDirectory, "disc.cue");
        File.WriteAllBytes(binPath, discBytes);
        File.WriteAllText(cuePath,
            "FILE \"disc.bin\" BINARY\r\n" +
            "TRACK 01 AUDIO\r\n" +
            "INDEX 01 00:00:00\r\n");

        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            $"IMGMOUNT D \"{cuePath}\" -t cdrom", out _);

        launched.Should().BeFalse();
        bool hasCdRomDrive = _fixture.DriveManager.TryGetDrive<CdRomDosDrive>('D', out CdRomDosDrive? mountedDrive);
        hasCdRomDrive.Should().BeTrue();
        mountedDrive.Should().NotBeNull();
        _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("IMGMOUNT -u D:", out _);
    }

    private string ExecuteAndReadOutput(string commandName, string outputFileName) {
        string dosOutputPath = $"C:\\{outputFileName}";

        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            $"{commandName} > {dosOutputPath}", out _);

        launched.Should().BeFalse();

        string hostOutputPath = Path.Join(_tempFile.Path, outputFileName);
        File.Exists(hostOutputPath).Should().BeTrue();
        return File.ReadAllText(hostOutputPath);
    }
}
