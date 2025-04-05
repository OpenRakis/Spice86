namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[Loop("CX", null, "loop")]
public partial class Loop16;
[Loop("ECX", null, "loop")]
public partial class Loop32;

[Loop("CX", "helper.State.ZeroFlag", "loope")]
public partial class Loopz16;
[Loop("ECX", "helper.State.ZeroFlag", "loope")]
public partial class Loopz32;

[Loop("CX", "!helper.State.ZeroFlag", "loopne")]
public partial class Loopnz16;
[Loop("ECX", "!helper.State.ZeroFlag", "loopne")]
public partial class Loopnz32;