namespace Spice86.Tests.ViewModels;

using FluentAssertions;

using NSubstitute;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// Tests for <see cref="DriveMenuItemViewModel"/> and <see cref="DrivesMenuViewModel"/>
/// covering combobox population, selection handling, and drive filtering.
/// </summary>
public class DrivesMenuViewModelTests {
    private static IDiscSwapper CreateDiscSwapper() => Substitute.For<IDiscSwapper>();
    private static IDriveMountService CreateMountService() => Substitute.For<IDriveMountService>();
    private static IHostStorageProvider CreateStorageProvider() => Substitute.For<IHostStorageProvider>();

    private static IDriveStatusProvider CreateStatusProvider(IReadOnlyList<DosVirtualDriveStatus> statuses) {
        IDriveStatusProvider provider = Substitute.For<IDriveStatusProvider>();
        provider.GetDriveStatuses().Returns(statuses);
        return provider;
    }

    /// <summary>
    /// DriveMenuItemViewModel combobox should list all image file names (no "..." option).
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_ComboboxOptions_ContainsAllImages() {
        // Arrange
        IReadOnlyList<string> imagePaths = new List<string> { "/images/disk1.img", "/images/disk2.img" };

        // Act
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk1.img", "FLOPPY",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert — two images, no "..." entry (replaced by dedicated mount buttons)
        vm.ComboboxOptions.Should().HaveCount(2);
        vm.ComboboxOptions[0].Should().Be("disk1.img");
        vm.ComboboxOptions[1].Should().Be("disk2.img");
    }

    /// <summary>
    /// DriveMenuItemViewModel initial selected option should match the current image file name.
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_InitialSelectedOption_MatchesCurrentImage() {
        // Arrange
        IReadOnlyList<string> imagePaths = new List<string> { "/images/disk1.img", "/images/disk2.img" };

        // Act
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk2.img", "FLOPPY",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert
        vm.SelectedOption.Should().Be("disk2.img");
    }

    /// <summary>
    /// Selecting a known image in the combobox should call SwapToImageIndex with the correct index.
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_SelectingKnownImage_CallsSwapToImageIndex() {
        // Arrange
        IReadOnlyList<string> imagePaths = new List<string> { "/images/disk1.img", "/images/disk2.img" };
        IDiscSwapper discSwapper = CreateDiscSwapper();
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk1.img", "FLOPPY",
            discSwapper, CreateMountService(), CreateStorageProvider());

        // Act - select the second image
        vm.SelectedOption = "disk2.img";

        // Assert
        discSwapper.Received(1).SwapToImageIndex('A', 1);
    }

    /// <summary>
    /// UpdateFromStatus with new image paths should rebuild the combobox options (no "...").
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_UpdateFromStatus_WithNewPaths_RebuildsOptions() {
        // Arrange
        IReadOnlyList<string> initialPaths = new List<string> { "/images/disk1.img" };
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, initialPaths, "/images/disk1.img", "FLOPPY",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        IReadOnlyList<string> newPaths = new List<string> { "/images/disk1.img", "/images/disk3.img" };
        DosVirtualDriveStatus newStatus = new DosVirtualDriveStatus(
            'A', DosVirtualDriveType.Floppy, hasMedia: true, volumeLabel: "FLOPPY",
            currentImagePath: "/images/disk1.img", imageCount: 2, allImagePaths: newPaths);

        // Act
        vm.UpdateFromStatus(newStatus);

        // Assert — exactly two image entries, no "..."
        vm.ComboboxOptions.Should().HaveCount(2, "two images, mount buttons are separate");
        vm.ComboboxOptions[1].Should().Be("disk3.img");
    }

    /// <summary>
    /// HDD drive entry should report IsHdd true and the combobox should be disabled.
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_HddDrive_IsHddTrue() {
        // Arrange
        IReadOnlyList<string> emptyPaths = new List<string>();

        // Act
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'C', DosVirtualDriveType.Fixed, emptyPaths, string.Empty, "HARDDISK",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert
        vm.IsHdd.Should().BeTrue();
        vm.IsFloppy.Should().BeFalse();
        vm.IsCdRom.Should().BeFalse();
        vm.ComboboxOptions.Should().BeEmpty("HDD drives have no switchable images");
    }

    /// <summary>
    /// DrivesMenuViewModel.Refresh should show floppy, CD-ROM, and HDD drives in AllDrives.
    /// </summary>
    [Fact]
    public void DrivesMenuViewModel_AllDrives_ContainsFloppyCdRomAndHdd() {
        // Arrange
        IReadOnlyList<string> emptyPaths = new List<string>();
        List<DosVirtualDriveStatus> statuses = new List<DosVirtualDriveStatus> {
            new DosVirtualDriveStatus('A', DosVirtualDriveType.Floppy, hasMedia: false, volumeLabel: "", allImagePaths: emptyPaths),
            new DosVirtualDriveStatus('C', DosVirtualDriveType.Fixed, hasMedia: true, volumeLabel: "HDD"),
            new DosVirtualDriveStatus('D', DosVirtualDriveType.CdRom, hasMedia: false, volumeLabel: "", allImagePaths: emptyPaths),
        };
        IDriveStatusProvider provider = CreateStatusProvider(statuses);

        // Act - constructor calls Refresh() synchronously (no timer)
        DrivesMenuViewModel vm = new DrivesMenuViewModel(
            provider, CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert — all three drive types are shown; Memory/virtual drives are excluded
        vm.AllDrives.Should().HaveCount(3, "floppy, HDD and CD-ROM drives all appear");
        vm.AllDrives[0].DriveLetter.Should().Be('A');
        vm.AllDrives[1].DriveLetter.Should().Be('C');
        vm.AllDrives[2].DriveLetter.Should().Be('D');
    }

    /// <summary>
    /// DrivesMenuViewModel.Refresh after status change should update existing entries.
    /// </summary>
    [Fact]
    public void DrivesMenuViewModel_Refresh_WithUpdatedStatus_UpdatesExistingEntry() {
        // Arrange
        IReadOnlyList<string> paths = new List<string> { "/images/disk1.img" };
        List<DosVirtualDriveStatus> statuses = new List<DosVirtualDriveStatus> {
            new DosVirtualDriveStatus('A', DosVirtualDriveType.Floppy, hasMedia: true,
                volumeLabel: "FLOPPY", currentImagePath: "/images/disk1.img", imageCount: 1, allImagePaths: paths),
        };
        IDriveStatusProvider provider = CreateStatusProvider(statuses);
        DrivesMenuViewModel vm = new DrivesMenuViewModel(
            provider, CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        IReadOnlyList<string> newPaths = new List<string> { "/images/disk1.img", "/images/disk2.img" };
        List<DosVirtualDriveStatus> updatedStatuses = new List<DosVirtualDriveStatus> {
            new DosVirtualDriveStatus('A', DosVirtualDriveType.Floppy, hasMedia: true,
                volumeLabel: "FLOPPY", currentImagePath: "/images/disk1.img", imageCount: 2, allImagePaths: newPaths),
        };
        provider.GetDriveStatuses().Returns(updatedStatuses);

        // Act
        vm.Refresh();

        // Assert - A: floppy is updated + placeholder D: CD-ROM is always present
        DriveMenuItemViewModel floppyEntry = vm.AllDrives.First(d => d.DriveLetter == 'A');
        floppyEntry.ComboboxOptions.Should().HaveCount(2, "two images, no '...' entry");
        vm.AllDrives.Should().Contain(d => d.DriveLetter == 'D' && d.IsCdRom,
            "placeholder D: CD-ROM should remain present even after a floppy-only status update");
    }

    /// <summary>
    /// DrivesMenuViewModel should always include a placeholder D: CD-ROM entry when no CD drives exist,
    /// matching the convention that A: and B: floppy slots are always visible.
    /// </summary>
    [Fact]
    public void DrivesMenuViewModel_NoCdDriveInStatus_ShowsPlaceholderCdSlot() {
        // Arrange — only floppy drives, no CD drives
        IReadOnlyList<string> emptyPaths = new List<string>();
        List<DosVirtualDriveStatus> statuses = new List<DosVirtualDriveStatus> {
            new DosVirtualDriveStatus('A', DosVirtualDriveType.Floppy, hasMedia: false, volumeLabel: "", allImagePaths: emptyPaths),
            new DosVirtualDriveStatus('B', DosVirtualDriveType.Floppy, hasMedia: false, volumeLabel: "", allImagePaths: emptyPaths),
        };
        IDriveStatusProvider provider = CreateStatusProvider(statuses);

        // Act
        DrivesMenuViewModel vm = new DrivesMenuViewModel(
            provider, CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert — placeholder D: CD drive should be automatically injected
        vm.AllDrives.Should().Contain(d => d.DriveLetter == 'D' && d.IsCdRom,
            "a placeholder CD-ROM slot should always appear so the user can mount an image");
    }
}
