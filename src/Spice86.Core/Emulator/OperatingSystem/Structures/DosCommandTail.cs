namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.ComponentModel.DataAnnotations;
using System.Text;

public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public byte Length {
        get => UInt8[0x0];
    }

    public static string PrepareCommandlineString(string? arguments) {

        if (string.IsNullOrWhiteSpace(arguments)) {
            return "";
        }

        string ag = arguments;

        // there needs to be a blank as first char in parameter string, if there isn't already
        if (ag[0] != ' ') {
            ag = ' ' + ag;
        }

        // Cut strings longer than 126 characters.
        ag = ag.Length > DosCommandTail.MaxCharacterLength ? ag[..DosCommandTail.MaxCharacterLength] : ag;

        // stripping trailing whitespaces
        ag = ag.TrimEnd(' ');

        CheckParameterString(ag);

        return ag;
    }

    public static void CheckParameterString(string value) {
        if (value.Length > 0 && value[0] != ' ') {
            throw new ArgumentException("Command line must start with a space character (DOS PSP requirement).");
        }

        if (value.Length > DosCommandTail.MaxCharacterLength) {
            throw new ArgumentException($"Command length cannot exceed {DosCommandTail.MaxCharacterLength} characters.");
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
        get {
            int length = UInt8[0x0];
            byte[] buffer = new byte[length];
            for (int i = 0; i < length; i++) {
                buffer[i] = UInt8[(uint)(1 + i)];
            }
            return Encoding.ASCII.GetString(buffer);
        }
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
    public const int MaxCharacterLength = MaxSize - 2; // length-byte + 126 chars max + \r

    public const int OffsetInPspSegment = 0x80;
}
