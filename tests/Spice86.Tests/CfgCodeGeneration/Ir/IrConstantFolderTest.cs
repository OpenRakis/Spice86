namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>The additive constant folding <see cref="IrConstantFolder"/> applies to the chains
/// expression merging creates.</summary>
public class IrConstantFolderTest {
    private static readonly RegisterNode Bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);

    [Fact]
    public void AdditionOfTwoConstantsFoldsToTheSum() {
        BinaryOperationNode add = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 2));

        IVisitableAstNode folded = IrConstantFolder.FoldAdditiveTerms(add);

        folded.Should().Be(new ConstantNode(DataType.UINT16, 2));
    }

    [Fact]
    public void ChainedAdditionConstantsFoldModuloTheWidth() {
        BinaryOperationNode wrappingSum = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.PLUS,
                new ConstantNode(DataType.UINT16, 0xFFFE)),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 3));
        BinaryOperationNode subtractSum = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.PLUS,
                new ConstantNode(DataType.UINT16, 1)),
            BinaryOperation.MINUS, new ConstantNode(DataType.UINT16, 3));

        IVisitableAstNode wrapped = IrConstantFolder.FoldAdditiveTerms(wrappingSum);
        IVisitableAstNode subtracted = IrConstantFolder.FoldAdditiveTerms(subtractSum);

        wrapped.Should().Be(new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1)));
        subtracted.Should().Be(new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.MINUS,
            new ConstantNode(DataType.UINT16, 2)));
    }

    [Fact]
    public void ZeroPlusSignedDisplacementFoldsInTheAdditionType() {
        BinaryOperationNode add16 = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.INT16, unchecked((ushort)-26956)));
        BinaryOperationNode add8 = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.INT8, unchecked((byte)-2)));

        IVisitableAstNode folded16 = IrConstantFolder.Fold(add16, IrConstantFolder.NoVariableConstants);
        IVisitableAstNode folded8 = IrConstantFolder.Fold(add8, IrConstantFolder.NoVariableConstants);

        folded16.Should().Be(new ConstantNode(DataType.UINT16, 0x96B4));
        folded8.Should().Be(new ConstantNode(DataType.UINT16, 0xFFFE));
    }

    [Fact]
    public void ZeroTermIsKeptWhenTheOtherOperandHasAnotherType() {
        BinaryOperationNode add = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new RegisterNode(DataType.UINT8, (int)RegisterIndex.AxIndex));

        IVisitableAstNode folded = IrConstantFolder.Fold(add, IrConstantFolder.NoVariableConstants);

        folded.Should().Be(add);
    }

    [Fact]
    public void ZeroTermIsDroppedWhenTheOtherOperandHasTheAdditionType() {
        BinaryOperationNode add = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, Bx);

        IVisitableAstNode folded = IrConstantFolder.Fold(add, IrConstantFolder.NoVariableConstants);

        folded.Should().Be(Bx);
    }
}
