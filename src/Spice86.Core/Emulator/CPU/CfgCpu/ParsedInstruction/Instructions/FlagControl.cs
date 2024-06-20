namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[FlagControl(FlagName:"CarryFlag", FlagValue:"!helper.State.CarryFlag")]
public partial class Cmc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"false")]
public partial class Clc;
[FlagControl(FlagName:"CarryFlag", FlagValue:"true")]
public partial class Stc;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"false")]
public partial class Cli;
[FlagControl(FlagName:"InterruptFlag", FlagValue:"true")]
public partial class Sti;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"false")]
public partial class Cld;
[FlagControl(FlagName:"DirectionFlag", FlagValue:"true")]
public partial class Std;