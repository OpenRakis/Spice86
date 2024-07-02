namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

[OperationParser("Cbw", false)]
partial class CbwParser;

[OperationParser("Cwd", false)]
partial class CwdParser;
[OperationParser("PushF", false)]
partial class PushFParser;
[OperationParser("PopF", false)]
partial class PopFParser;
[OperationParser("InAccDx", true)]
partial class InAccDxParser;
[OperationParser("OutAccDx", true)]
partial class OutAccDxParser;
[OperationParser("Leave", false)]
partial class LeaveParser;
[OperationParser("Scas", true)]
partial class ScasParser;
[OperationParser("Stos", true)]
partial class StosParser;
[OperationParser("InsDx", true)]
partial class InsDxParser;
[OperationParser("Pusha", false)]
partial class PushaParser;
[OperationParser("Popa", false)]
partial class PopaParser;