namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;

/// <summary>
/// Converts host names to DOS 8.3-compatible names.
/// </summary>
public static class DosNameConverter {
    private static readonly char[] InvalidCharacters = new[] {
        '"', '*', '+', ',', '/', ':', ';', '<', '=', '>', '?', '[', '\\', ']', '|'
    };

    /// <summary>
    /// Converts a host file name into uppercase 8.3 format.
    /// </summary>
    /// <param name="name">Source file name.</param>
    /// <returns>Uppercase DOS 8.3 name.</returns>
    public static string Convert(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        }

        string trimmedName = name.Trim();
        string[] parts = trimmedName.Split('.', StringSplitOptions.RemoveEmptyEntries);

        string basePart;
        string extensionPart;

        if (parts.Length == 0) {
            throw new ArgumentException("Name does not contain valid DOS characters.", nameof(name));
        }

        if (parts.Length == 1) {
            basePart = parts[0];
            extensionPart = string.Empty;
        } else {
            extensionPart = parts[parts.Length - 1];
            int extensionStartIndex = trimmedName.LastIndexOf('.');
            basePart = trimmedName.Substring(0, extensionStartIndex);
        }

        basePart = NormalizePart(basePart, 8);
        extensionPart = NormalizePart(extensionPart, 3);

        if (string.IsNullOrEmpty(extensionPart)) {
            return basePart;
        }

        return basePart + "." + extensionPart;
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> is already DOS 8.3 compatible.
    /// </summary>
    /// <param name="name">Name to validate.</param>
    /// <returns>True when DOS-compatible.</returns>
    public static bool IsDosCompatible(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        try {
            string converted = Convert(name);
            return string.Equals(converted, name.ToUpperInvariant(), StringComparison.Ordinal);
        } catch (ArgumentException) {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the provided character is allowed in DOS 8.3 names.
    /// </summary>
    /// <param name="character">Character to validate.</param>
    /// <returns>True when character is valid.</returns>
    public static bool IsAllowedDosCharacter(char character) {
        if (character < 0x20 || character > 0x7E) {
            return false;
        }

        for (int i = 0; i < InvalidCharacters.Length; i++) {
            if (InvalidCharacters[i] == character) {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePart(string value, int maxLength) {
        string uppercaseValue = value.ToUpperInvariant();
        if (uppercaseValue.Length > maxLength) {
            uppercaseValue = uppercaseValue.Substring(0, maxLength);
        }

        for (int i = 0; i < uppercaseValue.Length; i++) {
            if (!IsAllowedDosCharacter(uppercaseValue[i])) {
                throw new ArgumentException("Name contains characters invalid for DOS 8.3 format.", nameof(value));
            }
        }

        return uppercaseValue;
    }
}
