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

using System.Linq;
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

    private static readonly VariableReferenceNode X = new(DataType.UINT16, "value");
    private static readonly VariableReferenceNode X8 = new(DataType.UINT8, "value");
    private static readonly VariableReferenceNode X32 = new(DataType.UINT32, "value");
    private static readonly VariableReferenceNode X64 = new(DataType.UINT64, "value");
    private static readonly VariableReferenceNode Y = new(DataType.UINT16, "value2");
    private static readonly VariableReferenceNode Cond = new(DataType.BOOL, "cond");
    private static readonly ConstantNode Four = new(DataType.INT32, 4);

    private static BinaryOperationNode Bin(DataType type, ValueNode left, BinaryOperation operation,
        ValueNode right) => new(type, left, operation, right);

    private static BinaryOperationNode Bin(ValueNode left, BinaryOperation operation,
        ValueNode right) => new(left.DataType, left, operation, right);

    private static UnaryOperationNode Un(DataType type, UnaryOperation operation, ValueNode value) =>
        new(type, operation, value);

    /// <summary>The algebra folds <see cref="IrConstantFolder.FoldConstantTerms"/> applies to mask,
    /// shift, inverse, comparison and conditional expressions. The compiled C# of the original and
    /// of the folded expression must agree on every probe input, which checks the fold against the
    /// CLR's own integer promotion and shift semantics.</summary>
    public static IEnumerable<object[]> AlgebraFoldExpressions() {
        yield return ["and masks combine", Bin(Bin(X, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xFF)), BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xFFF)), "ushort value", new object[][] { W16 }];
        yield return ["or masks combine", Or(Or(X, 0xF0), 0x0F), "ushort value",
            new object[][] { W16 }];
        yield return ["xor constants cancel", Xor(Xor(X, 0xAA55), 0xAA55), "ushort value",
            new object[][] { W16 }];
        yield return ["double bitwise not", Un(DataType.UINT16, UnaryOperation.BITWISE_NOT,
            Un(DataType.UINT16, UnaryOperation.BITWISE_NOT, X)), "ushort value",
            new object[][] { W16 }];
        yield return ["double negate", Un(DataType.UINT16, UnaryOperation.NEGATE,
            Un(DataType.UINT16, UnaryOperation.NEGATE, X)), "ushort value",
            new object[][] { W16 }];
        yield return ["left then right shift", Bin(Bin(X, BinaryOperation.LEFT_SHIFT, Four),
            BinaryOperation.RIGHT_SHIFT, Four), "ushort value", new object[][] { W16 }];
        yield return ["left then right shift 32", Bin(Bin(X32, BinaryOperation.LEFT_SHIFT, Four),
            BinaryOperation.RIGHT_SHIFT, Four), "uint value", new object[][] { W32 }];
        yield return ["left then right shift byte", Bin(Bin(X8, BinaryOperation.LEFT_SHIFT, Four),
            BinaryOperation.RIGHT_SHIFT, Four), "byte value", new object[][] { W8 }];
        yield return ["right then left shift", Bin(Bin(X, BinaryOperation.RIGHT_SHIFT, Four),
            BinaryOperation.LEFT_SHIFT, Four), "ushort value", new object[][] { W16 }];
        yield return ["mask of right shift dropped", Bin(Bin(X, BinaryOperation.RIGHT_SHIFT, Four),
            BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x0FFF)),
            "ushort value", new object[][] { W16 }];
        yield return ["mask of right shift dropped 64", Bin(Bin(X64, BinaryOperation.RIGHT_SHIFT,
            new ConstantNode(DataType.INT32, 8)), BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT64, 0x00FFFFFFFFFFFFFF)), "ulong value",
            new object[][] { W64 }];
        yield return ["mask covering shifted-out bits dropped", Bin(
            Bin(X, BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFFF0)),
            BinaryOperation.RIGHT_SHIFT, Four), "ushort value", new object[][] { W16 }];
        yield return ["known width mask dropped", Bin(
            new TypeConversionNode(DataType.UINT16, X8), BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xFF)), "byte value", new object[][] { W8 }];
        yield return ["x and x", Bin(X, BinaryOperation.BITWISE_AND, X), "ushort value",
            new object[][] { W16 }];
        yield return ["x or x", Bin(X, BinaryOperation.BITWISE_OR, X), "ushort value",
            new object[][] { W16 }];
        yield return ["x times one", Bin(X, BinaryOperation.MULTIPLY,
            new ConstantNode(DataType.UINT16, 1)), "ushort value", new object[][] { W16 }];
        yield return ["x and zero", Bin(X, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0)), "ushort value", new object[][] { W16 }];
        yield return ["x or all ones", Bin(X, BinaryOperation.BITWISE_OR,
            new ConstantNode(DataType.UINT16, 0xFFFF)), "ushort value", new object[][] { W16 }];
        yield return ["x and not x", Bin(X, BinaryOperation.BITWISE_AND,
            Un(DataType.UINT16, UnaryOperation.BITWISE_NOT, X)), "ushort value",
            new object[][] { W16 }];
        yield return ["x or not x", Bin(X, BinaryOperation.BITWISE_OR,
            Un(DataType.UINT16, UnaryOperation.BITWISE_NOT, X)), "ushort value",
            new object[][] { W16 }];
        yield return ["x xor not x", Bin(X, BinaryOperation.BITWISE_XOR,
            Un(DataType.UINT16, UnaryOperation.BITWISE_NOT, X)), "ushort value",
            new object[][] { W16 }];
        yield return ["x xor all ones", Bin(X, BinaryOperation.BITWISE_XOR,
            new ConstantNode(DataType.UINT16, 0xFFFF)), "ushort value", new object[][] { W16 }];
        yield return ["masked or covered constant", Bin(Bin(X, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xF0)), BinaryOperation.BITWISE_OR,
            new ConstantNode(DataType.UINT16, 0xF0)), "ushort value", new object[][] { W16 }];
        yield return ["ored and covering mask", Bin(Bin(X, BinaryOperation.BITWISE_OR,
            new ConstantNode(DataType.UINT16, 0xF0)), BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xF0)), "ushort value", new object[][] { W16 }];
        yield return ["and pair masks merge", Bin(Bin(X, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0xF0)), BinaryOperation.BITWISE_OR,
            Bin(X, BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x0F))),
            "ushort value", new object[][] { W16 }];
        yield return ["or pair masks merge", Bin(Bin(X, BinaryOperation.BITWISE_OR,
            new ConstantNode(DataType.UINT16, 0x0F)), BinaryOperation.BITWISE_AND,
            Bin(X, BinaryOperation.BITWISE_OR, new ConstantNode(DataType.UINT16, 0xF0))),
            "ushort value", new object[][] { W16 }];
        yield return ["and pair masks merge to identity", Bin(
            Bin(X, BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFF00)),
            BinaryOperation.BITWISE_OR,
            Bin(X, BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x00FF))),
            "ushort value", new object[][] { W16 }];
        yield return ["or under and forces the mask", Bin(
            Or(X, 0xFF), BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x0F)),
            "ushort value", new object[][] { W16 }];
        yield return ["or under and all ones keeps the or", Bin(
            Or(X, 0x0F), BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFFFF)),
            "ushort value", new object[][] { W16 }];
        yield return ["xor of pair is equality", Bin(DataType.BOOL,
            Bin(X, BinaryOperation.BITWISE_XOR, Y), BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0)), "ushort value, ushort value2",
            new object[][] { W16, W16 }];
        yield return ["xor of pair is inequality", Bin(DataType.BOOL,
            Bin(X, BinaryOperation.BITWISE_XOR, Y), BinaryOperation.NOT_EQUAL,
            new ConstantNode(DataType.UINT16, 0)), "ushort value, ushort value2",
            new object[][] { W16, W16 }];
        yield return ["difference of pair is equality", Bin(DataType.BOOL,
            Bin(X, BinaryOperation.MINUS, Y), BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0)), "ushort value, ushort value2",
            new object[][] { W16, W16 }];
        yield return ["unsigned less than one", Bin(DataType.BOOL, X, BinaryOperation.LESS_THAN,
            new ConstantNode(DataType.UINT16, 1)), "ushort value", new object[][] { W16 }];
        yield return ["unsigned less or equal zero", Bin(DataType.BOOL, X,
            BinaryOperation.LESS_THAN_OR_EQUAL, new ConstantNode(DataType.UINT16, 0)),
            "ushort value", new object[][] { W16 }];
        yield return ["unsigned greater than zero", Bin(DataType.BOOL, X,
            BinaryOperation.GREATER_THAN, new ConstantNode(DataType.UINT16, 0)),
            "ushort value", new object[][] { W16 }];
        yield return ["unsigned greater or equal one", Bin(DataType.BOOL, X,
            BinaryOperation.GREATER_THAN_OR_EQUAL, new ConstantNode(DataType.UINT16, 1)),
            "ushort value", new object[][] { W16 }];
        yield return ["zero minus conditional", Bin(DataType.UINT16,
            new ConstantNode(DataType.UINT16, 0), BinaryOperation.MINUS,
            new ConditionalValueNode(DataType.UINT16, Cond,
                new ConstantNode(DataType.UINT16, 1), new ConstantNode(DataType.UINT16, 0))),
            "bool cond", new object[][] { Bools }];
        yield return ["conditional plus one", Bin(DataType.UINT16,
            new ConditionalValueNode(DataType.UINT16, Cond,
                new ConstantNode(DataType.UINT16, 1), new ConstantNode(DataType.UINT16, 0)),
            BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1)), "bool cond",
            new object[][] { Bools }];
        yield return ["zero minus negated conditional", Bin(DataType.UINT16,
            new ConstantNode(DataType.UINT16, 0), BinaryOperation.MINUS,
            Bin(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), BinaryOperation.MINUS,
                new ConditionalValueNode(DataType.UINT16, Cond,
                    new ConstantNode(DataType.UINT16, 1), new ConstantNode(DataType.UINT16, 0)))),
            "bool cond", new object[][] { Bools }];
        yield return ["negated conditional", Un(DataType.UINT16, UnaryOperation.NEGATE,
            new ConditionalValueNode(DataType.UINT16, Cond,
                new ConstantNode(DataType.UINT16, 1), new ConstantNode(DataType.UINT16, 0))),
            "bool cond", new object[][] { Bools }];
        yield return ["negated word is zero", Bin(DataType.BOOL,
            Un(DataType.UINT16, UnaryOperation.NEGATE, X), BinaryOperation.EQUAL,
            new ConstantNode(DataType.UINT16, 0)), "ushort value", new object[][] { W16 }];
        yield return ["negated word is not zero", Bin(DataType.BOOL,
            Un(DataType.UINT16, UnaryOperation.NEGATE, X), BinaryOperation.NOT_EQUAL,
            new ConstantNode(DataType.UINT16, 0)), "ushort value", new object[][] { W16 }];
        yield return ["bool xor is inequality", Bin(DataType.BOOL, Cond, BinaryOperation.BITWISE_XOR,
            new VariableReferenceNode(DataType.BOOL, "cond2")), "bool cond, bool cond2",
            new object[][] { Bools, Bools }];
        yield return ["bool equals false", Bin(DataType.BOOL, Cond, BinaryOperation.EQUAL,
            new ConstantNode(DataType.BOOL, 0)), "bool cond", new object[][] { Bools }];
        yield return ["bool equals true", Bin(DataType.BOOL, Cond, BinaryOperation.EQUAL,
            new ConstantNode(DataType.BOOL, 1)), "bool cond", new object[][] { Bools }];
        yield return ["bool not equals true", Bin(DataType.BOOL, Cond, BinaryOperation.NOT_EQUAL,
            new ConstantNode(DataType.BOOL, 1)), "bool cond", new object[][] { Bools }];
    }

    private static readonly object[] W8 = Enumerable.Range(0, 256).Select(i => (object)i).ToArray();
    private static readonly object[] W16 = [0, 1, 0xFF, 0xFFF, 0x1234, 0x8001, 0xABCD, 0xFFFF];
    private static readonly object[] W32 = [0U, 1U, 0xFFU, 0xFFFFU, 0x12345678U, 0x80000001U, 0xFFFFFFFFU];
    private static readonly object[] W64 = [0UL, 1UL, 0xFFUL, 0xFFFFUL, 0x123456789AUL,
        0x8000000000000001UL, 0xFFFFFFFFFFFFFFFFUL];
    private static readonly object[] Bools = [false, true];

    private static BinaryOperationNode Or(ValueNode left, ulong mask) =>
        Bin(left, BinaryOperation.BITWISE_OR, new ConstantNode(left.DataType, mask));

    private static BinaryOperationNode Xor(ValueNode left, ulong mask) =>
        Bin(left, BinaryOperation.BITWISE_XOR, new ConstantNode(left.DataType, mask));

    [Theory]
    [MemberData(nameof(AlgebraFoldExpressions))]
    public void AlgebraFoldKeepsThePrintedSemantics(string description, ValueNode expression,
        string arguments, params object[][] inputs) {
        CSharpExpressionPrinter printer = new(new Dictionary<ushort, string>());

        IVisitableAstNode folded = IrConstantFolder.FoldConstantTerms(expression);

        folded.Should().NotBeSameAs(expression, $"{description} must be a supported fold");
        ValueNode foldedValue = (ValueNode)folded;
        ValueNode printed = expression.DataType == DataType.BOOL ? expression
            : new TypeConversionNode(expression.DataType, expression);
        ValueNode printedFolded = foldedValue.DataType == DataType.BOOL ? foldedValue
            : new TypeConversionNode(foldedValue.DataType, foldedValue);
        string originalText = printer.Print(printed).Text;
        string foldedText = printer.Print(printedFolded).Text;
        string returnType = expression.DataType == DataType.BOOL ? "bool" : CSharpType(expression.DataType);
        AssertEquivalent($"{description}: '{originalText}' vs '{foldedText}'",
            $"public static class Algebra {{ public static {returnType} Original({arguments}) => {originalText}; public static {returnType} Folded({arguments}) => {foldedText}; }}",
            inputs);
    }

    private static string CSharpType(DataType type) => type.BitWidth switch {
        BitWidth.BYTE_8 => type.Signed ? "sbyte" : "byte",
        BitWidth.WORD_16 => type.Signed ? "short" : "ushort",
        BitWidth.DWORD_32 => type.Signed ? "int" : "uint",
        BitWidth.QWORD_64 => type.Signed ? "long" : "ulong",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>Compiles a class exposing the original and folded expressions, then invokes both on
    /// every input combination and requires equal results.</summary>
    private static IEnumerable<object> SecondInputs(object[][] inputs) =>
        inputs.Length == 1 ? [new object()] : inputs[1];

    private static void AssertEquivalent(string context, string source, object[][] inputs) {
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([source]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"{context} must compile: " + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext loadContext = new();
        try {
            peStream.Position = 0;
            Type compiled = loadContext.LoadFromStream(peStream).GetTypes()[0];
            MethodInfo original = compiled.GetMethod("Original")
                ?? throw new InvalidOperationException("Compiled Original method missing.");
            MethodInfo foldedMethod = compiled.GetMethod("Folded")
                ?? throw new InvalidOperationException("Compiled Folded method missing.");
            Type[] parameterTypes = [.. original.GetParameters().Select(p => p.ParameterType)];
            foreach (object first in inputs[0]) {
                foreach (object second in SecondInputs(inputs)) {
                    object[] input = inputs.Length == 1 ? [first] : [first, second];
                    object[] arguments = [.. input.Select((argument, i) =>
                        parameterTypes[i].IsInstanceOfType(argument)
                            ? argument : Convert.ChangeType(argument, parameterTypes[i]))];
                    object? expected = original.Invoke(null, arguments);
                    foldedMethod.Invoke(null, arguments).Should().Be(expected, context);
                }
            }
        } finally {
            loadContext.Unload();
        }
    }

    public static IEnumerable<object[]> RefusedAlgebraExpressions() {
        VariableReferenceNode x = new(DataType.UINT16, "value");
        // A memory read cannot be evaluated a second time away.
        SegmentedPointerNode read = new(DataType.UINT16,
            new SegmentRegisterNode((int)SegmentRegisterIndex.DsIndex), null, Bx);
        yield return [new BinaryOperationNode(DataType.UINT16, read, BinaryOperation.BITWISE_AND, read)];
        yield return [new BinaryOperationNode(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, read, BinaryOperation.BITWISE_AND,
                new ConstantNode(DataType.UINT16, 0xF0)), BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(DataType.UINT16, read, BinaryOperation.BITWISE_AND,
                new ConstantNode(DataType.UINT16, 0x0F)))];
        // A call cannot be forced to a constant either.
        MethodCallValueNode call = new(DataType.UINT16, null, "Read");
        yield return [new BinaryOperationNode(DataType.UINT16, call, BinaryOperation.BITWISE_AND,
            new ConstantNode(DataType.UINT16, 0))];
        yield return [new BinaryOperationNode(DataType.UINT16, call, BinaryOperation.MULTIPLY,
            new ConstantNode(DataType.UINT16, 0))];
        yield return [new BinaryOperationNode(DataType.UINT16, call, BinaryOperation.BITWISE_AND,
            new UnaryOperationNode(DataType.UINT16, UnaryOperation.BITWISE_NOT, call))];
        // A sign-extending narrowing keeps its mask.
        yield return [new BinaryOperationNode(DataType.INT16,
            new TypeConversionNode(DataType.INT16, new VariableReferenceNode(DataType.INT8, "value")),
            BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFF))];
        // A signed right shift is arithmetic: the unsigned mixed-shift rule does not apply.
        yield return [new BinaryOperationNode(DataType.INT16,
            new BinaryOperationNode(DataType.INT16, new VariableReferenceNode(DataType.INT16, "value"),
                BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, 4)),
            BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.INT32, 4))];
        // A mask narrower than the kept bits of a right shift stays.
        yield return [new BinaryOperationNode(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, x, BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, 4)),
            BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x07FF))];
        // `(x | 0x0F) & 0xFF` is not forced: the constant does not cover the mask.
        yield return [new BinaryOperationNode(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, x, BinaryOperation.BITWISE_OR,
                new ConstantNode(DataType.UINT16, 0x0F)),
            BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0xFF))];
        // An impure operand makes the forced-constant form stay too.
        yield return [new BinaryOperationNode(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, read, BinaryOperation.BITWISE_OR,
                new ConstantNode(DataType.UINT16, 0xFF)),
            BinaryOperation.BITWISE_AND, new ConstantNode(DataType.UINT16, 0x0F))];
        // A conditional with a non-constant arm cannot absorb a surrounding operator.
        yield return [new BinaryOperationNode(DataType.UINT16,
            new ConstantNode(DataType.UINT16, 0), BinaryOperation.MINUS,
            new ConditionalValueNode(DataType.UINT16, new VariableReferenceNode(DataType.BOOL, "cond"),
                x, new ConstantNode(DataType.UINT16, 0)))];
        // An impure boolean operand cannot collapse or force a constant.
        MethodCallValueNode boolCall = new(DataType.BOOL, null, "Flag");
        yield return [new BinaryOperationNode(DataType.BOOL, boolCall, BinaryOperation.LOGICAL_AND,
            boolCall)];
        yield return [new BinaryOperationNode(DataType.BOOL, boolCall, BinaryOperation.LOGICAL_OR,
            new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, boolCall))];
        // A narrowed `-x == 0` tests the truncated negation, not `x` itself: the byte
        // negation of 0x0100 is zero while the word is not.
        yield return [new BinaryOperationNode(DataType.BOOL,
            new UnaryOperationNode(DataType.UINT8, UnaryOperation.NEGATE, x),
            BinaryOperation.EQUAL, new ConstantNode(DataType.UINT8, 0))];
        yield return [new BinaryOperationNode(DataType.BOOL,
            new UnaryOperationNode(DataType.UINT8, UnaryOperation.NEGATE, x),
            BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT8, 0))];
    }

    /// <summary>Shapes the algebra fold must refuse: a duplicated or forced operand that is not
    /// pure, a sign-extending narrowing, a signed right shift and every mask that does not cover the
    /// shifted bits.</summary>
    [Theory]
    [MemberData(nameof(RefusedAlgebraExpressions))]
    public void RefusedAlgebraExpressionsStayUnchanged(ValueNode expression) {
        IrConstantFolder.FoldConstantTerms(expression).Should().BeSameAs(expression);
    }

    [Fact]
    public void EqualConditionalArmsOfAnImpureConditionStayAConditional() {
        ConditionalValueNode conditional = new(DataType.UINT8,
            new MethodCallValueNode(DataType.BOOL, null, "Flag"),
            new ConstantNode(DataType.UINT8, 0x12), new ConstantNode(DataType.UINT8, 0x12));

        IrConstantFolder.Fold(conditional, IrConstantFolder.NoVariableConstants)
            .Should().BeSameAs(conditional);
    }

    /// <summary><c>call || true</c> and <c>call &amp;&amp; false</c> keep the call because dropping
    /// it would skip its effects; <c>true || call</c> and <c>false &amp;&amp; call</c> fold because
    /// the short-circuit never evaluated it. The compiled folded expression must return the same
    /// value after the same number of calls.</summary>
    public static IEnumerable<object[]> LogicalCollapseCases() {
        MethodCallValueNode call() => new(DataType.BOOL, null, "Flag");
        yield return [new BinaryOperationNode(DataType.BOOL, call(), BinaryOperation.LOGICAL_OR,
            new ConstantNode(DataType.BOOL, 1))];
        yield return [new BinaryOperationNode(DataType.BOOL, call(), BinaryOperation.LOGICAL_AND,
            new ConstantNode(DataType.BOOL, 0))];
        yield return [new BinaryOperationNode(DataType.BOOL, new ConstantNode(DataType.BOOL, 1),
            BinaryOperation.LOGICAL_OR, call())];
        yield return [new BinaryOperationNode(DataType.BOOL, new ConstantNode(DataType.BOOL, 0),
            BinaryOperation.LOGICAL_AND, call())];
    }

    [Theory]
    [MemberData(nameof(LogicalCollapseCases))]
    public void LogicalCollapseKeepsTheCallCount(ValueNode expression) {
        CSharpExpressionPrinter printer = new(new Dictionary<ushort, string>());
        string originalText = printer.Print(expression).Text;
        IVisitableAstNode folded = IrConstantFolder.Fold(expression,
            IrConstantFolder.NoVariableConstants);
        string foldedText = printer.Print((ValueNode)folded).Text;
        string source =
            "public static class Algebra { public static int Calls;"
            + " public static bool Flag() { Calls++; return true; }"
            + $" public static bool Original() => {originalText};"
            + $" public static bool Folded() => {foldedText}; }}";
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([source]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"'{originalText}' vs '{foldedText}' must compile: "
            + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext loadContext = new();
        try {
            peStream.Position = 0;
            Type compiled = loadContext.LoadFromStream(peStream).GetTypes()[0];
            FieldInfo calls = compiled.GetField("Calls")
                ?? throw new InvalidOperationException("Compiled Calls field missing.");
            (object? Result, int Count) Run(string name) {
                calls.SetValue(null, 0);
                object? result = (compiled.GetMethod(name)
                    ?? throw new InvalidOperationException($"Compiled {name} method missing."))
                    .Invoke(null, []);
                return (result, (int)(calls.GetValue(null) ?? 0));
            }
            (object? Result, int Count) original = Run("Original");
            (object? Result, int Count) foldedResult = Run("Folded");
            foldedResult.Should().Be(original, $"'{originalText}' vs '{foldedText}'");
        } finally {
            loadContext.Unload();
        }
    }
}
