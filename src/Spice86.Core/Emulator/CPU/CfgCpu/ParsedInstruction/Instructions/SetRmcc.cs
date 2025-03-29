namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[SetRmcc("state.OverflowFlag", "o")]
public partial class SetRmo;

[SetRmcc("!state.OverflowFlag", "no")]
public partial class SetRmno;

[SetRmcc("state.CarryFlag", "b")]
public partial class SetRmb;

[SetRmcc("!state.CarryFlag", "ae")]
public partial class SetRmae;

[SetRmcc("state.ZeroFlag", "e")]
public partial class SetRme;

[SetRmcc("!state.ZeroFlag", "ne")]
public partial class SetRmne;

[SetRmcc("state.CarryFlag || state.ZeroFlag", "be")]
public partial class SetRmbe;

[SetRmcc("!state.CarryFlag && !state.ZeroFlag", "a")]
public partial class SetRma;

[SetRmcc("state.SignFlag", "s")]
public partial class SetRms;

[SetRmcc("!state.SignFlag", "ns")]
public partial class SetRmns;

[SetRmcc("state.ParityFlag", "p")]
public partial class SetRmp;

[SetRmcc("!state.ParityFlag", "np")]
public partial class SetRmnp;

[SetRmcc("state.SignFlag != state.OverflowFlag", "l")]
public partial class SetRml;

[SetRmcc("state.SignFlag == state.OverflowFlag", "ge")]
public partial class SetRmge;

[SetRmcc("state.ZeroFlag || state.SignFlag != state.OverflowFlag", "le")]
public partial class SetRmle;

[SetRmcc("!state.ZeroFlag && state.SignFlag == state.OverflowFlag", "g")]
public partial class SetRmg;