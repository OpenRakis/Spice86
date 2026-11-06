namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Xunit;

/// <summary>
/// A typed service call assigns the helper's typed return value straight into the contract's
/// output lvalues (<c>(AX, CarryFlag) = Dos.OpenFileOrDevice(...)</c>, register-local promotion
/// permitting). <see cref="ServiceContractEffects"/> must-writes, on the stub path, every
/// register the helper's parameters carry plus the selector and the result targets: the helper
/// stores its inputs into <c>State</c> before running the stub and the call assigns its return
/// to the outputs. The result targets also join the reads: the contract only may-writes the
/// outputs, so a stub path that preserves one reads its pre-call value. A direct-path call
/// stores no input or selector at all, so only its result targets are must-written.
/// </summary>
public sealed class IrInterruptServiceCallsTest {
    private static InterruptServiceEntry Entry(int selectorValue, string name, CpuValues reads,
        CpuValues writes, ServiceExecutionPath path) =>
        new(selectorValue, name,
            new ServiceContract(new CallEffect(reads, writes), ServiceMemoryEffect.None, false),
            ServiceStringArgument.None, path);

    /// <summary>The typed call's effects must-write every register the helper's parameters carry
    /// plus the selector: the helper stores them all into <c>State</c> before the stub runs.
    /// Parameter registers are not contract reads of the call itself (they arrive as
    /// arguments); the frame reads stay.</summary>
    [Fact]
    public void EffectsMustWriteTheParametersAndSelector() {
        // Arrange: reads Ah|Al|Dx|Ds|Ss|Sp - Al/Dx/Ds are parameters, Ah the selector.
        InterruptServiceEntry entry = Entry(0x3D, "OpenFileOrDevice",
            CpuValues.Ah | CpuValues.Al | CpuValues.Dx | CpuValues.Ds
            | CpuValues.Ss | CpuValues.Sp,
            CpuValues.Ax | CpuValues.Carry, ServiceExecutionPath.Stub);
        InterruptServiceCallNode call = new(0x21, ServiceSelector.Ah, entry, "Dos", []);

        // Act
        IrEffects effects = ServiceContractEffects.Of(call);

        // Assert
        effects.MustWrite.Should().Be(
            CpuValues.Al | CpuValues.Dx | CpuValues.Ds | CpuValues.Ah,
            "the helper writes every passed register and the selector into State");
        effects.Reads.Should().Be(
            CpuValues.Ss | CpuValues.Sp | CpuValues.Cs
            | CpuValues.StatusFlags | CpuValues.Direction,
            "the contract reads beyond the parameters are only the frame registers");
        effects.MayWrite.Should().Be(
            CpuValues.Ax | CpuValues.Dx | CpuValues.Ds | CpuValues.Carry,
            "may-write holds the contract writes plus the must-written inputs "
            + "(IrEffects folds mustWrite into mayWrite; Ah|Al are inside Ax)");
    }

    /// <summary>A may-written output the stub can preserve is read by the call: the caller-side
    /// assignment writes every result target, but the handler itself only promised a may-write,
    /// so the preserved path feeds on the pre-call value. <c>SetHandleCount</c> reads
    /// <c>Ah|Bx|Ss|Sp</c> and writes <c>Ax|Carry</c> with results AX and CarryFlag: the reads
    /// keep AL (AH is supplied as the selector) plus the frame, and the must-writes hold
    /// AX, BX and Carry.</summary>
    [Fact]
    public void EffectsReadThePreservedOutputsTheCallMustWrite() {
        // Arrange
        InterruptServiceEntry entry = Entry(0x67, "SetHandleCount",
            CpuValues.Ah | CpuValues.Bx | CpuValues.Ss | CpuValues.Sp,
            CpuValues.Ax | CpuValues.Carry, ServiceExecutionPath.Stub);
        InterruptServiceCallNode call = new(0x21, ServiceSelector.Ah, entry, "Dos", []) {
            Results = [new RegisterNode(DataType.UINT16, 0), new CpuFlagNode(Flags.Carry)]
        };

        // Act
        IrEffects effects = ServiceContractEffects.Of(call);

        // Assert
        effects.MustWrite.Should().Be(
            CpuValues.Bx | CpuValues.Ah | CpuValues.Ax | CpuValues.Carry,
            "the helper stores the BX input and the selector and the call assigns both outputs");
        (effects.Reads & CpuValues.Al).Should().Be(CpuValues.Al,
            "a preserved output reads its pre-call value; AH is supplied, so only AL remains");
        (effects.Reads & CpuValues.Ah).Should().Be(CpuValues.None,
            "the selector's bytes are supplied, not read");
        effects.MayWrite.Should().Be(
            CpuValues.Ax | CpuValues.Bx | CpuValues.Carry,
            "the contract writes plus the folded must-written inputs (Ah is inside Ax)");
    }

    /// <summary>A direct-path call stores no input register or selector: the helper invokes the
    /// service implementation with its parameters as C# arguments, so only the result targets
    /// are must-written. The preserved-output read stays (the contract's write is still a
    /// may-write), and the frame reads stay.</summary>
    [Fact]
    public void DirectEffectsWriteOnlyTheResultTargets() {
        // Arrange: reads Ah|Al|Dx|Ss|Sp, writes Ah|Carry - like int 17h WriteCharacter.
        InterruptServiceEntry entry = Entry(0x00, "WriteCharacter",
            CpuValues.Ah | CpuValues.Al | CpuValues.Dx | CpuValues.Ss | CpuValues.Sp,
            CpuValues.Ah | CpuValues.Carry,
            ServiceExecutionPath.Direct);
        InterruptServiceCallNode call = new(0x17, ServiceSelector.Ah, entry, "Printer", []) {
            Results = [new RegisterNode(DataType.UINT8, 4), new CpuFlagNode(Flags.Carry)]
        };

        // Act
        IrEffects effects = ServiceContractEffects.Of(call);

        // Assert
        effects.MustWrite.Should().Be(
            CpuValues.Ah | CpuValues.Carry,
            "a direct call stores no input or selector; only the result targets are written");
        effects.Reads.Should().Be(
            CpuValues.Ss | CpuValues.Sp | CpuValues.Cs
            | CpuValues.StatusFlags | CpuValues.Direction,
            "the parameters and the selector are not stored or read; only the frame reads stay");
        effects.MayWrite.Should().Be(
            CpuValues.Ah | CpuValues.Carry,
            "the contract writes plus the folded result targets");
    }
}
