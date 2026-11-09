namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Shared.Emulator.Memory;

using System.Reflection;
using System.Runtime.Loader;

using Xunit;

/// <summary>The constant chain folding <see cref="IrConstantFolder"/> applies to the additive and
/// shift chains expression merging creates.</summary>
public class IrConstantFolderTest {
    private static readonly RegisterNode Bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);

    [Fact]
    public void AdditionOfTwoConstantsFoldsToTheSum() {
        BinaryOperationNode add = new(DataType.UINT16, new ConstantNode(DataType.UINT16, 0),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 2));

        IVisitableAstNode folded = IrConstantFolder.FoldConstantTerms(add);

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

        IVisitableAstNode wrapped = IrConstantFolder.FoldConstantTerms(wrappingSum);
        IVisitableAstNode subtracted = IrConstantFolder.FoldConstantTerms(subtractSum);

        wrapped.Should().Be(new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1)));
        subtracted.Should().Be(new BinaryOperationNode(DataType.UINT16, Bx, BinaryOperation.MINUS,
            new ConstantNode(DataType.UINT16, 2)));
    }

    public static IEnumerable<object[]> ShiftChains64() {
        ulong[][] counts = [
            [31, 33], [63, 1], [64, 1], [65, 1], [ulong.MaxValue, 1]];
        foreach (DataType type in new[] { DataType.UINT64, DataType.INT64 }) {
            foreach (BinaryOperation operation in new[]
                { BinaryOperation.RIGHT_SHIFT, BinaryOperation.LEFT_SHIFT }) {
                foreach (ulong[] pair in counts) {
                    yield return [type, operation, pair[0], pair[1]];
                }
            }
        }
    }

    /// <summary>A same-operation 64-bit shift chain always folds; the compiled C# of the original
    /// and of the folded expression must agree on edge inputs, which checks the 6-bit count mask,
    /// the signed right-shift saturation and the zero result of an overflowing total against the
    /// CLR's own shift semantics.</summary>
    [Theory]
    [MemberData(nameof(ShiftChains64))]
    public void ShiftChain64FoldKeepsThePrintedSemantics(DataType dataType,
        BinaryOperation operation, ulong first, ulong second) {
        VariableReferenceNode value = new(dataType, "value");
        BinaryOperationNode chain = new(dataType,
            new BinaryOperationNode(dataType, value, operation, new ConstantNode(DataType.INT32, first)),
            operation, new ConstantNode(DataType.INT32, second));
        CSharpExpressionPrinter printer = new(new Dictionary<ushort, string>());

        IVisitableAstNode folded = IrConstantFolder.FoldConstantTerms(chain);

        folded.Should().NotBeSameAs(chain);
        string csharpType = dataType.Signed ? "long" : "ulong";
        string originalText = OriginalShiftText(printer, dataType, chain, operation, first, second);
        string foldedText = printer.Print(new TypeConversionNode(dataType, (ValueNode)folded)).Text;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([
            $"public static class Shifts {{ public static {csharpType} Original({csharpType} value) => {originalText}; public static {csharpType} Folded({csharpType} value) => {foldedText}; }}"]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"shifts '{originalText}' / '{foldedText}' must compile: "
            + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext context = new();
        try {
            peStream.Position = 0;
            Type compiled = context.LoadFromStream(peStream).GetType("Shifts")
                ?? throw new InvalidOperationException("Compiled Shifts type missing.");
            MethodInfo original = compiled.GetMethod("Original")
                ?? throw new InvalidOperationException("Compiled Original method missing.");
            MethodInfo foldedMethod = compiled.GetMethod("Folded")
                ?? throw new InvalidOperationException("Compiled Folded method missing.");
            foreach (ulong input in new ulong[]
                { 0, 1, ulong.MaxValue, 0x8000000000000000, 0x8000000000000001 }) {
                object argument = dataType.Signed ? (long)input : input;
                foldedMethod.Invoke(null, [argument]).Should().Be(original.Invoke(null, [argument]),
                    $"input 0x{input:X16}: '{originalText}' vs '{foldedText}'");
            }
        } finally {
            context.Unload();
        }
    }

    /// <summary>The original chain printed as C#; a count above <see cref="int.MaxValue"/> prints a
    /// literal a C# shift count cannot take, so the same semantics (the CLR masks the count) is
    /// written as an explicit int count.</summary>
    private static string OriginalShiftText(CSharpExpressionPrinter printer, DataType dataType,
        BinaryOperationNode chain, BinaryOperation operation, ulong first, ulong second) {
        if (first <= int.MaxValue && second <= int.MaxValue) {
            return printer.Print(new TypeConversionNode(dataType, chain)).Text;
        }
        string sign = operation == BinaryOperation.LEFT_SHIFT ? "<<" : ">>";
        string csharpType = dataType.Signed ? "long" : "ulong";
        return $"({csharpType})(value {sign} unchecked((int){first}UL) {sign} unchecked((int){second}UL))";
    }

    public static IEnumerable<object[]> RefusedShiftChains() {
        VariableReferenceNode value32 = new(DataType.UINT32, "value");
        ConstantNode four = new(DataType.INT32, 4);
        // Different operator.
        yield return [new BinaryOperationNode(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32, value32, BinaryOperation.LEFT_SHIFT, four),
            BinaryOperation.RIGHT_SHIFT, four)];
        // Different inner result type.
        yield return [new BinaryOperationNode(DataType.UINT64,
            new BinaryOperationNode(DataType.UINT32, value32, BinaryOperation.RIGHT_SHIFT, four),
            BinaryOperation.RIGHT_SHIFT, four)];
        // A width-changing cast between the shifts.
        VariableReferenceNode value16 = new(DataType.UINT16, "value");
        yield return [new BinaryOperationNode(DataType.UINT32,
            new TypeConversionNode(DataType.UINT32,
                new BinaryOperationNode(DataType.UINT16, value16, BinaryOperation.RIGHT_SHIFT, four)),
            BinaryOperation.RIGHT_SHIFT, four)];
        // A variable count.
        RegisterNode count = new(DataType.UINT32, (int)RegisterIndex.CxIndex);
        yield return [new BinaryOperationNode(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32, value32, BinaryOperation.RIGHT_SHIFT, count),
            BinaryOperation.RIGHT_SHIFT, four)];
    }

    /// <summary>Only a chain of one shift operation on one type with constant counts folds:
    /// anything else is returned unchanged.</summary>
    [Theory]
    [MemberData(nameof(RefusedShiftChains))]
    public void RefusedShiftChainsStayUnchanged(BinaryOperationNode chain) {
        IrConstantFolder.FoldConstantTerms(chain).Should().BeSameAs(chain);
    }

    public static IEnumerable<object[]> ShiftChainOperands() {
        yield return [new SegmentedPointerNode(DataType.UINT16,
            new SegmentRegisterNode((int)SegmentRegisterIndex.DsIndex), null, Bx)];
        yield return [new MethodCallValueNode(DataType.UINT16, null, "Read")];
    }

    /// <summary>A shift chain whose total reaches the operand width folds to
    /// <c>operand &amp; 0</c>: the operand's memory read, call or fault still evaluates exactly
    /// once. A 16-bit pair of counts summing to 32 traps a fold that would use C#'s 5-bit int
    /// promotion mask as the overflow boundary.</summary>
    [Theory]
    [MemberData(nameof(ShiftChainOperands))]
    public void OverflowingShiftChainKeepsTheOperandRead(ValueNode operand) {
        BinaryOperationNode chain = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, operand, BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, 16)),
            BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.INT32, 16));

        IVisitableAstNode folded = IrConstantFolder.FoldConstantTerms(chain);

        IrEffectsVisitor.Of(folded, false).Should().Be(IrEffectsVisitor.Of(chain, false));
        IrAstChildren.Descendants(folded).Count(node => ReferenceEquals(node, operand))
            .Should().Be(1);
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
