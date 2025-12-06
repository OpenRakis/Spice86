namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public byte Length {
        get => UInt8[0x0];
    }

    /// <summary>
    /// Converts the specified Spice86 command-line arguments string into the string-format used by DOS.
    /// </summary>
    /// <param name="arguments">The command-line arguments string.</param>
    /// <returns>The command-line arguments in the format used by DOS.</returns>
    public static string PrepareCommandlineString(string? arguments) {

        if (string.IsNullOrWhiteSpace(arguments)) {
            return "";
        }

        string ag = arguments;

        // there needs to be a blank as first char in parameter string, if there isn't already
        if (ag[0] != ' ') {
            ag = $" {ag}";
        }

        // Cut strings longer than 126 characters.
        ag = ag.Length > MaxCharacterLength ? ag[..MaxCharacterLength] : ag;

        // stripping trailing whitespaces
        ag = ag.TrimEnd(' ');

        CheckParameterString(ag);

        return ag;
    }

    public static void CheckParameterString(string value) {
        if (value.Length > 0 && value[0] != ' ') {
            throw new ArgumentException("Command line must start with a space character (DOS PSP requirement).");
        }

        if (value.Length > MaxCharacterLength) {
            throw new ArgumentException($"Command length cannot exceed {MaxCharacterLength} characters.");
        }

        if (value.Contains('\r')) {
            throw new ArgumentException("Command should not contain CR.");
        }
        if (value.Contains('\0')) {
            throw new ArgumentException("Command should not contain byte(0).");
        }
    }

    [Range(0, MaxCharacterLength)]
    public string Command {
        get => Encoding.ASCII.GetString(GetUInt8Array(1, Length).ToArray());
        set {
            CheckParameterString(value);

            // cleanup all
            Memset8(0, 0, MaxSize);

            UInt8[0x0] = (byte)value.Length;
            Span<byte> charBytes = Encoding.ASCII.GetBytes(value);
            for (int i = 0; i < charBytes.Length; i++) {
                byte character = charBytes[i];
                UInt8[1 + i] = character;
            }
            UInt8[1 + value.Length] = 0x0d;
        }
    }

    public const int MaxSize = 128;
    public const int MaxCharacterLength = MaxSize - 2; /// length-byte + 126 chars max + \r

    public const int OffsetInPspSegment = 0x80;
}
