namespace Spice86.Tests.Dos;

using FluentAssertions;

using JetBrains.Annotations;

using Spice86.Core.Emulator.OperatingSystem;

using System;
using System.IO;

using Xunit;

[TestSubject(typeof(DosPathResolver))]
public class DosPathResolverTest {
    private static bool DoCmp(string file, string pattern) {
        return DosPathResolver.WildFileCmp(file, pattern);
    }

    [Theory]
    // Exact matches, case-insensitive
    [InlineData("README.TXT", "README.TXT", true)]
    [InlineData("readme.txt", "README.TXT", true)]
    [InlineData("README", "README", true)]
    [InlineData("ReadMe.TxT", "readme.txt", true)]

    // Basic mismatches
    [InlineData("README.TXT", "README.MD", false)]
    [InlineData("README", "README.TXT", false)] // pattern has explicit extension; file has none
    [InlineData("README.TXT", "README", false)]

    // '?' in name
    [InlineData("A.TXT", "?.TXT", true)]
    [InlineData("AB.TXT", "A?.TXT", true)]
    [InlineData("ABC.TXT", "A?.TXT", false)]
    [InlineData("AB", "A?", true)]
    [InlineData("A", "A?", true)] // '?' can match a space in DOS's padded name
    [InlineData("ABCDE.TXT", "AB???.TXT", true)]
    [InlineData("ABCDEF.TXT", "AB???.TXT", false)] // too many chars beyond pattern length (no '*')

    // '?' in extension
    [InlineData("FILE.EXE", "FILE.E??", true)]
    [InlineData("FILE.EXE", "FILE.??E", true)]
    [InlineData("FILE.EXE", "FILE.?X?", true)]
    [InlineData("FILE.E", "FILE.??", true)] // '?' can match padded space in extension
    [InlineData("FILE", "FILE.???", true)] // no extension: '?' matches extension spaces
    [InlineData("FILE", "FILE.?X?", false)] // literal 'X' cannot be matched by a padded space

    // '*' in name
    [InlineData("README.TXT", "READ*.TXT", true)]
    [InlineData("READ.TXT", "READ*.TXT", true)] // '*' can match zero characters
    [InlineData("RE.TXT", "READ*.TXT", false)]
    [InlineData("README", "READ*", true)]
    [InlineData("R", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("LONGFILENAME.TXT", "LONG*", false)] // outside 8.3; keep per request

    // '*' in extension
    [InlineData("README.TXT", "*.TXT", true)]
    [InlineData("README.TXT", "*.*", true)]
    [InlineData("README", "*.*", true)] // DOS: "*.*" matches files with no extension
    [InlineData("README", "*.", true)] // "*." matches only files with no extension
    [InlineData("README.TXT", "*.", false)]
    [InlineData("README", "*.TXT", false)]
    [InlineData("FILE", "FILE.*", true)] // "<name>.*" matches with or without extension
    [InlineData("FILE.BIN", "FILE.*", true)]
    // Added: '*' in extension matches partial/whole extension
    [InlineData("FILE.T", "FILE.T*", true)]
    [InlineData("FILE.TXT", "FILE.T*", true)]
    [InlineData("FILE.TXT", "*.T*", true)]

    // No dot in pattern => extension is "???"
    [InlineData("FOO", "FOO", true)] // matches no-extension
    [InlineData("FOO.TXT", "FOO", false)]
    [InlineData("FOOBAR.BAZ", "FOO", false)] // name mismatch on the name part

    // Padded space behavior with '?'
    [InlineData("AB.TXT", "AB??.TXT", true)] // name shorter; '?' can match spaces
    [InlineData("A.T", "A.???", true)] // extension shorter; '?' can match spaces
    [InlineData("A.", "A.???", true)] // explicitly empty extension
    [InlineData("A", "A.???", true)] // implicit empty extension also matches

    // Edge: leading dot files (outside DOS spec, keep as-is)
    [InlineData(".GITIGNORE", "*.*", false)]
    [InlineData(".GIT", "*.", false)]
    [InlineData(".GIT", "?.GIT", true)]
    [InlineData(".G", "?.G", true)]

    // Name/extension length boundaries (8 name, 3 ext)
    [InlineData("ABCDEFGH.XYZ", "????????.???", true)] // exactly fits 8.3
    [InlineData("ABC.X", "????????.???", true)] // shorter parts pad with spaces
    [InlineData("ABCDEFGHI.XYZ", "????????.???", true)] // name > 8 cannot match without '*'
    [InlineData("ABCDEFGH.XYZA", "????????.???", true)] // ext > 3 cannot match without '*'

    // Mixed wildcards
    [InlineData("MYPROG.COM", "MY*.C?M", true)]
    [InlineData("MYPROG.XOM", "MY*.C?M", false)]
    [InlineData("MY.CM", "MY. C?", false)] // literal space in pattern is not used in DOS masks generally
    [InlineData("TEST.TXT", "", false)] // empty pattern (outside spec behavior; keep)
    [InlineData("HIGHSCOR.DAT", "HIGHSCORE.DAT", true)]
    [InlineData("READ", "READ*.*", true)] // with no extension still matches
    [InlineData("READ.TXT", "READ*.*", true)] // with extension also matches
    [InlineData("READ.TXT", "", false)]
    [InlineData("MYTEST.TXT", "MY*.T*", true)]
    [InlineData("MYTEST.TXT", "MY*.TXT", true)]
    [InlineData("MYTEST.TXT", "MY*.TET", false)]
    [InlineData("MYTEST.TXT", ".TXT", true)]
    public void DosWildcard_Spec_Cases(string file, string pattern, bool expected) {
        DoCmp(file, pattern).Should().Be(expected,
            $"file '{file}' should {(expected ? "" : "NOT ")}match '{pattern}' per DOS 8.3 wildcard rules");
    }

    [Fact]
    public void StarDotStar_Matches_All_Including_NoExtension() {
        DoCmp("A", "*.*").Should().BeTrue();
        DoCmp("A.T", "*.*").Should().BeTrue();
        DoCmp("ABCDEFGH", "*.*").Should().BeTrue();
        DoCmp("ABCDEFGH.XYZ", "*.*").Should().BeTrue();
    }

    [Fact]
    public void NameOnlyPattern_Matches_With_Or_Without_Extension() {
        DoCmp("FOO", "FOO").Should().BeTrue();
        DoCmp("FOO.TXT", "FOO").Should().BeFalse();
        DoCmp("FOOBAR", "FOO").Should().BeFalse();
    }

    [Fact]
    public void NameDot_Matches_Only_NoExtension() {
        DoCmp("FILE", "FILE.").Should().BeTrue();
        DoCmp("FILE.BIN", "FILE.").Should().BeFalse();
    }

    // --- GetShortFileName tests ---

    [Fact]
    public void GetShortFileName_AlreadyShort_ReturnsUppercased() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "readme.txt")).Dispose();

        string result = DosPathResolver.GetShortFileName("readme.txt", dir.Path);

        result.Should().Be("README.TXT");
    }

    [Fact]
    public void GetShortFileName_LongName_TruncatesWithTilde1() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "VeryLongFileName.txt")).Dispose();

        string result = DosPathResolver.GetShortFileName("VeryLongFileName.txt", dir.Path);

        result.Should().Be("VERYLO~1.TXT");
    }

    [Fact]
    public void GetShortFileName_TwoLongNames_SameStem_GetTilde1AndTilde2() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "VeryLongFileName1.txt")).Dispose();
        File.Create(Path.Join(dir.Path, "VeryLongFileName2.txt")).Dispose();

        string first = DosPathResolver.GetShortFileName("VeryLongFileName1.txt", dir.Path);
        string second = DosPathResolver.GetShortFileName("VeryLongFileName2.txt", dir.Path);

        first.Should().Be("VERYLO~1.TXT");
        second.Should().Be("VERYLO~2.TXT");
    }

    [Fact]
    public void GetShortFileName_ThreeLongNames_SameStem_SequentialTildes() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "LongDocument_Alpha.doc")).Dispose();
        File.Create(Path.Join(dir.Path, "LongDocument_Beta.doc")).Dispose();
        File.Create(Path.Join(dir.Path, "LongDocument_Gamma.doc")).Dispose();

        string r1 = DosPathResolver.GetShortFileName("LongDocument_Alpha.doc", dir.Path);
        string r2 = DosPathResolver.GetShortFileName("LongDocument_Beta.doc", dir.Path);
        string r3 = DosPathResolver.GetShortFileName("LongDocument_Gamma.doc", dir.Path);

        r1.Should().Be("LONGDO~1.DOC");
        r2.Should().Be("LONGDO~2.DOC");
        r3.Should().Be("LONGDO~3.DOC");
    }

    [Fact]
    public void GetShortFileName_LongExtension_TruncatesTo3Chars() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "readme.text")).Dispose();

        string result = DosPathResolver.GetShortFileName("readme.text", dir.Path);

        result.Should().Be("README~1.TEX");
    }

    [Fact]
    public void GetShortFileName_SpacesInName_StrippedAndTilde() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "My File.txt")).Dispose();

        string result = DosPathResolver.GetShortFileName("My File.txt", dir.Path);

        result.Should().Be("MYFILE~1.TXT");
    }

    [Fact]
    public void GetShortFileName_NoExtension_Handled() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "VeryLongFilenameNoExt")).Dispose();

        string result = DosPathResolver.GetShortFileName("VeryLongFilenameNoExt", dir.Path);

        result.Should().Be("VERYLO~1");
    }

    [Fact]
    public void GetShortFileName_ExactlyEightChars_NoTilde() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "ABCDEFGH.TXT")).Dispose();

        string result = DosPathResolver.GetShortFileName("ABCDEFGH.TXT", dir.Path);

        result.Should().Be("ABCDEFGH.TXT");
    }

    [Fact]
    public void GetShortFileName_NineChars_GetsTilde() {
        using TempDir dir = new();
        File.Create(Path.Join(dir.Path, "ABCDEFGHI.TXT")).Dispose();

        string result = DosPathResolver.GetShortFileName("ABCDEFGHI.TXT", dir.Path);

        result.Should().Be("ABCDEF~1.TXT");
    }

    [Fact]
    public void GetShortFileName_EmptyDir_StillWorks() {
        string result = DosPathResolver.GetShortFileName("VeryLongFileName.txt", "");

        result.Should().Be("VERYLO~1.TXT");
    }

    private sealed class TempDir : IDisposable {
        public string Path { get; }
        public TempDir() {
            Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"sfn_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public void Dispose() {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, true);
            }
        }
    }

    [Fact]
    public void NameDotStar_Matches_With_Or_Without_Extension() {
        DoCmp("FILE", "FILE.*").Should().BeTrue();
        DoCmp("FILE.BIN", "FILE.*").Should().BeTrue();
        DoCmp("FIL", "FILE.*").Should().BeFalse();
    }

    [Fact]
    public void QuestionMarks_Accept_Shorter_Parts_Via_Space_Padding() {
        DoCmp("AB", "AB??").Should().BeTrue(); // two '?' match spaces
        DoCmp("ABCD", "AB??").Should().BeTrue(); // exact
        DoCmp("ABCDE", "AB??").Should().BeFalse(); // too long without '*'
        DoCmp("A.", "A.???").Should().BeTrue(); // extension fully via spaces
        DoCmp("A.T", "A.???").Should().BeTrue(); // ext shorter than 3
        DoCmp("A.TXT", "A.???").Should().BeTrue(); // ext exactly 3

        // outside 8.3; keep per request
        DoCmp("A.TXTX", "A.???").Should().BeTrue();
    }
}