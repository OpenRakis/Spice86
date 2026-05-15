namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Generates DOS-compatible 8.3 aliases (e.g. "MYDOCU~1.TXT") for VFAT long
/// file names. The algorithm follows the canonical Windows behaviour: strip
/// disallowed characters, take the leading characters of the cleaned
/// basename, append "~&lt;n&gt;" where the numeric tail starts at 1 and grows
/// until no collision with the provided existing-names set remains. Past ~9
/// the prefix shrinks to keep the 8-character limit.
/// </summary>
public sealed class Dos83AliasGenerator
{
    private const int MaxBaseLength = 8;
    private const int MaxExtensionLength = 3;

    /// <summary>Generates a non-colliding 8.3 alias for the supplied long name.</summary>
    /// <param name="longName">The user-visible long file name.</param>
    /// <param name="existingShortNames">Existing 8.3 names in the target directory.</param>
    public string GenerateAlias(string longName, ISet<string> existingShortNames)
    {
        string sanitizedBase = SanitizeBaseName(longName);
        string sanitizedExtension = SanitizeExtension(longName);
        int counter = 1;
        while (true)
        {
            string candidate = BuildCandidate(sanitizedBase, sanitizedExtension, counter);
            if (!existingShortNames.Contains(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }

    private static string SanitizeBaseName(string longName)
    {
        int lastDot = longName.LastIndexOf('.');
        string rawBase = lastDot <= 0 ? longName : longName.Substring(0, lastDot);
        return KeepAllowedCharacters(rawBase);
    }

    private static string SanitizeExtension(string longName)
    {
        int lastDot = longName.LastIndexOf('.');
        if (lastDot < 0 || lastDot == longName.Length - 1)
        {
            return string.Empty;
        }
        string rawExtension = longName.Substring(lastDot + 1);
        string cleaned = KeepAllowedCharacters(rawExtension);
        if (cleaned.Length > MaxExtensionLength)
        {
            cleaned = cleaned.Substring(0, MaxExtensionLength);
        }
        return cleaned;
    }

    private static string KeepAllowedCharacters(string source)
    {
        StringBuilder builder = new(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];
            if (character == ' ' || character == '.')
            {
                continue;
            }
            char upperCharacter = char.ToUpperInvariant(character);
            if (DosNameConverter.IsAllowedDosCharacter(upperCharacter))
            {
                builder.Append(upperCharacter);
            }
        }
        return builder.ToString();
    }

    private static string BuildCandidate(string sanitizedBase, string sanitizedExtension, int counter)
    {
        string tail = "~" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int availableBaseLength = MaxBaseLength - tail.Length;
        if (availableBaseLength < 1)
        {
            availableBaseLength = 1;
        }
        string truncatedBase = sanitizedBase.Length > availableBaseLength
            ? sanitizedBase.Substring(0, availableBaseLength)
            : sanitizedBase;
        string assembledBase = truncatedBase + tail;
        if (sanitizedExtension.Length == 0)
        {
            return assembledBase;
        }
        return assembledBase + "." + sanitizedExtension;
    }
}
