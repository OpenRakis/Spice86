namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// <see cref="IrAstChildren"/> must know every concrete <see cref="IVisitableAstNode"/> type: the
/// switch arms throw <see cref="NotSupportedException"/> on a node they do not cover instead of
/// silently dropping a subtree the old catch-all ignored.
/// </summary>
public class IrAstChildrenTest {
    public static IEnumerable<object[]> ConcreteNodeTypes() =>
        typeof(IVisitableAstNode).Assembly.GetTypes()
            .Where(type => typeof(IVisitableAstNode).IsAssignableFrom(type)
                && !type.IsAbstract && !type.IsInterface)
            .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(ConcreteNodeTypes))]
    public void EveryConcreteNodeType_IsSupported(Type nodeType) {
        IVisitableAstNode node =
            (IVisitableAstNode)RuntimeHelpers.GetUninitializedObject(nodeType);

        Action enumerate = () => IrAstChildren.ChildrenOf(node).ToList();
        Action map = () => IrAstChildren.MapChildren(node, child => child);

        enumerate.Should().NotThrow<NotSupportedException>($"{nodeType.Name} must be enumerated");
        map.Should().NotThrow<NotSupportedException>($"{nodeType.Name} must be mappable");
    }

    [Fact]
    public void MapChildren_WithIdentityMapping_ReturnsTheSameInstance() {
        BinaryOperationNode node = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16,
                new RegisterNode(DataType.UINT16, 0), BinaryOperation.PLUS,
                new ConstantNode(DataType.UINT16, 1)),
            BinaryOperation.PLUS, new RegisterNode(DataType.UINT16, 1));

        IVisitableAstNode mapped = IrAstChildren.MapChildren(node, child => child);

        mapped.Should().BeSameAs(node);
    }

    [Fact]
    public void MapChildren_RebuildingATypedCall_KeepsItsSubtype() {
        AluCallNode call = new(AluOperation.Add, BitWidth.WORD_16,
            new RegisterNode(DataType.UINT16, 0));
        ConstantNode added = new(DataType.UINT16, 1);

        IVisitableAstNode mapped = IrAstChildren.MapChildren(call,
            child => child is RegisterNode ? added : child);

        mapped.Should().BeOfType<AluCallNode>()
            .Which.Arguments.Should().Equal(added);
    }

    [Fact]
    public void TryMapChildren_WhenEveryChildMaps_RebuildsTheNode() {
        BinaryOperationNode node = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1));
        ConstantNode replacement = new(DataType.UINT16, 7);

        IVisitableAstNode? mapped = IrAstChildren.TryMapChildren(node,
            child => child is RegisterNode ? replacement : child);

        mapped.Should().BeOfType<BinaryOperationNode>().Which.Left.Should().BeSameAs(replacement);
    }

    [Fact]
    public void TryMapChildren_WhenAChildIsRefused_ReturnsNull() {
        BinaryOperationNode node = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1));

        IVisitableAstNode? mapped = IrAstChildren.TryMapChildren(node,
            child => child is ConstantNode ? null : child);

        mapped.Should().BeNull();
    }

    [Fact]
    public void TryMapChildren_WithIdentityMapping_ReturnsTheSameInstance() {
        BinaryOperationNode node = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1));

        IVisitableAstNode? mapped = IrAstChildren.TryMapChildren(node, child => child);

        mapped.Should().BeSameAs(node);
    }

    [Fact]
    public void ChildrenOf_AndMapChildren_ThrowOnAnUnknownNodeType() {
        IVisitableAstNode unknown = new UnknownNode();

        Action enumerate = () => IrAstChildren.ChildrenOf(unknown).ToList();
        Action map = () => IrAstChildren.MapChildren(unknown, child => child);

        enumerate.Should().Throw<NotSupportedException>().WithMessage("*UnknownNode*");
        map.Should().Throw<NotSupportedException>().WithMessage("*UnknownNode*");
    }

    private sealed class UnknownNode : IVisitableAstNode {
        public T Accept<T>(IAstVisitor<T> astVisitor) => throw new NotSupportedException();
    }
}
