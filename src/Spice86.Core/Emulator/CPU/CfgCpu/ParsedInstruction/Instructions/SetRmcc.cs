namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[SetRmcc("state.OverflowFlag")]
public partial class SetRmo;

[SetRmcc("!state.OverflowFlag")]
public partial class SetRmno;

[SetRmcc("state.CarryFlag")]
public partial class SetRmb;

[SetRmcc("!state.CarryFlag")]
public partial class SetRmnb;

[SetRmcc("state.ZeroFlag")]
public partial class SetRmz;

[SetRmcc("!state.ZeroFlag")]
public partial class SetRmnz;

[SetRmcc("state.CarryFlag || state.ZeroFlag")]
public partial class SetRmbe;

[SetRmcc("!state.CarryFlag && !state.ZeroFlag")]
public partial class SetRma;

[SetRmcc("state.SignFlag")]
public partial class SetRms;

[SetRmcc("!state.SignFlag")]
public partial class SetRmns;

[SetRmcc("state.ParityFlag")]
public partial class SetRmp;

[SetRmcc("!state.ParityFlag")]
public partial class SetRmpo;

[SetRmcc("state.SignFlag != state.OverflowFlag")]
public partial class SetRml;

[SetRmcc("state.SignFlag == state.OverflowFlag")]
public partial class SetRmge;

[SetRmcc("state.ZeroFlag || state.SignFlag != state.OverflowFlag")]
public partial class SetRmng;

[SetRmcc("!state.ZeroFlag && state.SignFlag == state.OverflowFlag")]
public partial class SetRmg;