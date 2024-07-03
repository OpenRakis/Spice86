namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[Movs(8)]
public partial class Movs8;
[Movs(16)]
public partial class Movs16;
[Movs(32)]
public partial class Movs32;

[InsDx(8)]
public partial class InsDx8;
[InsDx(16)]
public partial class InsDx16;
[InsDx(32)]
public partial class InsDx32;

[OutsDx(8, "byte")]
public partial class OutsDx8;
[OutsDx(16, "ushort")]
public partial class OutsDx16;
[OutsDx(32, "uint")]
public partial class OutsDx32;

[Cmps(8)]
public partial class Cmps8;
[Cmps(16)]
public partial class Cmps16;
[Cmps(32)]
public partial class Cmps32;

[Stos(8, "AL")]
public partial class Stos8;
[Stos(16, "AX")]
public partial class Stos16;
[Stos(32, "EAX")]
public partial class Stos32;

[Lods(8, "AL")]
public partial class Lods8;
[Lods(16, "AX")]
public partial class Lods16;
[Lods(32, "EAX")]
public partial class Lods32;

[Scas(8, "AL")]
public partial class Scas8;
[Scas(16, "AX")]
public partial class Scas16;
[Scas(32, "EAX")]
public partial class Scas32;