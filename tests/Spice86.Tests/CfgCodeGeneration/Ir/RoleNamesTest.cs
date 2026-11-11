namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Reflection;

using Xunit;

/// <summary>
/// The G4.2 naming functions: the sink rule keeps only a name every sink agrees on, and a role
/// name beats the generic fallback.
/// </summary>
public sealed class RoleNamesTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);

    private static InterruptServiceCallNode ServiceCall(string implementationName,
        params IVisitableAstNode[] arguments) {
        MethodInfo implementation = typeof(DosInt21Handler).GetMethod(implementationName)
            ?? throw new InvalidOperationException($"{implementationName} is not a service method");
        InterruptServiceEntry service = new(0x48, implementationName,
            new ServiceContract(new CallEffect(CpuValues.Bx, CpuValues.Ax),
                ServiceMemoryEffect.ReadWrite, false),
            ServiceStringArgument.None, implementation, [CpuValues.Bx, CpuValues.Ax]);
        return new InterruptServiceCallNode(0x21, null, service, "DosInt21Handler", arguments) {
            ReturnSegment = new ConstantNode(DataType.UINT16, 0),
            ReturnOffset = new ConstantNode(DataType.UINT16, 3)
        };
    }

    [Fact]
    public void WebPassedToOneSink_GetsTheParameterName() {
        // Arrange: the same AX value is bound to ResizeHandleTable's previousAx only.
        InterruptServiceCallNode call = ServiceCall(nameof(DosInt21Handler.ResizeHandleTable),
            Bx, Ax);

        // Act
        HashSet<string> names = RoleNames.SinkNames([call], IrSsaValue.Eax, Ax, null, null);

        // Assert
        RoleNames.UniqueName(names).Should().Be("previousAx");
    }

    [Fact]
    public void WebPassedToTwoDifferentSinks_FallsBack() {
        // Arrange: AX binds to ResizeHandleTable's requestedSize and, at a second site, to its
        // previousAx - two different sink names for one value.
        InterruptServiceCallNode first = ServiceCall(nameof(DosInt21Handler.ResizeHandleTable),
            Ax, Bx);
        InterruptServiceCallNode second = ServiceCall(nameof(DosInt21Handler.ResizeHandleTable),
            Bx, Ax);

        // Act
        HashSet<string> names = RoleNames.SinkNames([first, second], IrSsaValue.Eax, Ax, null, null);

        // Assert
        names.Should().BeEquivalentTo("requestedSize", "previousAx");
        RoleNames.UniqueName(names).Should().BeNull("two different sinks must not pick an arbitrary name");
    }

    [Fact]
    public void WebUsedAsMemoryOffset_GetsTheIndexRole() {
        // Arrange: BX is the offset term of a segmented store.
        SegmentedPointerNode pointer = new(DataType.UINT8,
            new SegmentRegisterNode(3), null, Bx);

        // Act
        string? name = RoleNames.RoleName([pointer], IrSsaValue.Ebx, Bx, false, []);

        // Assert
        name.Should().Be("index", "the role rule wins over the generic fallback");
    }

    [Fact]
    public void WebWithNoEvidence_GetsNoHint() {
        // Arrange: BX occurs in no memory offset, loop, rep or far-pointer load.
        ConstantNode constant = new(DataType.UINT16, 0x1234);

        // Act
        string? name = RoleNames.RoleName([constant], IrSsaValue.Ebx, Bx, false, []);

        // Assert
        name.Should().BeNull();
    }
}
