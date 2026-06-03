namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;

/// <summary>
/// Converts host names to DOS 8.3-compatible names.
/// </summary>
public static class DosNameConverter
{
    private static readonly char[] InvalidCharacters = new[] {
        '"', '*', '+', ',', '/', ':', ';', '<', '=', '>', '?', '[', '\\', ']', '|'
    };

    /// <summary>
    /// CP437 extended uppercase table for byte values 0x80..0xA4 (37 entries),
    /// ported byte-for-byte from dosbox-staging src/dos/dos_files.cpp DOS_ToUpper.
    /// A zero entry means the source byte already is the uppercase form (no change).
    /// </summary>
    private static readonly byte[] Cp437ExtendedUppercase = new byte[] {
        0x00, 0x9a, 0x45, 0x41, 0x8E, 0x41, 0x8F, 0x80, 0x45, 0x45, 0x45, 0x49, 0x49, 0x49, 0x00, 0x00,
        0x00, 0x92, 0x00, 0x4F, 0x99, 0x4F, 0x55, 0x55, 0x59, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x41, 0x49, 0x4F, 0x55, 0xA5
    };

    /// <summary>
    /// DOS uppercase mapping for a single byte. Mirrors dosbox-staging DOS_ToUpper:
    /// ASCII lowercase a..z folds to A..Z; bytes 0x80..0xA4 use the CP437 table.
    /// All other bytes pass through unchanged.
    /// </summary>
    /// <param name="b">Source byte.</param>
    /// <returns>Uppercase byte.</returns>
    public static byte DosToUpper(byte b)
    {
        if (b > 0x60 && b < 0x7B)
        {
            return (byte)(b - 0x20);
        }

        if (b > 0x7F && b < 0xA5)
        {
            byte mapped = Cp437ExtendedUppercase[b - 0x80];
            if (mapped != 0)
            {
                return mapped;
            }
        }

        return b;
    }

    /// <summary>
    /// Converts a host file name into uppercase 8.3 format.
    /// </summary>
    /// <param name="name">Source file name.</param>
    /// <returns>Uppercase DOS 8.3 name.</returns>
    public static string Convert(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        }

        string trimmedName = name.Trim();
        string[] parts = trimmedName.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            throw new ArgumentException("Name does not contain valid DOS characters.", nameof(name));
        }

        string basePart;
        string extensionPart;

        if (parts.Length == 1)
        {
            basePart = parts[0];
            extensionPart = string.Empty;
        }
        else
        {
            extensionPart = parts[parts.Length - 1];
            int extensionStartIndex = trimmedName.LastIndexOf('.');
            basePart = trimmedName.Substring(0, extensionStartIndex);
        }

        basePart = NormalizePart(basePart, 8);
        extensionPart = NormalizePart(extensionPart, 3);

        if (string.IsNullOrEmpty(extensionPart))
        {
            return basePart;
        }

        return basePart + "." + extensionPart;
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> is already DOS 8.3 compatible.
    /// </summary>
    /// <param name="name">Name to validate.</param>
    /// <returns>True when DOS-compatible.</returns>
    public static bool IsDosCompatible(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            string converted = Convert(name);
            return string.Equals(converted, ToDosUppercase(name), StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the provided character is allowed in DOS 8.3 names.
    /// Bytes 0x80..0xFF are accepted (DOS codepage extended range); the printable
    /// ASCII range 0x20..0x7E is filtered against the FAT-reserved character set.
    /// </summary>
    /// <param name="character">Character to validate.</param>
    /// <returns>True when character is valid.</returns>
    public static bool IsAllowedDosCharacter(char character)
    {
        if (character < (char)0x20)
        {
            return false;
        }

        if (character >= (char)0x80)
        {
            return character <= (char)0xFF;
        }

        if (character > (char)0x7E)
        {
            return false;
        }

        for (int i = 0; i < InvalidCharacters.Length; i++)
        {
            if (InvalidCharacters[i] == character)
            {
                return false;
            }
        }

        return true;
    }

    private static string ToDosUppercase(string value)
    {
        char[] buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c <= (char)0xFF)
            {
                buffer[i] = (char)DosToUpper((byte)c);
            }
            else
            {
                buffer[i] = c;
            }
        }
        return new string(buffer);
    }

    private static string NormalizePart(string value, int maxLength)
    {
        string uppercaseValue = ToDosUppercase(value);
        if (uppercaseValue.Length > maxLength)
        {
            uppercaseValue = uppercaseValue.Substring(0, maxLength);
        }

        for (int i = 0; i < uppercaseValue.Length; i++)
        {
            if (!IsAllowedDosCharacter(uppercaseValue[i]))
            {
                throw new ArgumentException("Name contains characters invalid for DOS 8.3 format.", nameof(value));
            }
        }

        return uppercaseValue;
    }
}
