namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[Loop("CX")]
public partial class Loop16;
[Loop("ECX")]
public partial class Loop32;

[Loop("CX", "helper.State.ZeroFlag")]
public partial class Loopz16;
[Loop("ECX", "helper.State.ZeroFlag")]
public partial class Loopz32;

[Loop("CX", "!helper.State.ZeroFlag")]
public partial class Loopnz16;
[Loop("ECX", "!helper.State.ZeroFlag")]
public partial class Loopnz32;