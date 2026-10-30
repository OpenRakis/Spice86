namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The sparse SSA of hand-built IR methods: definitions and uses of the machine values and tokens,
/// phis at joins and loop headers, entry merges, legality queries and the SSA verifier.
/// </summary>
public sealed class IrSsaTest {
    private static readonly IrValues Eax = (IrValues)0xFUL;
    private static readonly IrValues Ecx = (IrValues)(0xFUL << 4);
    private static readonly IrValues Ebx = (IrValues)(0xFUL << 12);
    private static readonly IrValues Esi = (IrValues)(0xFUL << 24);
    private static readonly IrValues Edi = (IrValues)(0xFUL << 28);
    private static readonly IrValues Es = (IrValues)(1UL << 32);
    private static readonly IrValues Ds = (IrValues)(1UL << 35);
    private static readonly IrValues Ax = (IrValues)0x3UL;
    private static readonly IrValues Carry = (IrValues)(1UL << 38);

    [Fact]
    public void StraightLine_AddUsesMovVersionAndDefinesANewOne() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrStatement add = new(new CommentNode("add"), instruction,
            new IrEffects(Ax | (IrValues)(0x3UL << 12), Ax, IrValues.None, IrTokens.None, IrTokens.None, false, false));
        block.Statements.Add(mov);
        block.Statements.Add(add);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        int movVersion = ssa.Definitions(mov).Single().Version;
        ssa.VersionUsed(add, IrSsaValue.Eax).Should().Be(movVersion);
        int addVersion = ssa.Definitions(add).Single().Version;
        addVersion.Should().NotBe(movVersion);
        ssa.Info(addVersion).Kind.Should().Be(IrSsaDefinitionKind.PartialDefinition);
    }

    [Fact]
    public void PartialDefinition_LinksToThePreviousVersion() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement movEax = Writes(instruction, Eax);
        IrStatement movAl = Writes(instruction, (IrValues)0x1UL);
        block.Statements.Add(movEax);
        block.Statements.Add(movAl);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        int partial = ssa.Definitions(movAl).Single().Version;
        IrSsaVersionInfo info = ssa.Info(partial);
        info.Kind.Should().Be(IrSsaDefinitionKind.PartialDefinition);
        info.Previous.Should().Be(ssa.Definitions(movEax).Single().Version);
    }

    [Fact]
    public void Join_TwoWritingArmsMeetInAPhi() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        IrStatement trueWrite = Writes(instruction, Eax);
        IrStatement falseWrite = Writes(instruction, Eax);
        trueArm.Statements.Add(trueWrite);
        falseArm.Statements.Add(falseWrite);
        entry.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        IrSsaPhi phi = ssa.PhisOf(join).Should().ContainSingle(p => p.Value == IrSsaValue.Eax).Subject;
        phi.Operands.Should().HaveCount(2);
        phi.Operands.Select(operand => operand.Version).Should().BeEquivalentTo(
            [ssa.Definitions(trueWrite).Single().Version, ssa.Definitions(falseWrite).Single().Version]);
        ssa.Info(phi.Result).Kind.Should().Be(IrSsaDefinitionKind.Phi);
        IrVerifier.VerifySsa(method, ssa);
    }

    [Fact]
    public void Loop_HeaderPhiTakesTheBackEdgeVersion() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock header = Block(1);
        IrBlock exit = Block(2);
        IrStatement init = Writes(instruction, Ecx);
        IrStatement decrement = new(new CommentNode("dec"), instruction,
            new IrEffects(Ecx, IrValues.None, Ecx, IrTokens.None, IrTokens.None, false, false));
        entry.Statements.Add(init);
        header.Statements.Add(decrement);
        entry.Terminator = new IrJump(instruction, new IrEdge(header, false));
        header.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
            new IrEdge(exit, false), new IrEdge(header, true));
        exit.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, header, exit);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        IrSsaPhi phi = ssa.PhisOf(header).Should().ContainSingle(p => p.Value == IrSsaValue.Ecx).Subject;
        phi.Operands.Should().HaveCount(2);
        phi.Operands.Should().Contain(operand => operand.Predecessor == header
            && operand.Version == ssa.VersionAfter(header, header.Statements.Count, IrSsaValue.Ecx));
        IrVerifier.VerifySsa(method, ssa);
    }

    [Fact]
    public void InnerLoopHeader_GetsPhisForRegistersAndMemory() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock header = Block(1);
        IrBlock exit = Block(2);
        IrStatement body = new(new CommentNode("movsb"), instruction,
            new IrEffects(Ecx | Esi | Edi | Ds | Es, IrValues.None, Ecx | Esi | Edi,
                IrTokens.Memory, IrTokens.Memory, false, false));
        header.Statements.Add(body);
        entry.Terminator = new IrJump(instruction, new IrEdge(header, true));
        header.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
            new IrEdge(header, true), new IrEdge(exit, false));
        exit.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, header, exit);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.PhisOf(header).Select(phi => phi.Value).Should().BeEquivalentTo(
            [IrSsaValue.Ecx, IrSsaValue.Esi, IrSsaValue.Edi, IrSsaValue.Memory]);
        IrVerifier.VerifySsa(method, ssa);
    }

    [Fact]
    public void MemoryToken_StoreBetweenLoadsOrdersThem() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement firstLoad = Reads(instruction, IrTokens.Memory);
        IrStatement store = new(new CommentNode("store"), instruction,
            new IrEffects(IrValues.None, IrValues.None, IrValues.None, IrTokens.None, IrTokens.Memory, false, false));
        IrStatement secondLoad = Reads(instruction, IrTokens.Memory);
        block.Statements.Add(firstLoad);
        block.Statements.Add(store);
        block.Statements.Add(secondLoad);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        int firstVersion = ssa.VersionUsed(firstLoad, IrSsaValue.Memory);
        int secondVersion = ssa.VersionUsed(secondLoad, IrSsaValue.Memory);
        secondVersion.Should().NotBe(firstVersion);
        secondVersion.Should().Be(ssa.Definitions(store).Single().Version);
        ssa.Info(secondVersion).Kind.Should().Be(IrSsaDefinitionKind.PartialDefinition);
    }

    [Fact]
    public void IoToken_OutBetweenInsOrdersThem() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement firstIn = Reads(instruction, IrTokens.Io);
        IrStatement ioOut = new(new CommentNode("out"), instruction,
            new IrEffects(IrValues.None, IrValues.None, IrValues.None, IrTokens.None, IrTokens.Io, false, false));
        IrStatement secondIn = Reads(instruction, IrTokens.Io);
        block.Statements.Add(firstIn);
        block.Statements.Add(ioOut);
        block.Statements.Add(secondIn);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.VersionUsed(secondIn, IrSsaValue.Io).Should().NotBe(ssa.VersionUsed(firstIn, IrSsaValue.Io));
    }

    [Fact]
    public void EventCheck_DefinesMemoryButNoRegister() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement check = new(new CheckExternalEventsNode(instruction.Address), instruction,
            new IrEffects(IrValues.None, IrValues.None, IrValues.None, IrTokens.Memory, IrTokens.Memory, false, true));
        block.Statements.Add(check);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.Definitions(check).Select(definition => definition.Value).Should()
            .BeEquivalentTo([IrSsaValue.Memory]);
    }

    [Fact]
    public void Fault_StatementUsesEveryValue() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement divide = new(new CommentNode("div"), instruction,
            new IrEffects(Eax | (IrValues)(0xFUL << 8), IrValues.None, Eax | (IrValues)(0xFUL << 8),
                IrTokens.None, IrTokens.None, true, false));
        block.Statements.Add(divide);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()) {
            if (value == IrSsaValue.Count) {
                continue;
            }
            ssa.VersionUsed(divide, value).Should().NotBe(-1, $"{value} is used by a faulting statement");
        }
    }

    [Fact]
    public void Exit_UsesEveryValue() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrExit exit = new(instruction, ReturnValue());
        block.Terminator = exit;
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()) {
            if (value == IrSsaValue.Count) {
                continue;
            }
            int before = ssa.VersionBefore(block, block.Statements.Count, value);
            ssa.Uses(before).Should().Contain(use => use.Terminator == exit,
                $"{value} is used by the exit");
        }
    }

    [Fact]
    public void DeadPartialFlagDefinition_LeavesTheEarlierFlagWriteDead() {
        // Arrange: a flag write then a partial flag definition like a shift by CL whose flags feed
        // nothing - the shift's read of the previous flags is dead with it.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement setFlag = Writes(instruction, Carry);
        IrStatement shift = new(new CommentNode("shl"), instruction,
            new IrEffects(Ecx, IrValues.None, IrValues.StatusFlags, IrTokens.None, IrTokens.None,
                false, false));
        block.Statements.Add(setFlag);
        block.Statements.Add(shift);
        block.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.IsLive(ssa.Definitions(setFlag).Single().Version).Should().BeFalse();
        ssa.IsLive(ssa.Definitions(shift).Single(definition => definition.Value == IrSsaValue.Carry)
            .Version).Should().BeFalse();
    }

    [Fact]
    public void FailAsUntestedExit_KeepsTheFlagWritesBeforeItDead() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement setFlag = Writes(instruction, Carry);
        block.Statements.Add(setFlag);
        block.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.IsLive(ssa.Definitions(setFlag).Single().Version).Should().BeFalse();
    }

    [Fact]
    public void ReturnExit_KeepsTheFlagWritesBeforeItLive() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement setFlag = Writes(instruction, Carry);
        block.Statements.Add(setFlag);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        ssa.IsLive(ssa.Definitions(setFlag).Single().Version).Should().BeTrue();
    }

    [Fact]
    public void SecondEntry_MergesIntoAPhiWithAnEntryOperand() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock firstBlock = Block(0);
        IrBlock secondBlock = Block(1);
        IrStatement write = Writes(first, Eax);
        firstBlock.Statements.Add(write);
        firstBlock.Terminator = new IrJump(first, new IrEdge(secondBlock, false));
        secondBlock.Terminator = new IrExit(second, ReturnValue());
        IrMethod method = Method([Entry(first, firstBlock), Entry(second, secondBlock)],
            [first, second], firstBlock, secondBlock);

        // Act
        IrSsa ssa = IrSsa.Build(method);

        // Assert
        IrSsaPhi phi = ssa.PhisOf(secondBlock).Should().ContainSingle(p => p.Value == IrSsaValue.Eax).Subject;
        phi.HasEntryOperand.Should().BeTrue();
        phi.Operands.Should().ContainSingle();
        phi.Operands[0].Predecessor.Should().BeSameAs(firstBlock);
        phi.Operands[0].Version.Should().Be(ssa.Definitions(write).Single().Version);
        IrVerifier.VerifySsa(method, ssa);
    }

    public static TheoryData<int, int, int, bool> SameVersionQueries => new() {
        { 1, 1, 2, true },
        { 0, 0, 1, false }
    };

    [Theory]
    [MemberData(nameof(SameVersionQueries))]
    public void SameVersions_ReportsWhetherADefinitionLiesBetween(int valueIndex, int from, int to,
        bool expected) {
        IrValues value = valueIndex == 0 ? Eax : Ebx;
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(Writes(instruction, Eax));
        block.Statements.Add(new IrStatement(new CommentNode("s"), instruction, IrEffects.None));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        bool same = ssa.SameVersions(value, IrTokens.None, (block, from), (block, to));

        // Assert
        same.Should().Be(expected);
    }

    [Fact]
    public void VerifySsa_FailsWhenADefinitionDoesNotDominateItsUse() {
        // Arrange: the shared use block has a second entry in the broken method, so the writer no
        // longer dominates it.
        CfgInstruction instruction = Instruction(0);
        IrBlock writer = Block(0);
        IrBlock reader = Block(1);
        writer.Statements.Add(Writes(instruction, Eax));
        writer.Terminator = new IrJump(instruction, new IrEdge(reader, false));
        IrStatement use = Reads(instruction, Ax);
        reader.Statements.Add(use);
        reader.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod whole = Method([Entry(instruction, writer)], [instruction], writer, reader);
        IrSsa ssa = IrSsa.Build(whole);
        IrMethod withSecondEntry = Method([Entry(instruction, writer), Entry(instruction, reader)],
            [instruction], writer, reader);

        // Act
        Action act = () => IrVerifier.VerifySsa(withSecondEntry, ssa);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*ssa*");
    }

    [Fact]
    public void Temporaries_ReportsDeclarationAndReads() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrBlock other = Block(1);
        IrStatement declaration = new(
            new VariableDeclarationNode(DataType.UINT16, "tmp", new ConstantNode(DataType.UINT16, 1)),
            instruction, IrEffects.None);
        IrStatement read = new(new VariableReferenceNode(DataType.UINT16, "tmp"),
            instruction, IrEffects.None);
        block.Statements.Add(declaration);
        block.Terminator = new IrJump(instruction, new IrEdge(other, false));
        other.Statements.Add(read);
        other.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block, other);

        // Act
        IrTemporaries temporaries = IrTemporaries.Build(method);

        // Assert
        temporaries.DeclarationOf("tmp").Should().Be((block, 0));
        temporaries.ReadsOf("tmp").Should().BeEquivalentTo([(other, 0)]);
        temporaries.DeclarationOf("absent").Should().BeNull();
    }

    [Fact]
    public void Replace_MovesTheRecordedVersionsToTheReplacement() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        block.Statements.Add(mov);
        block.Statements.Add(Reads(instruction, Ax));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        int version = ssa.Definitions(mov).Single().Version;
        IrStatement replacement = Writes(instruction, Eax);

        // Act
        ssa.Replace(mov, replacement);
        block.Statements[0] = replacement;

        // Assert
        ssa.Definitions(replacement).Single().Version.Should().Be(version);
        ssa.VersionBefore(block, 1, IrSsaValue.Eax).Should().Be(version);
    }

    [Fact]
    public void Replace_AttachesADefinitionOnlyToTheReplacementThatWritesIt() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        block.Statements.Add(mov);
        block.Statements.Add(Reads(instruction, Ax));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        int old = ssa.VersionBefore(block, 0, IrSsaValue.Eax);
        int version = ssa.Definitions(mov).Single().Version;
        IrStatement nonWriter = new(new CommentNode("n"), instruction, IrEffects.None);
        IrStatement writer = Writes(instruction, Eax);
        block.Statements.RemoveAt(0);
        block.Statements.Insert(0, nonWriter);
        block.Statements.Insert(1, writer);

        // Act
        ssa.Replace(mov, [nonWriter, writer]);

        // Assert
        ssa.VersionBefore(block, 1, IrSsaValue.Eax).Should().Be(old);
        ssa.VersionBefore(block, 2, IrSsaValue.Eax).Should().Be(version);
        ssa.Definitions(nonWriter).Should().BeEmpty();
        ssa.Definitions(writer).Should().ContainSingle()
            .Which.Version.Should().Be(version);
    }

    [Fact]
    public void ReplaceTerminator_MovesTheRecordedSiteToTheReplacement() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrBlock target = Block(1);
        block.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(target, false), new IrEdge(target, false));
        target.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block, target);
        IrSsa ssa = IrSsa.Build(method);
        int carryVersion = ssa.VersionUsed(block.Terminator, IrSsaValue.Carry);
        IrBranch replacement = new(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(target, false), new IrEdge(target, false));

        // Act
        ssa.ReplaceTerminator(block.Terminator, replacement);
        block.Terminator = replacement;

        // Assert
        ssa.VersionUsed(replacement, IrSsaValue.Carry).Should().Be(carryVersion);
        carryVersion.Should().NotBe(-1);
    }

    [Fact]
    public void ReplaceTerminator_ThrowsWhenTheOriginalHasNoSite() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrExit notInMethod = new(instruction, ReturnValue());

        // Act
        Action act = () => ssa.ReplaceTerminator(notInMethod,
            new IrExit(instruction, ReturnValue()));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Replace_ThrowsWhenTheReplacementDefinesAValueTheOriginalDidNot() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        block.Statements.Add(mov);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        Action act = () => ssa.Replace(mov, Writes(instruction, Ebx));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void VerifySsa_FailsWhenAReplacementReadsAValueTheOriginalDidNot() {
        // Arrange: the replacement keeps the original's recorded uses, which cannot cover a read
        // the original did not make.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        block.Statements.Add(mov);
        block.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrStatement reader = new(new CommentNode("r"), instruction,
            new IrEffects(Ebx, Eax, IrValues.None, IrTokens.None, IrTokens.None, false, false));
        block.Statements[0] = reader;
        ssa.Replace(mov, reader);

        // Act
        Action act = () => IrVerifier.VerifySsa(method, ssa);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*use record*");
    }

    [Fact]
    public void VerifySsaAfterPasses_AcceptsADeadDefinitionParkedOnANonDefiningReplacement() {
        // Arrange: a definition no replacement writes is parked on the last one; the version is
        // dead, so only the strict check may reject it.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        block.Statements.Add(mov);
        block.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrStatement plain = new(new CommentNode("n"), instruction, IrEffects.None);
        block.Statements[0] = plain;
        ssa.Replace(mov, plain);

        // Act
        Action strict = () => IrVerifier.VerifySsa(method, ssa);
        Action afterPasses = () => IrVerifier.VerifySsaAfterPasses(method, ssa);

        // Assert
        strict.Should().Throw<InvalidOperationException>()
            .WithMessage("*version of Eax changes where the effects define nothing*");
        afterPasses.Should().NotThrow();
    }

    [Fact]
    public void VersionBefore_ThrowsOnAnInsertedStatementThatDefinesAValue() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(Writes(instruction, Eax));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        block.Statements.Add(Writes(instruction, Eax));

        // Act
        Action act = () => ssa.VersionBefore(block, 2, IrSsaValue.Eax);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static IrStatement Writes(ICfgNode origin, IrValues mask) =>
        new(new CommentNode("w"), origin,
            new IrEffects(IrValues.None, mask, IrValues.None, IrTokens.None, IrTokens.None, false, false));

    private static IrStatement Reads(ICfgNode origin, IrTokens tokens) =>
        new(new CommentNode("r"), origin,
            new IrEffects(IrValues.None, IrValues.None, IrValues.None, tokens, IrTokens.None, false, false));

    private static IrStatement Reads(ICfgNode origin, IrValues values) =>
        new(new CommentNode("r"), origin,
            new IrEffects(values, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));

    private static FailAsUntestedNode Fail() => new([UntestedMessagePart.Literal("untested")]);
}
