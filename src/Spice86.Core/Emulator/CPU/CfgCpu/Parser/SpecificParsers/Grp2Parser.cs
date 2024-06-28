namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

public class Grp2Parser : BaseGrpOperationParser {
    public Grp2Parser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        ushort opcode = context.OpcodeField.Value;
        bool useImm = !BitIsTrue(opcode, 4);
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        if (useImm) {
            return GetOperationRmImmFactory(groupIndex).Parse(context, modRmContext, bitWidth);
        }
        bool useCl = BitIsTrue(opcode, 1);
        if (useCl) {
            return GetOperationClFactory(groupIndex).Parse(context, modRmContext, bitWidth);
        }

        return GetOperationOneFactory(groupIndex).Parse(context, modRmContext, bitWidth);
    }
    
    protected BaseOperationModRmFactory GetOperationOneFactory(int groupIndex) {
        return groupIndex switch {
            0 => new Grp2RolOneRmOperationFactory(this),
            1 => new Grp2RorOneRmOperationFactory(this),
            2 => new Grp2RclOneRmOperationFactory(this),
            3 => new Grp2RcrOneRmOperationFactory(this),
            4 => new Grp2ShlOneRmOperationFactory(this),
            5 => new Grp2ShrOneRmOperationFactory(this),
            7 => new Grp2SarOneRmOperationFactory(this),
        };
    }
    protected BaseOperationModRmFactory GetOperationClFactory(int groupIndex) {
        return groupIndex switch {
            0 => new Grp2RolClRmOperationFactory(this),
            1 => new Grp2RorClRmOperationFactory(this),
            2 => new Grp2RclClRmOperationFactory(this),
            3 => new Grp2RcrClRmOperationFactory(this),
            4 => new Grp2ShlClRmOperationFactory(this),
            5 => new Grp2ShrClRmOperationFactory(this),
            7 => new Grp2SarClRmOperationFactory(this),
        };
    }

    protected BaseOperationModRmFactory GetOperationRmImmFactory(int groupIndex) {
        return groupIndex switch {
            0 => new Grp2RolRmImmOperationFactory(this),
            1 => new Grp2RorRmImmOperationFactory(this),
            2 => new Grp2RclRmImmOperationFactory(this),
            3 => new Grp2RcrRmImmOperationFactory(this),
            4 => new Grp2ShlRmImmOperationFactory(this),
            5 => new Grp2ShrRmImmOperationFactory(this),
            7 => new Grp2SarRmImmOperationFactory(this)
        };
    }
}

[OperationModRmFactory("Grp2RolOneRm")]
public partial class Grp2RolOneRmOperationFactory;
[OperationModRmFactory("Grp2RorOneRm")]
public partial class Grp2RorOneRmOperationFactory;
[OperationModRmFactory("Grp2RclOneRm")]
public partial class Grp2RclOneRmOperationFactory;
[OperationModRmFactory("Grp2RcrOneRm")]
public partial class Grp2RcrOneRmOperationFactory;
[OperationModRmFactory("Grp2ShlOneRm")]
public partial class Grp2ShlOneRmOperationFactory;
[OperationModRmFactory("Grp2ShrOneRm")]
public partial class Grp2ShrOneRmOperationFactory;
[OperationModRmFactory("Grp2SarOneRm")]
public partial class Grp2SarOneRmOperationFactory;

[OperationModRmFactory("Grp2RolClRm")]
public partial class Grp2RolClRmOperationFactory;
[OperationModRmFactory("Grp2RorClRm")]
public partial class Grp2RorClRmOperationFactory;
[OperationModRmFactory("Grp2RclClRm")]
public partial class Grp2RclClRmOperationFactory;
[OperationModRmFactory("Grp2RcrClRm")]
public partial class Grp2RcrClRmOperationFactory;
[OperationModRmFactory("Grp2ShlClRm")]
public partial class Grp2ShlClRmOperationFactory;
[OperationModRmFactory("Grp2ShrClRm")]
public partial class Grp2ShrClRmOperationFactory;
[OperationModRmFactory("Grp2SarClRm")]
public partial class Grp2SarClRmOperationFactory;

[OperationModRmImmFactory("Grp2RolRmImm", true)]
public partial class Grp2RolRmImmOperationFactory;
[OperationModRmImmFactory("Grp2RorRmImm", true)]
public partial class Grp2RorRmImmOperationFactory;
[OperationModRmImmFactory("Grp2RclRmImm", true)]
public partial class Grp2RclRmImmOperationFactory;
[OperationModRmImmFactory("Grp2RcrRmImm", true)]
public partial class Grp2RcrRmImmOperationFactory;
[OperationModRmImmFactory("Grp2ShlRmImm", true)]
public partial class Grp2ShlRmImmOperationFactory;
[OperationModRmImmFactory("Grp2ShrRmImm", true)]
public partial class Grp2ShrRmImmOperationFactory;
[OperationModRmImmFactory("Grp2SarRmImm", true)]
public partial class Grp2SarRmImmOperationFactory;