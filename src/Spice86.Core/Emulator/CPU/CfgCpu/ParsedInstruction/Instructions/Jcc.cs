namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;


[JccNearImm(8, "sbyte", "state.OverflowFlag", "jo")]
public partial class Jo8;

[JccNearImm(8, "sbyte", "!state.OverflowFlag", "jno")]
public partial class Jno8;

[JccNearImm(8, "sbyte", "state.CarryFlag", "jb")]
public partial class Jb8;

[JccNearImm(8, "sbyte", "!state.CarryFlag", "jae")]
public partial class Jae8;

[JccNearImm(8, "sbyte", "state.ZeroFlag", "je")]
public partial class Je8;

[JccNearImm(8, "sbyte", "!state.ZeroFlag", "jne")]
public partial class Jne8;

[JccNearImm(8, "sbyte", "state.CarryFlag || state.ZeroFlag", "jbe")]
public partial class Jbe8;

[JccNearImm(8, "sbyte", "!state.CarryFlag && !state.ZeroFlag", "ja")]
public partial class Ja8;

[JccNearImm(8, "sbyte", "state.SignFlag", "js")]
public partial class Js8;

[JccNearImm(8, "sbyte", "!state.SignFlag", "jns")]
public partial class Jns8;

[JccNearImm(8, "sbyte", "state.ParityFlag", "jp")]
public partial class Jp8;

[JccNearImm(8, "sbyte", "!state.ParityFlag", "jnp")]
public partial class Jnp8;

[JccNearImm(8, "sbyte", "state.SignFlag != state.OverflowFlag", "jl")]
public partial class Jl8;

[JccNearImm(8, "sbyte", "state.SignFlag == state.OverflowFlag", "jge")]
public partial class Jge8;

[JccNearImm(8, "sbyte", "state.ZeroFlag || state.SignFlag != state.OverflowFlag", "jle")]
public partial class Jle8;

[JccNearImm(8, "sbyte", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag", "jg")]
public partial class Jg8;

[JccNearImm(16, "short", "state.OverflowFlag", "jo")]
public partial class Jo16;

[JccNearImm(16, "short", "!state.OverflowFlag", "jno")]
public partial class Jno16;

[JccNearImm(16, "short", "state.CarryFlag", "jb")]
public partial class Jb16;

[JccNearImm(16, "short", "!state.CarryFlag", "jae")]
public partial class Jae16;

[JccNearImm(16, "short", "state.ZeroFlag", "je")]
public partial class Je16;

[JccNearImm(16, "short", "!state.ZeroFlag", "jne")]
public partial class Jne16;

[JccNearImm(16, "short", "state.CarryFlag || state.ZeroFlag", "jbe")]
public partial class Jbe16;

[JccNearImm(16, "short", "!state.CarryFlag && !state.ZeroFlag", "ja")]
public partial class Ja16;

[JccNearImm(16, "short", "state.SignFlag", "js")]
public partial class Js16;

[JccNearImm(16, "short", "!state.SignFlag", "jns")]
public partial class Jns16;

[JccNearImm(16, "short", "state.ParityFlag", "jp")]
public partial class Jp16;

[JccNearImm(16, "short", "!state.ParityFlag", "jnp")]
public partial class Jnp16;

[JccNearImm(16, "short", "state.SignFlag != state.OverflowFlag", "jl")]
public partial class Jl16;

[JccNearImm(16, "short", "state.SignFlag == state.OverflowFlag", "jge")]
public partial class Jge16;

[JccNearImm(16, "short", "state.ZeroFlag || state.SignFlag != state.OverflowFlag", "jle")]
public partial class Jle16;

[JccNearImm(16, "short", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag", "jg")]
public partial class Jg16;

[JccNearImm(32, "int", "state.OverflowFlag", "jo")]
public partial class Jo32;

[JccNearImm(32, "int", "!state.OverflowFlag", "jno")]
public partial class Jno32;

[JccNearImm(32, "int", "state.CarryFlag", "jb")]
public partial class Jb32;

[JccNearImm(32, "int", "!state.CarryFlag", "jae")]
public partial class Jae32;

[JccNearImm(32, "int", "state.ZeroFlag", "je")]
public partial class Je32;

[JccNearImm(32, "int", "!state.ZeroFlag", "jne")]
public partial class Jne32;

[JccNearImm(32, "int", "state.CarryFlag || state.ZeroFlag", "jbe")]
public partial class Jbe32;

[JccNearImm(32, "int", "!state.CarryFlag && !state.ZeroFlag", "ja")]
public partial class Ja32;

[JccNearImm(32, "int", "state.SignFlag", "js")]
public partial class Js32;

[JccNearImm(32, "int", "!state.SignFlag", "jns")]
public partial class Jns32;

[JccNearImm(32, "int", "state.ParityFlag", "jp")]
public partial class Jp32;

[JccNearImm(32, "int", "!state.ParityFlag", "jnp")]
public partial class Jnp32;

[JccNearImm(32, "int", "state.SignFlag != state.OverflowFlag", "jl")]
public partial class Jl32;

[JccNearImm(32, "int", "state.SignFlag == state.OverflowFlag", "jge")]
public partial class Jge32;

[JccNearImm(32, "int", "state.ZeroFlag || state.SignFlag != state.OverflowFlag", "jle")]
public partial class Jle32;

[JccNearImm(32, "int", "!state.ZeroFlag && state.SignFlag == state.OverflowFlag", "jg")]
public partial class Jg32;
[JccNearImm(8, "sbyte", "state.CX == 0", "jcxz")]
public partial class Jcxz16;
// JCXZ32 still uses an 8bit offset
[JccNearImm(8, "sbyte", "state.ECX == 0", "jcxz")]
public partial class Jcxz32;