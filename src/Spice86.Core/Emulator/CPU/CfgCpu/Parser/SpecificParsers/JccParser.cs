namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class JccParser : BaseInstructionParser {
    private readonly Jcc8SpecificParser _jcc8SpecificParser;
    private readonly Jcc16SpecificParser _jcc16SpecificParser;
    private readonly Jcc32SpecificParser _jcc32SpecificParser;
    public JccParser(BaseInstructionParser other) : base(other) {
        _jcc8SpecificParser = new(this);
        _jcc16SpecificParser = new(this);
        _jcc32SpecificParser = new(this);
    }

    public CfgInstruction Parse(ParsingContext context) {
        bool is8 = context.OpcodeField.Value <= 0xFF;
        BitWidth addressWidth = GetBitWidth(is8, context.AddressWidthFromPrefixes==BitWidth.DWORD_32);
        // Take the lowest 4 bits
        int condition = context.OpcodeField.Value & 0xF;
        return addressWidth switch {
            BitWidth.BYTE_8 => _jcc8SpecificParser.Parse(context, condition),
            BitWidth.WORD_16 => _jcc16SpecificParser.Parse(context, condition),
            BitWidth.DWORD_32 => _jcc32SpecificParser.Parse(context, condition)
        };
    }
}

[JccSpecificParser(8, "sbyte")]
public partial class Jcc8SpecificParser;
[JccSpecificParser(16, "short")]
public partial class Jcc16SpecificParser;
[JccSpecificParser(32, "int")]
public partial class Jcc32SpecificParser;
