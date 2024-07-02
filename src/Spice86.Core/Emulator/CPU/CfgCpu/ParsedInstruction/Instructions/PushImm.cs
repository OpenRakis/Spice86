namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[PushImm(16, "ushort")]
public partial class PushImm16;
[PushImm(32, "uint")]
public partial class PushImm32;

[PushImm8SignExtended(16, "short", "ushort")]
public partial class PushImm8SignExtended16;
[PushImm8SignExtended(32, "int", "uint")]
public partial class PushImm8SignExtended32;