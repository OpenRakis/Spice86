namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class ShutdownWriteBackTests {
    [Fact]
    public void Dispose_WithDirtyMountedFloppy_PersistsImageBytesToDisk() {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Spice86_ShutdownWriteBack_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string imagePath = System.IO.Path.Combine(tempDir, "shutdown-floppy.img");

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();
            bool disposed = false;
            try {
                DosDriveManager driveManager = spice86.Machine.Dos.DosDriveManager;
                byte[] image = new Fat12ImageBuilder().Build();
                driveManager.MountFloppyImage('A', image, imagePath);

                bool writeSucceeded = driveManager.WriteToImage(0, 0x120, new byte[] { 0x11, 0x22 }, 0, 2);
                writeSucceeded.Should().BeTrue();

                bool found = driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
                found.Should().BeTrue();
                floppy.Should().NotBeNull();
                if (floppy is null) {
                    throw new InvalidOperationException("Expected mounted floppy drive on A:");
                }

                floppy.IsDirty.Should().BeTrue();

                // Act
                spice86.Dispose();
                disposed = true;

                // Assert
                System.IO.File.Exists(imagePath).Should().BeTrue();
                byte[] onDisk = System.IO.File.ReadAllBytes(imagePath);
                onDisk[0x120].Should().Be(0x11);
                onDisk[0x121].Should().Be(0x22);
            } finally {
                if (!disposed) {
                    spice86.Dispose();
                }
            }
        } finally {
            if (System.IO.Directory.Exists(tempDir)) {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
