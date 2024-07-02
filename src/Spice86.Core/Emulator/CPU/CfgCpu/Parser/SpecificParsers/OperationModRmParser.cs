namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

[OperationModRmParser("MovRmReg", true)]
partial class MovRmRegParser;

[OperationModRmParser("MovRegRm", true)]
partial class MovRegRmParser;

[OperationModRmParser("MovRmSreg", false)]
partial class MovRmSregParser;

[OperationModRmParser("TestRmReg", true)]
partial class TestRmRegParser;

[OperationModRmParser("XchgRm", true)]
partial class XchgRmParser;

[OperationModRmParser("Les", false)]
partial class LesParser;
[OperationModRmParser("Lss", false)]
partial class LssParser;

[OperationModRmParser("Lds", false)]
partial class LdsParser;

[OperationModRmParser("Lfs", false)]
partial class LfsParser;

[OperationModRmParser("Lgs", false)]
partial class LgsParser;

[OperationModRmParser("Lea", false)]
partial class LeaParser;
[OperationModRmParser("PopRm", false)]
partial class PopRmParser;
[OperationModRmParser("ShldClRm", false)]
partial class ShldClRmParser;
[OperationModRmParser("ImulRm", false)]
partial class ImulRmParser;
[OperationModRmParser("MovRmZeroExtendByte", false)]
partial class MovRmZeroExtendByteParser;
[OperationModRmParser("MovRmSignExtendByte", false)]
partial class MovRmSignExtendByteParser;
