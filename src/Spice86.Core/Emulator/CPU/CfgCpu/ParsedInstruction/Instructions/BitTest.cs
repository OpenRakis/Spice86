namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

[BitTestRm(16, "BT", "None")]
public partial class BtRm16;

[BitTestRm(32, "BT", "None")]
public partial class BtRm32;

[BitTestRm(16, "BTS", "Set")]
public partial class BtsRm16;

[BitTestRm(32, "BTS", "Set")]
public partial class BtsRm32;

[BitTestRm(16, "BTR", "Reset")]
public partial class BtrRm16;

[BitTestRm(32, "BTR", "Reset")]
public partial class BtrRm32;

[BitTestRm(16, "BTC", "Toggle")]
public partial class BtcRm16;

[BitTestRm(32, "BTC", "Toggle")]
public partial class BtcRm32;

[BitTestRmImm(16, "BT", "None")]
public partial class BtRmImm16;

[BitTestRmImm(32, "BT", "None")]
public partial class BtRmImm32;

[BitTestRmImm(16, "BTS", "Set")]
public partial class BtsRmImm16;

[BitTestRmImm(32, "BTS", "Set")]
public partial class BtsRmImm32;

[BitTestRmImm(16, "BTR", "Reset")]
public partial class BtrRmImm16;

[BitTestRmImm(32, "BTR", "Reset")]
public partial class BtrRmImm32;

[BitTestRmImm(16, "BTC", "Toggle")]
public partial class BtcRmImm16;

[BitTestRmImm(32, "BTC", "Toggle")]
public partial class BtcRmImm32;

[BsfRm(16, "BSF")]
public partial class BsfRm16;

[BsfRm(32, "BSF")]
public partial class BsfRm32;

[BsrRm(16, "BSR")]
public partial class BsrRm16;

[BsrRm(32, "BSR")]
public partial class BsrRm32;