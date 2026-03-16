namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class JccParser : BaseInstructionParser {
    private readonly Jcc8SpecificParser _jcc8SpecificParser;
    private readonly Jcc16SpecificParser _jcc16SpecificParser;
    private readonly Jcc32SpecificParser _jcc32SpecificParser;

    public JccParser(BaseInstructionParser other) : base(other) {
        _jcc8SpecificParser = new Jcc8SpecificParser(this);
        _jcc16SpecificParser = new Jcc16SpecificParser(this);
        _jcc32SpecificParser = new Jcc32SpecificParser(this);
    }

    public CfgInstruction Parse(ParsingContext context) {
        bool is8 = context.OpcodeField.Value <= 0xFF;
        // For near Jcc (0F 80..0F 8F), the displacement width is selected by operand-size (66h),
        // not address-size (67h). Short Jcc (70..7F) always uses 8-bit displacement.
        BitWidth width = GetBitWidth(is8, context.HasOperandSize32);
        // Take the lowest 4 bits
        int condition = context.OpcodeField.Value & 0xF;
        return width switch {
            BitWidth.BYTE_8 => _jcc8SpecificParser.Parse(context, condition),
            BitWidth.WORD_16 => _jcc16SpecificParser.Parse(context, condition),
            BitWidth.DWORD_32 => _jcc32SpecificParser.Parse(context, condition),
            _ => throw CreateUnsupportedBitWidthException(width)
        };
    }
}

[JccSpecificParser(8, "sbyte")]
public partial class Jcc8SpecificParser;

[JccSpecificParser(16, "short")]
public partial class Jcc16SpecificParser;

[JccSpecificParser(32, "int")]
public partial class Jcc32SpecificParser;