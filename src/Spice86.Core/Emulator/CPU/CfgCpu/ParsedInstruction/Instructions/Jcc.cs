namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;


[JccNearImm(8, "sbyte", "state.OverflowFlag")]
public partial class Jo8;

[JccNearImm(8, "sbyte", "!state.OverflowFlag")]
public partial class Jno8;

[JccNearImm(8, "sbyte", "state.CarryFlag")]
public partial class Jb8;

[JccNearImm(8, "sbyte", "!state.CarryFlag")]
public partial class Jnb8;

[JccNearImm(8, "sbyte", "state.ZeroFlag")]
public partial class Jz8;

[JccNearImm(8, "sbyte", "!state.ZeroFlag")]
public partial class Jnz8;

[JccNearImm(8, "sbyte", "state.CarryFlag || state.ZeroFlag")]
public partial class Jbe8;

[JccNearImm(8, "sbyte", "!state.CarryFlag && !state.ZeroFlag")]
public partial class Ja8;

[JccNearImm(8, "sbyte", "state.SignFlag")]
public partial class Js8;

[JccNearImm(8, "sbyte", "!state.SignFlag")]
public partial class Jns8;

[JccNearImm(8, "sbyte", "state.ParityFlag")]
public partial class Jp8;

[JccNearImm(8, "sbyte", "!state.ParityFlag")]
public partial class Jpo8;

[JccNearImm(8, "sbyte", "state.SignFlag != state.OverflowFlag")]
public partial class Jl8;

[JccNearImm(8, "sbyte", "state.SignFlag == state.OverflowFlag")]
public partial class Jge8;

[JccNearImm(8, "sbyte", "state.ZeroFlag || state.SignFlag != state.OverflowFlag")]
public partial class Jng8;

[JccNearImm(8, "sbyte", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag")]
public partial class Jg8;

[JccNearImm(16, "short", "state.OverflowFlag")]
public partial class Jo16;

[JccNearImm(16, "short", "!state.OverflowFlag")]
public partial class Jno16;

[JccNearImm(16, "short", "state.CarryFlag")]
public partial class Jb16;

[JccNearImm(16, "short", "!state.CarryFlag")]
public partial class Jnb16;

[JccNearImm(16, "short", "state.ZeroFlag")]
public partial class Jz16;

[JccNearImm(16, "short", "!state.ZeroFlag")]
public partial class Jnz16;

[JccNearImm(16, "short", "state.CarryFlag || state.ZeroFlag")]
public partial class Jbe16;

[JccNearImm(16, "short", "!state.CarryFlag && !state.ZeroFlag")]
public partial class Ja16;

[JccNearImm(16, "short", "state.SignFlag")]
public partial class Js16;

[JccNearImm(16, "short", "!state.SignFlag")]
public partial class Jns16;

[JccNearImm(16, "short", "state.ParityFlag")]
public partial class Jp16;

[JccNearImm(16, "short", "!state.ParityFlag")]
public partial class Jpo16;

[JccNearImm(16, "short", "state.SignFlag != state.OverflowFlag")]
public partial class Jl16;

[JccNearImm(16, "short", "state.SignFlag == state.OverflowFlag")]
public partial class Jge16;

[JccNearImm(16, "short", "state.ZeroFlag || state.SignFlag != state.OverflowFlag")]
public partial class Jng16;

[JccNearImm(16, "short", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag")]
public partial class Jg16;

[JccNearImm(32, "int", "state.OverflowFlag")]
public partial class Jo32;

[JccNearImm(32, "int", "!state.OverflowFlag")]
public partial class Jno32;

[JccNearImm(32, "int", "state.CarryFlag")]
public partial class Jb32;

[JccNearImm(32, "int", "!state.CarryFlag")]
public partial class Jnb32;

[JccNearImm(32, "int", "state.ZeroFlag")]
public partial class Jz32;

[JccNearImm(32, "int", "!state.ZeroFlag")]
public partial class Jnz32;

[JccNearImm(32, "int", "state.CarryFlag || state.ZeroFlag")]
public partial class Jbe32;

[JccNearImm(32, "int", "!state.CarryFlag && !state.ZeroFlag")]
public partial class Ja32;

[JccNearImm(32, "int", "state.SignFlag")]
public partial class Js32;

[JccNearImm(32, "int", "!state.SignFlag")]
public partial class Jns32;

[JccNearImm(32, "int", "state.ParityFlag")]
public partial class Jp32;

[JccNearImm(32, "int", "!state.ParityFlag")]
public partial class Jpo32;

[JccNearImm(32, "int", "state.SignFlag != state.OverflowFlag")]
public partial class Jl32;

[JccNearImm(32, "int", "state.SignFlag == state.OverflowFlag")]
public partial class Jge32;

[JccNearImm(32, "int", "state.ZeroFlag || state.SignFlag != state.OverflowFlag")]
public partial class Jng32;

[JccNearImm(32, "int", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag")]
public partial class Jg32;
[JccNearImm(8, "sbyte", "state.CX == 0")]
public partial class Jcxz16;
// JCXZ32 still uses an 8bit offset
[JccNearImm(8, "sbyte", "state.ECX == 0")]
public partial class Jcxz32;