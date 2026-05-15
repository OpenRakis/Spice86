namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class ShutdownWriteBackTests
{
    [Fact]
    public void Dispose_WithDirtyMountedFloppy_PersistsImageBytesToDisk()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Spice86_ShutdownWriteBack_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string imagePath = System.IO.Path.Combine(tempDir, "shutdown-floppy.img");

        try
        {
            Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();
            bool disposed = false;
            try
            {
                DosDriveManager driveManager = spice86.Machine.Dos.DosDriveManager;
                byte[] image = new Fat12ImageBuilder().Build();
                driveManager.MountFloppyImage('A', image, imagePath);

                bool writeSucceeded = driveManager.WriteToImage(0, 0x120, new byte[] { 0x11, 0x22 }, 0, 2);
                writeSucceeded.Should().BeTrue();

                bool found = driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
                found.Should().BeTrue();
                floppy.Should().NotBeNull();
                if (floppy is null)
                {
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
            }
            finally
            {
                if (!disposed)
                {
                    spice86.Dispose();
                }
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void RequestPause_WithDirtyMountedFloppy_PersistsImageBytesToDisk()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Spice86_PauseWriteBack_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string imagePath = System.IO.Path.Combine(tempDir, "pause-floppy.img");

        try
        {
            Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();
            try
            {
                DosDriveManager driveManager = spice86.Machine.Dos.DosDriveManager;
                byte[] image = new Fat12ImageBuilder().Build();
                driveManager.MountFloppyImage('A', image, imagePath);

                bool writeSucceeded = driveManager.WriteToImage(0, 0x130, new byte[] { 0x33, 0x44 }, 0, 2);
                writeSucceeded.Should().BeTrue();

                // Act
                spice86.Machine.PauseHandler.RequestPause("test pause write-back");

                // Assert
                System.IO.File.Exists(imagePath).Should().BeTrue();
                byte[] onDisk = System.IO.File.ReadAllBytes(imagePath);
                onDisk[0x130].Should().Be(0x33);
                onDisk[0x131].Should().Be(0x44);
            }
            finally
            {
                if (spice86.Machine.PauseHandler.IsPaused)
                {
                    spice86.Machine.PauseHandler.Resume();
                }

                spice86.Dispose();
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void HddImageMount_FullLifecycle_PausesAndShutdownPersistAllWritesToDisk()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Spice86_HddLifecycle_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string imagePath = System.IO.Path.Combine(tempDir, "hdd.img");

        try
        {
            Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();
            bool disposed = false;
            try
            {
                DosDriveManager driveManager = spice86.Machine.Dos.DosDriveManager;
                byte[] image = new Fat12ImageBuilder().Build();
                driveManager.MountFloppyImage('C', image, imagePath);

                // First write -> flushed by pause
                bool writePreShutdown = driveManager.WriteToImage(2, 0x200, new byte[] { 0x77, 0x88 }, 0, 2);
                writePreShutdown.Should().BeTrue();

                // Act: pause must persist the first write to disk.
                spice86.Machine.PauseHandler.RequestPause("hdd pause write-back");

                // Assert: on-disk reflects first write before shutdown.
                System.IO.File.Exists(imagePath).Should().BeTrue();
                byte[] onDiskAfterPause = System.IO.File.ReadAllBytes(imagePath);
                onDiskAfterPause[0x200].Should().Be(0x77);
                onDiskAfterPause[0x201].Should().Be(0x88);

                spice86.Machine.PauseHandler.Resume();

                // Second write -> flushed by shutdown
                bool writePostResume = driveManager.WriteToImage(2, 0x210, new byte[] { 0x99, 0xAA }, 0, 2);
                writePostResume.Should().BeTrue();

                // Act: shutdown.
                spice86.Dispose();
                disposed = true;

                // Assert: on-disk reflects both writes after shutdown.
                byte[] onDiskAfterShutdown = System.IO.File.ReadAllBytes(imagePath);
                onDiskAfterShutdown[0x200].Should().Be(0x77);
                onDiskAfterShutdown[0x201].Should().Be(0x88);
                onDiskAfterShutdown[0x210].Should().Be(0x99);
                onDiskAfterShutdown[0x211].Should().Be(0xAA);
            }
            finally
            {
                if (!disposed)
                {
                    if (spice86.Machine.PauseHandler.IsPaused)
                    {
                        spice86.Machine.PauseHandler.Resume();
                    }
                    spice86.Dispose();
                }
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Dispose_WithDirtyMultiImageFloppy_PersistsAllDirtyImagesToDisk()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "Spice86_ShutdownWriteBack_Multi_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string imagePath1 = System.IO.Path.Combine(tempDir, "shutdown-floppy-1.img");
        string imagePath2 = System.IO.Path.Combine(tempDir, "shutdown-floppy-2.img");

        try
        {
            Spice86DependencyInjection spice86 = new Spice86Creator("add").Create();
            bool disposed = false;
            try
            {
                DosDriveManager driveManager = spice86.Machine.Dos.DosDriveManager;
                byte[] image1 = new Fat12ImageBuilder().Build();
                byte[] image2 = new Fat12ImageBuilder().Build();

                driveManager.MountFloppyImage('A', image1, imagePath1);
                driveManager.AddFloppyImage('A', image2, imagePath2);

                driveManager.SwapFloppyToIndex('A', 0);
                bool writeFirst = driveManager.WriteToImage(0, 0x150, new byte[] { 0xAA }, 0, 1);
                writeFirst.Should().BeTrue();

                driveManager.SwapFloppyToIndex('A', 1);
                bool writeSecond = driveManager.WriteToImage(0, 0x1A0, new byte[] { 0xBB }, 0, 1);
                writeSecond.Should().BeTrue();

                // Act
                spice86.Dispose();
                disposed = true;

                // Assert
                System.IO.File.Exists(imagePath1).Should().BeTrue();
                System.IO.File.Exists(imagePath2).Should().BeTrue();
                byte[] onDisk1 = System.IO.File.ReadAllBytes(imagePath1);
                byte[] onDisk2 = System.IO.File.ReadAllBytes(imagePath2);
                onDisk1[0x150].Should().Be(0xAA);
                onDisk2[0x1A0].Should().Be(0xBB);
            }
            finally
            {
                if (!disposed)
                {
                    spice86.Dispose();
                }
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
