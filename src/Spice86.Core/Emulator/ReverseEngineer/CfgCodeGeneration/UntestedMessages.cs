namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The generated <c>throw FailAsUntested(...)</c> statements, one per untested situation; each message keeps the
/// address because the runtime log is where it is read.
/// </summary>
internal static class UntestedMessages {
    /// <summary>A conditional jump arm whose target was never observed.</summary>
    public static FailAsUntestedNode Jump(SegmentedAddress at) => Plain($"Untested jump at {at}");

    /// <summary>A conditional fallthrough arm that was never observed.</summary>
    public static FailAsUntestedNode ConditionalFallthrough(SegmentedAddress at) => Plain($"Untested fallthrough at {at}");

    /// <summary>A fallthrough after an instruction that was never observed.</summary>
    public static FailAsUntestedNode FallthroughAfter(SegmentedAddress at) => Plain($"Untested fallthrough after {at}");

    /// <summary>An indirect jump or call with no observed targets.</summary>
    public static FailAsUntestedNode IndirectWithoutTargets(string targetKind, SegmentedAddress at) =>
        Plain($"Untested indirect {targetKind} at {at}");

    /// <summary>Formats an already normalized near target in an untested-target failure.</summary>
    public static FailAsUntestedNode NearTarget(string targetKind, ValueNode target, SegmentedAddress at) => new([
        UntestedMessagePart.Literal($"Untested near {targetKind} target 0x"), UntestedMessagePart.Hole(target, "X4"),
        UntestedMessagePart.Literal($" at {at}")]);

    /// <summary>A far jump or call with an unknown runtime target.</summary>
    public static FailAsUntestedNode FarTarget(string targetKind, ValueNode segment, ValueNode offset, SegmentedAddress at) => new([
        UntestedMessagePart.Literal($"Untested far {targetKind} target "), UntestedMessagePart.Hole(segment, "X4"),
        UntestedMessagePart.Literal(":"), UntestedMessagePart.Hole(offset, "X4"), UntestedMessagePart.Literal($" at {at}")]);

    /// <summary>A self-modifying code selector with no matching signature.</summary>
    public static FailAsUntestedNode CodeVariant(SegmentedAddress at) => Plain($"Untested code variant at {at}");

    /// <summary>A callback whose handler moved CS:IP to a target discovery never observed.</summary>
    public static FailAsUntestedNode CallbackTarget(SegmentedAddress at) => new([
        UntestedMessagePart.Literal("Untested callback target "),
        UntestedMessagePart.Hole(new SegmentRegisterNode((int)SegmentRegisterIndex.CsIndex), "X4"),
        UntestedMessagePart.Literal(":"), UntestedMessagePart.Hole(new InstructionPointerNode(), "X4"),
        UntestedMessagePart.Literal($" at {at}")]);

    /// <summary>A call that returned but no continuation was observed during discovery.</summary>
    public static FailAsUntestedNode ReturnFromCall(SegmentedAddress at) => Plain($"Untested return from call at {at}");

    /// <summary>A call landed on a redirected return target discovery never observed for it.</summary>
    public static FailAsUntestedNode RedirectedReturnTarget(SegmentedAddress at) => Plain($"Redirected return target not observed at {at}");

    /// <summary>An entry dispatch with an unknown load offset.</summary>
    public static FailAsUntestedNode EntryOffset() => new([
        UntestedMessagePart.Literal("Untested entry offset 0x"), UntestedMessagePart.Hole(new LoadOffsetParameterNode(), "X4")]);

    private static FailAsUntestedNode Plain(string message) => new([UntestedMessagePart.Literal(message)]);
}
