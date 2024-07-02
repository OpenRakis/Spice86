namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
[ImulImmRm(Size: 16, RmSignedType:"short", RmUnsignedType:"ushort", ImmSignedType:"sbyte", ResSignedType: "int")]
public partial class ImulImm8Rm16;
[ImulImmRm(Size: 32, RmSignedType:"int", RmUnsignedType:"uint", ImmSignedType:"sbyte", ResSignedType: "long")]
public partial class ImulImm8Rm32;
[ImulImmRm(Size: 16, RmSignedType:"short", RmUnsignedType:"ushort", ImmSignedType:"short", ResSignedType: "int")]
public partial class ImulImmRm16;
[ImulImmRm(Size: 32, RmSignedType:"int", RmUnsignedType:"uint", ImmSignedType:"int", ResSignedType: "long")]
public partial class ImulImmRm32;
[ImulRm(Size: 16, RmSignedType:"short", RmUnsignedType:"ushort", ResSignedType: "int")]
public partial class ImulRm16;
[ImulRm(Size: 32, RmSignedType:"int", RmUnsignedType:"uint", ResSignedType: "long")]
public partial class ImulRm32;