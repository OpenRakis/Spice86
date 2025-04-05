namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

// TEST
[Grp3TestRmImm(8, "byte")]
public partial class Grp3TestRmImm8;

[Grp3TestRmImm(16, "ushort")]
public partial class Grp3TestRmImm16;

[Grp3TestRmImm(32, "uint")]
public partial class Grp3TestRmImm32;

// NOT
[Grp3NotRm(8, "byte")]
public partial class Grp3NotRm8;

[Grp3NotRm(16, "ushort")]
public partial class Grp3NotRm16;

[Grp3NotRm(32, "uint")]
public partial class Grp3NotRm32;

// NEG
[Grp3NegRm(8, "byte")]
public partial class Grp3NegRm8;

[Grp3NegRm(16, "ushort")]
public partial class Grp3NegRm16;

[Grp3NegRm(32, "uint")]
public partial class Grp3NegRm32;

// MUL
[Grp3MulRmAcc(8, "byte", "byte", "ushort", "AL", "AH", "Mul")]
public partial class Grp3MulRmAcc8;

[Grp3MulRmAcc(16, "ushort", "ushort", "uint", "AX", "DX", "Mul")]
public partial class Grp3MulRmAcc16;

[Grp3MulRmAcc(32, "uint", "uint", "ulong", "EAX", "EDX", "Mul")]
public partial class Grp3MulRmAcc32;

// IMUL
[Grp3MulRmAcc(8, "byte", "sbyte", "short", "AL", "AH", "Imul")]
public partial class Grp3ImulRmAcc8;

[Grp3MulRmAcc(16, "ushort", "short", "int", "AX", "DX", "Imul")]
public partial class Grp3ImulRmAcc16;

[Grp3MulRmAcc(32, "uint", "int", "long", "EAX", "EDX", "Imul")]
public partial class Grp3ImulRmAcc32;

// DIV
[Grp3DivRmAcc(8, "byte", "byte", "ushort", "ushort", true, "AL", "AH", "Div")]
public partial class Grp3DivRmAcc8;

[Grp3DivRmAcc(16, "ushort", "ushort", "uint", "uint", false, "AX", "DX", "Div")]
public partial class Grp3DivRmAcc16;

[Grp3DivRmAcc(32, "uint", "uint", "ulong", "ulong", false, "EAX", "EDX", "Div")]
public partial class Grp3DivRmAcc32;

// IDIV
[Grp3DivRmAcc(8, "byte", "sbyte", "ushort", "short", true, "AL", "AH", "Idiv")]
public partial class Grp3IdivRmAcc8;

[Grp3DivRmAcc(16, "ushort", "short", "uint", "int", false, "AX", "DX", "Idiv")]
public partial class Grp3IdivRmAcc16;

[Grp3DivRmAcc(32, "uint", "int", "ulong", "long", false, "EAX", "EDX", "Idiv")]
public partial class Grp3IdivRmAcc32;