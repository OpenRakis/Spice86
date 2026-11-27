namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>A generated <c>Callback</c> helper invocation.</summary>
internal sealed record CallbackHelperNode : MethodCallNode {
    /// <summary>Creates the helper invocation.</summary>
    /// <param name="contract">The callback's service contract, or <c>null</c> when unknown.</param>
    /// <param name="arguments">The arguments to pass to the method.</param>
    public CallbackHelperNode(ServiceContract? contract, params IVisitableAstNode[] arguments)
        : base(null, HelperCallNames.CallbackMethodName, arguments) {
        Contract = contract;
    }

    /// <summary>The callback's service contract, or <c>null</c> when unknown.</summary>
    public ServiceContract? Contract { get; }

    /// <inheritdoc />
    public override MethodCallNode WithArguments(IVisitableAstNode[] arguments) =>
        new CallbackHelperNode(Contract, arguments);
}
