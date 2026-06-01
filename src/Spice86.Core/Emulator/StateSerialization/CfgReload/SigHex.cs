namespace Spice86.Core.Emulator.StateSerialization.CfgReload;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Single authoritative definition of the <c>sigHex</c> codec: the on-disk byte image of a CFG node's
/// signature, encoded as concatenated upper-case hex pairs with <see cref="NullByteSentinel"/> standing
/// in for a modified-immediate (null) byte. Both the CFG reload artifact and the human/LLM block dump
/// use this format, so encode and decode live here to keep the format defined in one place.
/// </summary>
internal static class SigHex {
    /// <summary>Two-character sentinel marking a null (modified-immediate / live-read) signature byte.</summary>
    public const string NullByteSentinel = "__";

    private const string ByteFormat = "X2";

    /// <summary>
    /// Encodes a signature byte list. A null byte becomes <see cref="NullByteSentinel"/>; a present byte
    /// becomes its two-digit upper-case hex value.
    /// </summary>
    public static string Encode(IEnumerable<byte?> signatureBytes) {
        return string.Concat(signatureBytes.Select(EncodeByte));
    }

    /// <summary>
    /// Decodes a sigHex string into a nullable byte array. <see cref="NullByteSentinel"/> decodes to
    /// <c>null</c>; every other pair decodes to a byte.
    /// </summary>
    public static byte?[] Decode(string sigHex) {
        if (sigHex.Length % 2 != 0) {
            throw new InvalidOperationException($"Malformed sigHex '{sigHex}': odd length");
        }
        byte?[] result = new byte?[sigHex.Length / 2];
        for (int i = 0; i < result.Length; i++) {
            result[i] = DecodePair(sigHex.Substring(i * 2, 2));
        }
        return result;
    }

    private static string EncodeByte(byte? value) {
        if (value.HasValue) {
            return value.Value.ToString(ByteFormat);
        }
        return NullByteSentinel;
    }

    private static byte? DecodePair(string pair) {
        if (pair == NullByteSentinel) {
            return null;
        }
        return byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
