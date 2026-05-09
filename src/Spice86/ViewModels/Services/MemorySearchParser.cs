namespace Spice86.ViewModels.Services;

public static class MemorySearchParser {
    public static byte[]? ParseBinarySearchValue(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        string normalized = RemoveWhitespace(value);
        if (normalized.Length == 0) {
            return null;
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return ParseHexWithPrefix(normalized[2..]);
        }

        return ParseLiteralBinary(normalized);
    }

    private static byte[]? ParseHexWithPrefix(string hexPayload) {
        if (hexPayload.Length == 0 || hexPayload.Length % 2 != 0) {
            return null;
        }

        try {
            return Convert.FromHexString(hexPayload);
        } catch (FormatException) {
            return null;
        }
    }

    private static byte[]? ParseLiteralBinary(string payload) {
        foreach (char character in payload) {
            if (character != '0' && character != '1') {
                return null;
            }
        }

        if (payload.Length == 0 || payload.Length % 8 != 0) {
            return null;
        }

        int byteCount = payload.Length / 8;
        byte[] result = new byte[byteCount];
        for (int byteIndex = 0; byteIndex < byteCount; byteIndex++) {
            byte value = 0;
            for (int bitIndex = 0; bitIndex < 8; bitIndex++) {
                value <<= 1;
                if (payload[(byteIndex * 8) + bitIndex] == '1') {
                    value |= 1;
                }
            }
            result[byteIndex] = value;
        }

        return result;
    }

    private static string RemoveWhitespace(string value) {
        char[] buffer = new char[value.Length];
        int writeIndex = 0;
        foreach (char character in value) {
            if (!char.IsWhiteSpace(character)) {
                buffer[writeIndex] = character;
                writeIndex++;
            }
        }

        return new string(buffer, 0, writeIndex);
    }
}
