namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[FlagControl(FlagName:"CarryFlag", FlagValue:"!helper.State.CarryFlag", "cmc")]
public partial class Cmc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"false", "clc")]
public partial class Clc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"true", "stc")]
public partial class Stc;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"false", "cli")]
public partial class Cli;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"true", "sti")]
public partial class Sti;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"false", "cld")]
public partial class Cld;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"true", "std")]
public partial class Std;