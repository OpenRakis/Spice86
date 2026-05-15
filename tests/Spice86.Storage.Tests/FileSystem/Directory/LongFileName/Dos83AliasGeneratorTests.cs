namespace Spice86.Storage.Tests.FileSystem.Directory.LongFileName;

using System;
using System.Collections.Generic;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using Xunit;

/// <summary>
/// Behavioural tests for the 8.3 alias generator that produces DOS-compatible
/// short names for VFAT long file names.
/// </summary>
public sealed class Dos83AliasGeneratorTests
{
    /// <summary>A short basename receives the canonical ~1 suffix.</summary>
    [Fact]
    public void GenerateAlias_SimpleLongName_ProducesTildeOneAlias()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);

        // Act
        string alias = generator.GenerateAlias("MyDocument.txt", existing);

        // Assert
        alias.Should().Be("MYDOCU~1.TXT");
    }

    /// <summary>The leading 6 chars of a longer basename feed the alias.</summary>
    [Fact]
    public void GenerateAlias_TruncatesBaseNameToSixChars()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);

        // Act
        string alias = generator.GenerateAlias("AnotherVeryLongFile.html", existing);

        // Assert
        alias.Should().Be("ANOTHE~1.HTM");
    }

    /// <summary>A long name without an extension produces an alias without a dot.</summary>
    [Fact]
    public void GenerateAlias_NoExtension_ProducesAliasWithoutDot()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);

        // Act
        string alias = generator.GenerateAlias("LongFileName", existing);

        // Assert
        alias.Should().Be("LONGFI~1");
    }

    /// <summary>Spaces and dots inside the basename are stripped before truncation.</summary>
    [Fact]
    public void GenerateAlias_StripsSpacesAndInternalDots()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);

        // Act
        string alias = generator.GenerateAlias("Hello World.tar.gz", existing);

        // Assert
        alias.Should().Be("HELLOW~1.GZ");
    }

    /// <summary>Collision with an existing alias bumps the tilde index.</summary>
    [Fact]
    public void GenerateAlias_CollisionBumpsTildeIndex()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase) { "MYDOCU~1.TXT", "MYDOCU~2.TXT" };

        // Act
        string alias = generator.GenerateAlias("MyDocument.txt", existing);

        // Assert
        alias.Should().Be("MYDOCU~3.TXT");
    }

    /// <summary>Past ~9, the base prefix shrinks to fit a two-digit counter.</summary>
    [Fact]
    public void GenerateAlias_CollisionPastNine_ShrinksBaseAndUsesTwoDigitCounter()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        for (int counter = 1; counter <= 9; counter++)
        {
            existing.Add($"MYDOCU~{counter}.TXT");
        }

        // Act
        string alias = generator.GenerateAlias("MyDocument.txt", existing);

        // Assert
        alias.Should().Be("MYDOC~10.TXT");
    }

    /// <summary>Disallowed DOS characters are dropped from the basename.</summary>
    [Fact]
    public void GenerateAlias_DropsDisallowedCharacters()
    {
        // Arrange
        Dos83AliasGenerator generator = new();
        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);

        // Act
        string alias = generator.GenerateAlias("a+b,c=d.txt", existing);

        // Assert
        alias.Should().Be("ABCD~1.TXT");
    }
}
