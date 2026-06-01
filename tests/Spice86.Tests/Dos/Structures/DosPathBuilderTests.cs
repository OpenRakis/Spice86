namespace Spice86.Tests.Dos.Structures;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;

using Xunit;

public class DosPathBuilderTests {
    [Theory]
    [InlineData("", DosSpecialFileName.Empty)]
    [InlineData("foo", DosSpecialFileName.None)]
    [InlineData(" foo", DosSpecialFileName.None)]
    [InlineData(".foo", DosSpecialFileName.None)]
    // DOS semantics: trailing whitespace on a file name is silently stripped during canonicalization
    // (FreeDOS 8.3 entries are space-padded; trailing spaces match naturally). It is a valid file name,
    // not an "Invalid"/reserved one. AITD's TATOU.COM uses an FCB-padded buffer for AH=3Dh Open.
    [InlineData("foo ", DosSpecialFileName.None)]
    [InlineData("Info.cc1            ", DosSpecialFileName.None)]
    [InlineData("FILE.EXT  ", DosSpecialFileName.None)]
    // DOS semantics: a single trailing dot means "no extension" (FreeDOS truename strips it).
    // It is a valid file name, not an "Invalid"/reserved one. See kernel/kernel/newstuff.c "strip trailing dot".
    [InlineData("foo.", DosSpecialFileName.None)]
    [InlineData("foo.bar", DosSpecialFileName.None)]
    // Two or more trailing dots are still ill-formed (matches FreeDOS PNE_DOT multi-dot rejection).
    [InlineData("foo..", DosSpecialFileName.Invalid)]
    [InlineData("foo...", DosSpecialFileName.Invalid)]
    [InlineData(".", DosSpecialFileName.CurrentDirectory)]
    [InlineData("..", DosSpecialFileName.ParentDirectory)]
    [InlineData("NUL", DosSpecialFileName.Null)]
    [InlineData("Nul", DosSpecialFileName.Null)]
    [InlineData("NUL.TXT", DosSpecialFileName.Null)]
    [InlineData("NUL .TXT", DosSpecialFileName.Null)]
    [InlineData("NULL", DosSpecialFileName.None)]
    [InlineData("con", DosSpecialFileName.Console)]
    [InlineData("CoN.TAR.GZ", DosSpecialFileName.Console)]
    [InlineData("AUx", DosSpecialFileName.Auxiliary)]
    [InlineData("PRN", DosSpecialFileName.Printer)]
    [InlineData("COM", DosSpecialFileName.None)]
    [InlineData("COM1", DosSpecialFileName.SerialPort1)]
    [InlineData("cOM2", DosSpecialFileName.SerialPort2)]
    [InlineData("coM3", DosSpecialFileName.SerialPort3)]
    [InlineData("com4", DosSpecialFileName.SerialPort4)]
    [InlineData("cOm5", DosSpecialFileName.SerialPort5)]
    [InlineData("COm6", DosSpecialFileName.SerialPort6)]
    [InlineData("COM7", DosSpecialFileName.SerialPort7)]
    [InlineData("COM8", DosSpecialFileName.SerialPort8)]
    [InlineData("COM9", DosSpecialFileName.SerialPort9)]
    [InlineData("COM¹", DosSpecialFileName.SerialPort1)]
    [InlineData("COM²", DosSpecialFileName.SerialPort2)]
    [InlineData("COM³", DosSpecialFileName.SerialPort3)]
    [InlineData("LPT", DosSpecialFileName.None)]
    [InlineData("LPT1", DosSpecialFileName.ParallelPort1)]
    [InlineData("lPT2", DosSpecialFileName.ParallelPort2)]
    [InlineData("lpT3", DosSpecialFileName.ParallelPort3)]
    [InlineData("lpt4", DosSpecialFileName.ParallelPort4)]
    [InlineData("lPt5", DosSpecialFileName.ParallelPort5)]
    [InlineData("LPt6", DosSpecialFileName.ParallelPort6)]
    [InlineData("LPT7", DosSpecialFileName.ParallelPort7)]
    [InlineData("LPT8", DosSpecialFileName.ParallelPort8)]
    [InlineData("LPT9", DosSpecialFileName.ParallelPort9)]
    [InlineData("LPT¹", DosSpecialFileName.ParallelPort1)]
    [InlineData("LPT²", DosSpecialFileName.ParallelPort2)]
    [InlineData("LPT³", DosSpecialFileName.ParallelPort3)]
    internal void SpecialFileNames_DefaultSettings(string fileName, DosSpecialFileName expectedSpecialFileName) {
        // Arrange
        using DosPathBuilder builder = DosPathBuilder.Create();

        // Act
        // Documentation for ParseSpecialFileName() indicates that leading white space must always be trimmed.
        ReadOnlySpan<char> fileNameTrimmed = fileName.AsSpan().TrimStart();
        DosSpecialFileName result = builder.ParseSpecialFileName(fileNameTrimmed);

        // Assert
        result.Should().Be(expectedSpecialFileName);
    }

    [Theory]
    [InlineData("COM¹", DosSpecialFileName.Invalid)]
    [InlineData("COM²", DosSpecialFileName.Invalid)]
    [InlineData("COM³", DosSpecialFileName.Invalid)]
    [InlineData("LPT¹", DosSpecialFileName.Invalid)]
    [InlineData("LPT²", DosSpecialFileName.Invalid)]
    [InlineData("LPT³", DosSpecialFileName.Invalid)]
    internal void SpecialFileNames_NoDeviceSuperscriptSetting(string fileName, DosSpecialFileName expectedSpecialFileName) {
        // Arrange
        DosPathBuilder builder = DosPathBuilder.Create();
        builder.SpecialFileNameSettings = DosSpecialFileNameSettings.NoDeviceSuperscriptDigits;

        try {
            // Act
            // Documentation for ParseSpecialFileName() indicates that leading white space must always be trimmed.
            ReadOnlySpan<char> fileNameTrimmed = fileName.AsSpan().TrimStart();
            DosSpecialFileName result = builder.ParseSpecialFileName(fileNameTrimmed);

            // Assert
            result.Should().Be(expectedSpecialFileName);
        } finally {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData('A', null, null, null, @"A:\")]
    [InlineData('Z', null, null, null, @"Z:\")]
    [InlineData('c', "FOO", null, null, @"C:\FOO")]
    [InlineData('C', "/foo", null, null, @"C:\foo")]
    [InlineData('c', "/foo", "bar", null, @"C:\bar")]
    [InlineData('c', " /foo", null, "bar", @"C:\foo\bar")]
    [InlineData('z', "// foo", null, null, @"Z:\foo")]
    [InlineData('z', @"//.foo\ \\bar\", null, null, @"Z:\.foo\bar")]
    [InlineData('Q', "foo", "/baz", "bar", @"Q:\baz\bar")]
    [InlineData('B', null, null, "'[f00]_", @"B:\'[f00]_")]
    [InlineData('B', null, "'[f00]_", null, @"B:\'[f00]_")]
    [InlineData('r', "foo;bar", null, null, @"R:\foo;bar")]
    // DOS semantics: a single trailing dot on a path segment means "no extension"
    // and is stripped during canonicalization (FreeDOS truename "strip trailing dot").
    [InlineData('C', @"GAME\V.", null, null, @"C:\GAME\V")]
    [InlineData('C', @"V.", null, null, @"C:\V")]
    [InlineData('C', null, null, "V.", @"C:\V")]
    [InlineData('C', @"FOO.\BAR.", null, null, @"C:\FOO\BAR")]
    // DOS semantics: trailing whitespace on a path segment is stripped during canonicalization.
    // AITD passes FCB-padded ASCIIZ filenames (e.g. "Info.cc1            ") to AH=3Dh Open.
    [InlineData('C', null, null, "Info.cc1            ", @"C:\Info.cc1")]
    [InlineData('C', "GAME", null, "Info.cc1            ", @"C:\GAME\Info.cc1")]
    [InlineData('C', @"FOO  \BAR  ", null, null, @"C:\FOO\BAR")]
    [InlineData('C', null, null, "foo .", @"C:\foo")]
    public void BuildPath_Drive_Relative_Rooted_FileName(char driveLetter, string? appendRelativePath, string? appendRootedPathAfterRelative,
            string? appendFileName, string expectedPath) {
        // Arrange
        using DosPathBuilder builder = DosPathBuilder.Create();

        // Act
        DosPathBuilderResult resultSetDriveLetter = builder.SetDriveLetter(driveLetter);
        DosPathBuilderResult resultAppendRelativePath = appendRelativePath is not null
            ? builder.AppendRelativePath(appendRelativePath, out _)
            : DosPathBuilderResult.Success;
        DosPathBuilderResult resultAppendAbsolutePath = appendRootedPathAfterRelative is not null
            ? builder.AppendRootedPath(appendRootedPathAfterRelative, out _)
            : DosPathBuilderResult.Success;
        DosPathBuilderResult resultAppendFileName = appendFileName is not null
            ? builder.AppendFileName(appendFileName)
            : DosPathBuilderResult.Success;
        DosPathBuilderResult resultFreeze = builder.Freeze();
        string resultPath = builder.ToString();
        DosPathBuilderResult resultFreeze2 = builder.Freeze();

        // Assert
        resultSetDriveLetter.Should().Be(DosPathBuilderResult.Success);
        resultAppendRelativePath.Should().Be(DosPathBuilderResult.Success);
        resultAppendAbsolutePath.Should().Be(DosPathBuilderResult.Success);
        resultAppendFileName.Should().Be(DosPathBuilderResult.Success);
        resultFreeze.Should().Be(DosPathBuilderResult.Success);
        resultFreeze2.Should().Be(DosPathBuilderResult.Success);
        resultPath.Should().Be(expectedPath);
        builder.IsFrozen.Should().BeTrue();
    }

    [Theory]
    [InlineData("foo. .")]
    [InlineData("foo.. .")]
    public void AppendFileName_TrailingDotExposedByWhitespace_IsRejected(string fileName) {
        // Arrange
        using DosPathBuilder builder = DosPathBuilder.Create();
        DosPathBuilderResult resultSetDriveLetter = builder.SetDriveLetter('C');

        // Act
        DosPathBuilderResult result = builder.AppendFileName(fileName);

        // Assert
        resultSetDriveLetter.Should().Be(DosPathBuilderResult.Success);
        result.Should().Be(DosPathBuilderResult.InvalidReservedFileName);
    }

    [Theory]
    [InlineData("foo. .")]
    [InlineData("foo.. .")]
    public void AppendRelativePath_TrailingDotExposedByWhitespace_IsRejected(string relativePath) {
        // Arrange
        using DosPathBuilder builder = DosPathBuilder.Create();
        DosPathBuilderResult resultSetDriveLetter = builder.SetDriveLetter('C');

        // Act
        DosPathBuilderResult result = builder.AppendRelativePath(relativePath, out bool endsWithDirectorySeparator);

        // Assert
        resultSetDriveLetter.Should().Be(DosPathBuilderResult.Success);
        result.Should().Be(DosPathBuilderResult.InvalidReservedFileName);
        endsWithDirectorySeparator.Should().BeFalse();
    }
}
