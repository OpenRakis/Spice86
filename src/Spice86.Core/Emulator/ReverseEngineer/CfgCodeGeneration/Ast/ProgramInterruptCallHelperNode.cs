namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// A generated <c>InterruptCall</c> invocation to a handler of the generated program itself,
/// printed <c>InterruptCall(returnCs, 0xReturnIp, 0xVector, handlerCs, 0xHandlerIp, handler)</c>.
/// The helper checks the vector still points to the handler and fails as untested otherwise, so
/// the call always enters the handler its arguments name.
/// </summary>
internal sealed record ProgramInterruptCallHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="arguments">The arguments to pass to the method.</param>
    /// <param name="handler">The registers the handler reads at its entry version and may leave
    /// changed at a return, from the interrupt effect builder's summary of the target partition
    /// entry.</param>
    /// <param name="hardwareInterruptEffect">The program's hardware interrupt effect at lowering: a
    /// hardware interrupt delivered inside the handler may read its uses and may leave its writes
    /// changed.</param>
    public ProgramInterruptCallHelperNode(IVisitableAstNode[] arguments, CallEffect handler,
        CallEffect hardwareInterruptEffect)
        : base(null, HelperCallNames.InterruptCallMethodName, arguments) {
        Handler = handler;
        HardwareInterruptEffect = hardwareInterruptEffect;
    }

    /// <summary>The effect of the called handler.</summary>
    public CallEffect Handler { get; }

    /// <summary>The program's hardware interrupt effect at lowering.</summary>
    public CallEffect HardwareInterruptEffect { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new ProgramInterruptCallHelperNode(arguments, Handler, HardwareInterruptEffect);
}
