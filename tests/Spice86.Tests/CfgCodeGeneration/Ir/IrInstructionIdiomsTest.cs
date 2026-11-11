namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using System.Reflection;
using System.Runtime.Loader;

using Xunit;

/// <summary>
/// The <c>xchg</c> and far-pointer idioms rewrite only on their tagged origins; each legality
/// refusal leaves the statements untouched.
/// </summary>
public sealed class IrInstructionIdiomsTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);
    private static readonly SegmentRegisterNode Ds = new(3);
    private static readonly SegmentRegisterNode Es = new(0);

    [Fact]
    public void ExchangeTriple_BecomesTupleSwap() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Bx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeEquivalentTo(new TupleSwapNode(Ax, Bx));
    }

    [Fact]
    public void ExchangeTriple_UntaggedOrigin_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Bx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.None, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void ExchangeOfSameRegister_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Ax), Assign(Ax, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void FarPointerQuadruple_BecomesTwoAssignments() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        ValueNode valueRead = Pointer(new ConstantNode(DataType.UINT16, 0x10));
        ValueNode segmentRead = Pointer(new ConstantNode(DataType.UINT16, 0x12));
        IrBlock block = Block(instruction, Declaration("value", valueRead),
            Declaration("segment", segmentRead),
            Assign(Bx, Reference("value")), Assign(Ds, Reference("segment")));
        IrMethod method = Method(instruction, IrInstructionShape.LoadFarPointer, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeEquivalentTo(Assign(Bx, valueRead));
        block.Statements[2].Node.Should().BeEquivalentTo(Assign(Ds, segmentRead));
    }

    [Fact]
    public void FarPointerSegmentAddressReadsValueRegister_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction,
            Declaration("value", Pointer(new ConstantNode(DataType.UINT16, 0x10))),
            Declaration("segment", Pointer(Bx)),
            Assign(Bx, Reference("value")), Assign(Ds, Reference("segment")));
        IrMethod method = Method(instruction, IrInstructionShape.LoadFarPointer, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(5);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void ExchangeTripleSplitAcrossOrigins_Stays() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock block = Block(first, Declaration("tmp", Ax), Assign(Ax, Bx));
        block.Statements.Add(IrStatement.Create(Assign(Bx, Reference("tmp")), second));
        IrMethod method = Method(first, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void BytePairInEntryBlock_Combines() {
        // Arrange: the sequence sits in the method entry block, which is not a merge target.
        CfgInstruction entryInstruction = Instruction(0);
        IrBlock block = new(0);
        RegisterNode dl = new(DataType.UINT8, 2);
        RegisterNode dh = new(DataType.UINT8, 6);
        RegisterNode al = new(DataType.UINT8, 0);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), entryInstruction));
        block.Statements.Add(IrStatement.Create(ByteAssign(dl, al), Instruction(1)));
        block.Statements.Add(IrStatement.Create(
            ByteAssign(dh, new ConstantNode(DataType.UINT8, 0)), Instruction(2)));
        block.Terminator = new IrExit(Instruction(2),
            new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = new("M", [block], [new IrEntry(entryInstruction, block)],
            new HashSet<ICfgNode> { entryInstruction },
            new Dictionary<ICfgNode, IrInstructionShape>());
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(1);
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void BytePairWithMiddleEntryMarker_Stays() {
        // Arrange: a mid-block entry lands on the marker between the byte writes.
        CfgInstruction landing = Instruction(9);
        RegisterNode dl = new(DataType.UINT8, 2);
        RegisterNode dh = new(DataType.UINT8, 6);
        RegisterNode al = new(DataType.UINT8, 0);
        (IrMethod method, IrBlock block) = SequenceMethod(
            [(ByteAssign(dl, al), Instruction(1)),
                (new InstructionStartNode("nop"), landing),
                (ByteAssign(dh, new ConstantNode(DataType.UINT8, 0)), Instruction(3))],
            [landing]);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void BytePairEntryOnSecondWrite_Stays() {
        // Arrange: a mid-block entry lands on the second byte write itself.
        CfgInstruction landing = Instruction(2);
        RegisterNode dl = new(DataType.UINT8, 2);
        RegisterNode dh = new(DataType.UINT8, 6);
        RegisterNode al = new(DataType.UINT8, 0);
        (IrMethod method, IrBlock block) = SequenceMethod(
            [(ByteAssign(dl, al), Instruction(1)),
                (ByteAssign(dh, new ConstantNode(DataType.UINT8, 0)), landing)],
            [landing]);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(3);
    }

    /// <summary>A byte arithmetic right side must keep its byte truncation inside the composed
    /// word: <c>AL = (byte)(BL + 1); AH = 0</c> with BL = 0xFF composes to AX = 0, not 0x0100.</summary>
    [Fact]
    public void BytePairArithmeticByteValue_KeepsTheTruncation() {
        // Arrange
        RegisterNode al = new(DataType.UINT8, 0);
        RegisterNode ah = new(DataType.UINT8, 4);
        RegisterNode bl = new(DataType.UINT8, 3);
        (IrMethod method, IrBlock block) = SequenceMethod(
            ByteAssign(al, new BinaryOperationNode(DataType.UINT8, bl, BinaryOperation.PLUS,
                new ConstantNode(DataType.UINT8, 1))),
            ByteAssign(ah, new ConstantNode(DataType.UINT8, 0)));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert: the printed composed word evaluates to the byte-truncated value.
        combined.Should().Be(1);
        BinaryOperationNode assign = block.Statements[1].Node.Should()
            .BeOfType<BinaryOperationNode>().Subject;
        CSharpExpressionPrinter printer = new(new Dictionary<ushort, string>());
        string text = printer.Print(assign.Right).Text;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([
            $"public static class Composition {{ public static int Folded(byte BL) => {text}; }}"]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"'{text}' must compile: " + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext loadContext = new();
        try {
            peStream.Position = 0;
            MethodInfo folded = loadContext.LoadFromStream(peStream).GetTypes()[0]
                .GetMethod("Folded")
                ?? throw new InvalidOperationException("Compiled Folded method missing.");
            folded.Invoke(null, [(byte)0xFF]).Should().Be(0, text);
            folded.Invoke(null, [(byte)0x34]).Should().Be(0x35, text);
        } finally {
            loadContext.Unload();
        }
    }

    [Fact]
    public void BytePairSecondReadsOverlappingRegister_Stays() {
        // Arrange: mov dl, al ; mov dh, (byte)(dx >> 8) reads DX over the pending DL write.
        RegisterNode dl = new(DataType.UINT8, 2);
        RegisterNode dh = new(DataType.UINT8, 6);
        RegisterNode al = new(DataType.UINT8, 0);
        RegisterNode dx = new(DataType.UINT16, 2);
        (IrMethod method, IrBlock block) = SequenceMethod(
            ByteAssign(dl, al),
            ByteAssign(dh, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT16, dx, BinaryOperation.RIGHT_SHIFT,
                    new ConstantNode(DataType.INT32, 8)))));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void XorSwapWithSubstitutedMiddleOperand_Stays() {
        // Arrange: ax ^= 0x5678 ; dx = 0x5678 ^ ax ; ax ^= dx swaps with a constant, which the
        // register-only pattern cannot prove is the DX value.
        RegisterNode dx = new(DataType.UINT16, 2);
        ConstantNode constant = new(DataType.UINT16, 0x5678);
        (IrMethod method, IrBlock block) = SequenceMethod(
            XorAssignValue(Ax, constant), XorAssignValue(dx, constant), XorAssign(Ax, dx));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void XorSwapOfSameRegister_Stays() {
        // Arrange: ax ^= ax zeroes it, it is not a swap.
        (IrMethod method, IrBlock block) = SequenceMethod(
            XorAssign(Ax, Ax), XorAssign(Ax, Ax), XorAssign(Ax, Ax));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void BytePairWithObserverBetween_Stays() {
        // Arrange: a store between the byte writes observes the first half.
        RegisterNode dl = new(DataType.UINT8, 2);
        RegisterNode dh = new(DataType.UINT8, 6);
        RegisterNode al = new(DataType.UINT8, 0);
        (IrMethod method, IrBlock block) = SequenceMethod(
            ByteAssign(dl, al),
            Assign(Pointer(new ConstantNode(DataType.UINT16, 0x14)), Bx),
            ByteAssign(dh, new ConstantNode(DataType.UINT8, 0)));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(4);
    }

    private static BinaryOperationNode ByteAssign(RegisterNode target, ValueNode value) =>
        new(DataType.UINT8, target, BinaryOperation.ASSIGN, value);

    private static BinaryOperationNode XorAssign(RegisterNode target, RegisterNode other) =>
        XorAssignValue(target, other);

    private static BinaryOperationNode XorAssignValue(RegisterNode target, ValueNode other) =>
        new(target.DataType, target, BinaryOperation.ASSIGN,
            new BinaryOperationNode(target.DataType, target, BinaryOperation.BITWISE_XOR, other));

    /// <summary>A method whose only non-entry block holds <paramref name="nodes"/>, one per
    /// instruction.</summary>
    private static (IrMethod Method, IrBlock Block) SequenceMethod(
        params IVisitableAstNode[] nodes) =>
        SequenceMethod([.. nodes.Select((node, i) => (node, Instruction((ushort)(i + 1))))], []);

    /// <summary>A method whose only non-entry block holds <paramref name="statements"/> with their
    /// origins; <paramref name="midEntries"/> adds mid-block entries landing on those origins so a
    /// merge through them must refuse.</summary>
    private static (IrMethod Method, IrBlock Block) SequenceMethod(
        IReadOnlyList<(IVisitableAstNode Node, CfgInstruction Origin)> statements,
        CfgInstruction[] midEntries) {
        CfgInstruction entryInstruction = Instruction(0);
        IrBlock entry = new(0);
        IrBlock sequence = new(1);
        entry.Terminator = new IrJump(entryInstruction, new IrEdge(sequence, false));
        sequence.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"),
            Instruction(1)));
        foreach ((IVisitableAstNode node, CfgInstruction origin) in statements) {
            sequence.Statements.Add(IrStatement.Create(node, origin));
        }
        sequence.Terminator = new IrExit(Instruction((ushort)(statements.Count + 1)),
            new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrEntry[] entries = [new IrEntry(entryInstruction, entry),
            .. midEntries.Select(node => new IrEntry(node, sequence))];
        IrMethod method = new("M", [entry, sequence], entries,
            new HashSet<ICfgNode>([entryInstruction, .. midEntries]),
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, sequence);
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static SegmentedPointerNode Pointer(ValueNode offset) =>
        new(DataType.UINT16, Es, null, offset);

    private static VariableDeclarationNode Declaration(string name, ValueNode initializer) =>
        new(DataType.UINT16, name, initializer);

    private static VariableReferenceNode Reference(string name) =>
        new(DataType.UINT16, name);

    private static BinaryOperationNode Assign(ValueNode left, ValueNode right) =>
        new(DataType.UINT16, left, BinaryOperation.ASSIGN, right);

    private static IrBlock Block(ICfgNode origin, params IVisitableAstNode[] nodes) {
        IrBlock block = new(0);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), origin));
        foreach (IVisitableAstNode node in nodes) {
            block.Statements.Add(IrStatement.Create(node, origin));
        }
        block.Terminator = new IrExit(origin, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        return block;
    }

    private static IrMethod Method(ICfgNode origin, IrInstructionShape shape, params IrBlock[] blocks) =>
        new("M", [.. blocks], [new IrEntry(origin, blocks[0])], new HashSet<ICfgNode> { origin },
            new Dictionary<ICfgNode, IrInstructionShape> { [origin] = shape });

    [Theory]
    [InlineData(0xCCCD, 3, 10)]
    [InlineData(0xAAAB, 1, 3)]
    public void ReciprocalDivision_FullDomain_MatchesHardwareDivide(ushort multiplier, int shift,
            ushort divisor) {
        // Arrange
        (IrMethod method, IrBlock block) = ReciprocalSequence(multiplier, shift, false);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert: the MUL+SHR pair is one DX = (ushort)(AX / divisor) assignment whose printed
        // form equals x / divisor on the whole ushort domain.
        combined.Should().Be(1);
        block.Statements.Should().HaveCount(6);
        BinaryOperationNode assign = block.Statements[1].Node.Should()
            .BeOfType<BinaryOperationNode>().Subject;
        CSharpExpressionPrinter printer = new(new Dictionary<ushort, string>());
        string text = printer.Print(assign.Right).Text;
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile([
            $"public static class Division {{ public static int Folded(ushort AX) => {text}; }}"]);
        using MemoryStream peStream = compilation.PeStream;
        compilation.EmitResult.Success.Should().BeTrue(
            $"'{text}' must compile: " + string.Join("; ", compilation.EmitResult.Diagnostics));
        CollectibleAssemblyLoadContext loadContext = new();
        try {
            peStream.Position = 0;
            MethodInfo folded = loadContext.LoadFromStream(peStream).GetTypes()[0]
                .GetMethod("Folded")
                ?? throw new InvalidOperationException("Compiled Folded method missing.");
            for (int x = ushort.MinValue; x <= ushort.MaxValue; x++) {
                object? actual = folded.Invoke(null, [(ushort)x]);
                actual.Should().Be(x / divisor, $"x = {x}: {text}");
            }
        } finally {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ReciprocalDivision_InvalidMultiplier_Stays() {
        // Arrange: 0xCCCC >> 3 cannot represent any exact division over the full domain.
        (IrMethod method, IrBlock block) = ReciprocalSequence(0xCCCC, 3, false);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(7);
    }

    [Fact]
    public void ReciprocalDivisionSigned_Stays() {
        // Arrange: IMUL is not the unsigned reciprocal idiom.
        (IrMethod method, IrBlock block) = ReciprocalSequence(0xCCCD, 3, true);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(7);
    }

    [Fact]
    public void ReciprocalDivisionLiveLowResult_Stays() {
        // Arrange: the low half of the product is still read after the window.
        (IrMethod method, IrBlock block) = ReciprocalSequence(0xCCCD, 3, false,
            Assign(Bx, Ax));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(8);
    }

    [Fact]
    public void ReciprocalDivisionLiveFlag_Stays() {
        // Arrange: a carry read after the window observes a flag the MUL wrote.
        (IrMethod method, IrBlock block) = ReciprocalSequence(0xCCCD, 3, false,
            Assign(Ax, new TypeConversionNode(DataType.UINT16,
                new ConditionalValueNode(DataType.UINT16, new CpuFlagNode(Flags.Carry),
                    new ConstantNode(DataType.UINT16, 1),
                    new ConstantNode(DataType.UINT16, 0)))));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int combined = IrInstructionIdioms.ApplyValueSequences(method, ssa);

        // Assert
        combined.Should().Be(0);
        block.Statements.Should().HaveCount(8);
    }

    /// <summary>A MUL constant + SHR sequence: <c>(AX, DX) = Mul16(AX, m); DX = DX >>> s</c>,
    /// followed by kills of the flags MUL writes so their versions are dead outside the window
    /// (a real <c>cmp</c> does the same), then <paramref name="tail"/> observers.</summary>
    private static (IrMethod Method, IrBlock Block) ReciprocalSequence(ushort multiplier,
            int shift, bool signed, params IVisitableAstNode[] tail) {
        RegisterNode dx = new(DataType.UINT16, 2);
        IVisitableAstNode mul = new MultiplicationNode(BitWidth.WORD_16, signed, Ax,
            new ConstantNode(DataType.UINT16, multiplier), Ax, dx);
        IVisitableAstNode shr = new BinaryOperationNode(DataType.UINT16, dx,
            BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.UINT16, dx, BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, (ulong)shift)));
        IVisitableAstNode lowKill = Assign(Ax, new ConstantNode(DataType.UINT16, 0));
        IVisitableAstNode[] kills = [
            FlagKill(Flags.Carry), FlagKill(Flags.Overflow), FlagKill(Flags.Zero)];
        return SequenceMethod([mul, shr, .. tail, lowKill, .. kills]);
    }

    private static BinaryOperationNode FlagKill(ushort flag) =>
        new(DataType.BOOL, new CpuFlagNode(flag), BinaryOperation.ASSIGN,
            new ConstantNode(DataType.BOOL, 0));
}
