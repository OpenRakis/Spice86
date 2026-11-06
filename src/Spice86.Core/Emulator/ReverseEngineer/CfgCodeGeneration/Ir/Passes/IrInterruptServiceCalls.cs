namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// Turns an <c>int</c> site whose observed targets are all the installed stub of its vector
/// (<see cref="InterruptSiteKind.BuiltInNotAnalysed"/>) into a typed service call when the
/// selector register is a known constant with a precise contract: the site prints
/// <c>Group.Method(cs, ip, ...)</c> instead of <c>InterruptCall(cs, ip, vector)</c> and takes the
/// contract's effects, so values stay live across it and only the writes the service makes die.
/// Runs right after <see cref="IrConstants"/>: it reads the constant facts over the first SSA
/// build and keeps that SSA consistent by replacing every changed statement site.
/// <para>Legality, in the effects model (<c>CpuValues</c>, <c>IrTokens</c>, <c>MayFault</c>,
/// <c>DeliversInterrupts</c>; <c>IrSsa.SameVersions</c> for the moves it implies):</para>
/// <list type="bullet">
/// <item>IS-1: the site is typed only when every observed target is the vector's installed stub;
/// any other target keeps the untyped call (a program handler or hook the helper's vector check
/// would reject at runtime).</item>
/// <item>IS-2: the selector register's bytes must be known (not undetermined) at the site;
/// otherwise the call keeps <c>InterruptCall</c> as <see cref="InterruptSiteKind.SelectorUnknown"/>.</item>
/// <item>IS-3: the selector value must have a precise contract with a typed helper group; an
/// <c>Unknown</c> contract or a missing entry keeps <c>InterruptCall</c> as
/// <see cref="InterruptSiteKind.NoContract"/>.</item>
/// <item>IS-4: the typed call's effects are the contract's: reads are the arguments plus
/// <c>Reads</c> minus the supplied registers, plus the result targets' current values (the
/// contract only may-writes them, so a path preserving an output reads the pre-call value), SS,
/// SP and every flag (the frame push and <c>iret</c> cover them). On <c>Stub</c>, must-write
/// includes the supplied registers and the result targets. On <c>Direct</c>, only result
/// targets are must-written; may-write is <c>Writes</c>; tokens are Memory
/// always and Io when the service does I/O; <c>DeliversInterrupts</c> is not set: the
/// <c>int</c> frame clears IF and no precise service sets it again (the only services that do,
/// int 21h 4Bh and the int 74h mouse call, are <c>Unknown</c>), so no hardware handler runs
/// inside a typed call and its registers are not part of the effect; delivery after the call
/// is the separate event-check statement.</item>
/// <item>IS-5: on the stub path the parameter registers and the selector are in must-write,
/// never in reads: the helper stores every argument it is passed (and the selector constant)
/// into <c>State</c> before running the stub, so a write the program made to them before the
/// call is dead. On the direct path the helper invokes the core without storing them, so only
/// the result targets are must-written.</item>
/// </list>
/// </summary>
internal static class IrInterruptServiceCalls {
    /// <summary>Replaces every analysable <c>int</c> statement of <paramref name="method"/> by its
    /// typed service call or by the same call with its refined kind; returns the number of
    /// statements that became typed calls. The SSA sites of the changed statements are replaced
    /// too: a typed call's effects are narrower than the untyped one's, so verification never sees
    /// a stale site even when no statement became typed.</summary>
    public static int Apply(IrMethod method, IrSsa ssa, IrConstantFacts facts) {
        int typed = 0;
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (statement.Node is not InterruptCallHelperNode {
                        Kind: InterruptSiteKind.BuiltInNotAnalysed, Service: { } service
                    } call) {
                    continue;
                }
                IVisitableAstNode replacement = Classify(call, service, block, index, facts);
                if (ReferenceEquals(replacement, call)) {
                    continue;
                }
                IrStatement typedStatement = IrStatement.Create(replacement, statement.Origin);
                block.Statements[index] = typedStatement;
                if (ssa.HasSite(statement)) {
                    ssa.Replace(statement, typedStatement);
                }
                if (replacement is InterruptServiceCallNode) {
                    typed++;
                }
            }
        }
        return typed;
    }

    private static IVisitableAstNode Classify(InterruptCallHelperNode call,
        InterruptServiceVector service, IrBlock block, int index, IrConstantFacts facts) {
        InterruptServiceEntry? entry;
        ServiceSelector? selector = null;
        int? selectorValue = null;
        if (service.Services is HandlerServices.SingleService single) {
            entry = single.Service;
        } else {
            HandlerServices.SelectedServices selected =
                (HandlerServices.SelectedServices)service.Services;
            if (!TrySelectorValue(selected.Selector, block, index, facts, out int value)) {
                return new InterruptCallHelperNode(InterruptSiteKind.SelectorUnknown, service, null,
                    [.. call.Arguments]);
            }
            selector = selected.Selector;
            selectorValue = value;
            entry = selected.Services.GetValueOrDefault(value);
        }
        if (entry is null
            || ReferenceEquals(entry.Contract, ServiceContract.Unknown)
            || !InterruptServiceGroups.TryGetPropertyName(service.HandlerType,
                out string? groupPropertyName)) {
            return new InterruptCallHelperNode(InterruptSiteKind.NoContract, service,
                selectorValue, [.. call.Arguments]);
        }
        return new InterruptServiceCallNode(service.VectorNumber, selector, entry,
            groupPropertyName, Arguments(call, selector, entry.Contract)) {
            Results = [.. ServiceCallSignature.Outputs(entry.Contract)
                .Select(output => IrValueSets.NodeOf(output.Mask))]
        };
    }

    private static bool TrySelectorValue(ServiceSelector selector, IrBlock block, int index,
        IrConstantFacts facts, out int value) {
        value = 0;
        RegisterNode register = (RegisterNode)IrValueSets.NodeOf(selector.ToCpuValues());
        if (!facts.TryGetBefore(block, index, register, out ulong registerValue)) {
            return false;
        }
        value = (int)registerValue;
        return true;
    }

    private static IVisitableAstNode[] Arguments(InterruptCallHelperNode call,
        ServiceSelector? selector, ServiceContract contract) {
        List<IVisitableAstNode> arguments = [call.Arguments[0], call.Arguments[1]];
        foreach (TypedParameter parameter in ServiceCallSignature.Parameters(contract,
            selector)) {
            arguments.Add(ArgumentNode(parameter));
        }
        return [.. arguments];
    }

    private static IVisitableAstNode ArgumentNode(TypedParameter parameter) =>
        IrValueSets.NodeOf(parameter.Mask);
}
