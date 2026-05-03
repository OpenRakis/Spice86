namespace Spice86.Tests.ViewModels;

using FluentAssertions;

using NSubstitute;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using System.Collections.Generic;

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
    /// DriveMenuItemViewModel combobox should list all image file names plus the "..." option.
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_ComboboxOptions_ContainsAllImagesAndEllipsis() {
        // Arrange
        IReadOnlyList<string> imagePaths = new List<string> { "/images/disk1.img", "/images/disk2.img" };

        // Act
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk1.img",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        // Assert
        vm.ComboboxOptions.Should().HaveCount(3);
        vm.ComboboxOptions[0].Should().Be("disk1.img");
        vm.ComboboxOptions[1].Should().Be("disk2.img");
        vm.ComboboxOptions[2].Should().Be("...");
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
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk2.img",
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
            'A', DosVirtualDriveType.Floppy, imagePaths, "/images/disk1.img",
            discSwapper, CreateMountService(), CreateStorageProvider());

        // Act - select the second image
        vm.SelectedOption = "disk2.img";

        // Assert
        discSwapper.Received(1).SwapToImageIndex('A', 1);
    }

    /// <summary>
    /// UpdateFromStatus with new image paths should rebuild the combobox options.
    /// </summary>
    [Fact]
    public void DriveMenuItemViewModel_UpdateFromStatus_WithNewPaths_RebuildsOptions() {
        // Arrange
        IReadOnlyList<string> initialPaths = new List<string> { "/images/disk1.img" };
        DriveMenuItemViewModel vm = new DriveMenuItemViewModel(
            'A', DosVirtualDriveType.Floppy, initialPaths, "/images/disk1.img",
            CreateDiscSwapper(), CreateMountService(), CreateStorageProvider());

        IReadOnlyList<string> newPaths = new List<string> { "/images/disk1.img", "/images/disk3.img" };
        DosVirtualDriveStatus newStatus = new DosVirtualDriveStatus(
            'A', DosVirtualDriveType.Floppy, hasMedia: true, volumeLabel: "FLOPPY",
            currentImagePath: "/images/disk1.img", imageCount: 2, allImagePaths: newPaths);

        // Act
        vm.UpdateFromStatus(newStatus);

        // Assert
        vm.ComboboxOptions.Should().HaveCount(3, "two images plus '...'");
        vm.ComboboxOptions[1].Should().Be("disk3.img");
    }

    /// <summary>
    /// DrivesMenuViewModel.Refresh should only show floppy and CD-ROM drives in AllDrives.
    /// </summary>
    [Fact]
    public void DrivesMenuViewModel_AllDrives_OnlyContainsFloppyAndCdRom() {
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

        // Assert
        vm.AllDrives.Should().HaveCount(2, "only floppy and CD-ROM drives should appear");
        vm.AllDrives[0].DriveLetter.Should().Be('A');
        vm.AllDrives[1].DriveLetter.Should().Be('D');
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

        // Assert
        vm.AllDrives.Should().HaveCount(1);
        vm.AllDrives[0].ComboboxOptions.Should().HaveCount(3, "two images plus '...'");
    }
}
