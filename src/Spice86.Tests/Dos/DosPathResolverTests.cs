namespace Spice86.Tests.Dos;

using FluentAssertions;

using Moq;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

public class DosPathResolverTests {
    private static readonly string MountPoint = Path.GetFullPath(@"Resources\MountPoint");

    [Theory]
    [InlineData(@"bar", @"/foo/bar")]
    [InlineData(@"bar/", @"/foo/bar/")]
    [InlineData(@"bar\", @"/foo/bar/")]
    [InlineData(@"BAR", @"/foo/bar")]
    [InlineData(@".\bAr", @"/foo/bar")]
    [InlineData(@".\bAr\nothere", @"")]
    [InlineData(@".", @"/foo")]
    [InlineData(@".\", @"/foo")]
    [InlineData(@"..\", @"")]
    [InlineData(@"..", @"")]
    [InlineData(@"d:\foo\bar", @"/foo/bar")]
    [InlineData(@"d:bar", @"/foo/bar")]
    public void SetCurrentDir(string newCurrentDir, string expected) {
        // Arrange
        DosPathResolver dosPathResolver = ArrangeDosPathResolver('D', @"D:\foo");

        // Act
        dosPathResolver.SetCurrentDir(newCurrentDir);

        // Assert
        string actual = dosPathResolver.CurrentHostDirectory.Replace(ConvertUtils.ToSlashPath(MountPoint), "");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(@"bar")]
    [InlineData(@"BAR")]
    [InlineData(@"BAR\")]
    [InlineData(@"BAR/")]
    [InlineData(@"../foo/BAR")]
    [InlineData(@"..\foo\BAR")]
    [InlineData(@"./BAR")]
    [InlineData(@".\BAR")]
    [InlineData(@"D:BAR")]
    [InlineData(@"D:BAR\")]
    [InlineData(@"D:BAR/")]
    [InlineData(@"D:\bar\..\Foo")]
    public void RelativePaths(string dosPath) {
        // Arrange
        DosPathResolver dosPathResolver = ArrangeDosPathResolver('D', @"foo");

        // Act
        string? actual = dosPathResolver.TryGetFullHostPathFromDos(dosPath);

        // Assert
        actual.Should().NotBeNull($"'{dosPath}' should be resolvable");
        Directory.Exists(actual).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"\FoO")]
    [InlineData(@"/FOo")]
    [InlineData(@"/fOO/")]
    [InlineData(@"/Foo\")]
    [InlineData(@"\FoO\")]
    [InlineData(@"D:\FoO\BAR\")]
    public void AbsolutePaths(string dosPath) {
        // Arrange
        DosPathResolver dosPathResolver = ArrangeDosPathResolver('D', @"foo");
        string? actual = dosPathResolver.TryGetFullHostPathFromDos(dosPath);

        // Assert
        actual.Should().NotBeNull($"'{dosPath}' should be resolvable");
        Directory.Exists(actual).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"E:\FolDeR")]
    [InlineData(@"bAr")]
    public void MultipleDrives(string dosPath) {
        // Arrange
        DosPathResolver dosPathResolver = ArrangeDosPathResolver('D', @"foo");
        dosPathResolver.DriveMap.Add('E', new MountedFolder('E', Path.GetFullPath(@"Resources\MountPoint\drive2")));
        string? actual = dosPathResolver.TryGetFullHostPathFromDos(dosPath);

        // Assert
        actual.Should().NotBeNull($"'{dosPath}' should be resolvable");
        Directory.Exists(actual).Should().BeTrue();
    }

    private static DosPathResolver ArrangeDosPathResolver(char currentDriveLetter, string currentDosPath) {
        var loggerService = new Mock<ILoggerService>();
        var configuration = new Configuration { CDrive = MountPoint };
        var dosPathResolver = new DosPathResolver(loggerService.Object, configuration);
        dosPathResolver.DriveMap.Add(currentDriveLetter, new MountedFolder(currentDriveLetter, MountPoint));
        dosPathResolver.SetCurrentDir(currentDosPath);
        return dosPathResolver;
    }
}