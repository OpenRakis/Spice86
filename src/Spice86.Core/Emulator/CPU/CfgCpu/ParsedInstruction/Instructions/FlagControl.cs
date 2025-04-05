namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[FlagControl(FlagName:"CarryFlag", FlagValue:"!helper.State.CarryFlag", "CMC")]
public partial class Cmc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"false", "CLC")]
public partial class Clc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"true", "STC")]
public partial class Stc;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"false", "CLI")]
public partial class Cli;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"true", "STI")]
public partial class Sti;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"false", "CLD")]
public partial class Cld;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"true", "STD")]
public partial class Std;