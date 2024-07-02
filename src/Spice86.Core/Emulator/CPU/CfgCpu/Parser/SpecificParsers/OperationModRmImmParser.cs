namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

[OperationModRmImmParser(Operation: "ShldImm8Rm", Has8: false, IsOnlyField8: true, IsUnsignedField: true)]
public partial class ShldImm8RmParser;

[OperationModRmImmParser(Operation: "MovRmImm", Has8: true, IsOnlyField8: false, IsUnsignedField: true)]
public partial class MovRmImmParser;

[OperationModRmImmParser(Operation: "ImulImmRm", Has8: false, IsOnlyField8: false, IsUnsignedField: false)]
public partial class ImulImmRmParser;
[OperationModRmImmParser(Operation: "ImulImm8Rm", Has8: false, IsOnlyField8: true, IsUnsignedField: false)]
public partial class ImulImm8RmParser;