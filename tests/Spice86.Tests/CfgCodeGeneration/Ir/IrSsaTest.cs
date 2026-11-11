namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
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
    private static readonly CpuValues Eax = CpuValues.Eax;
    private static readonly CpuValues Ecx = CpuValues.Ecx;
    private static readonly CpuValues Ebx = CpuValues.Ebx;
    private static readonly CpuValues Esi = CpuValues.Esi;
    private static readonly CpuValues Edi = CpuValues.Edi;
    private static readonly CpuValues Es = CpuValues.Es;
    private static readonly CpuValues Ds = CpuValues.Ds;
    private static readonly CpuValues Ax = CpuValues.Ax;
    private static readonly CpuValues Carry = CpuValues.Carry;

    [Fact]
    public void StraightLine_AddUsesMovVersionAndDefinesANewOne() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrStatement add = new(new CommentNode("add"), instruction,
            new IrEffects(Ax | CpuValues.Bx, Ax, CpuValues.None, IrTokens.None, IrTokens.None, false, false));
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.Append(block, add);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrStatement movAl = Writes(instruction, CpuValues.Al);
        IrEditor.WithoutSsa.Append(block, movEax);
        IrEditor.WithoutSsa.Append(block, movAl);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.Append(trueArm, trueWrite);
        IrEditor.WithoutSsa.Append(falseArm, falseWrite);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false)));
        IrEditor.WithoutSsa.SetTerminator(trueArm, new IrJump(instruction, new IrEdge(join, false)));
        IrEditor.WithoutSsa.SetTerminator(falseArm, new IrJump(instruction, new IrEdge(join, false)));
        IrEditor.WithoutSsa.SetTerminator(join, new IrExit(instruction, ReturnValue()));
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
            new IrEffects(Ecx, CpuValues.None, Ecx, IrTokens.None, IrTokens.None, false, false));
        IrEditor.WithoutSsa.Append(entry, init);
        IrEditor.WithoutSsa.Append(header, decrement);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrJump(instruction, new IrEdge(header, false)));
        IrEditor.WithoutSsa.SetTerminator(header, new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
            new IrEdge(exit, false), new IrEdge(header, true)));
        IrEditor.WithoutSsa.SetTerminator(exit, new IrExit(instruction, ReturnValue()));
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
            new IrEffects(Ecx | Esi | Edi | Ds | Es, CpuValues.None, Ecx | Esi | Edi,
                IrTokens.Memory, IrTokens.Memory, false, false));
        IrEditor.WithoutSsa.Append(header, body);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrJump(instruction, new IrEdge(header, true)));
        IrEditor.WithoutSsa.SetTerminator(header, new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
            new IrEdge(header, true), new IrEdge(exit, false)));
        IrEditor.WithoutSsa.SetTerminator(exit, new IrExit(instruction, ReturnValue()));
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
            new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.Memory, false, false));
        IrStatement secondLoad = Reads(instruction, IrTokens.Memory);
        IrEditor.WithoutSsa.Append(block, firstLoad);
        IrEditor.WithoutSsa.Append(block, store);
        IrEditor.WithoutSsa.Append(block, secondLoad);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
            new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.Io, false, false));
        IrStatement secondIn = Reads(instruction, IrTokens.Io);
        IrEditor.WithoutSsa.Append(block, firstIn);
        IrEditor.WithoutSsa.Append(block, ioOut);
        IrEditor.WithoutSsa.Append(block, secondIn);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrStatement check = new(new CheckExternalEventsNode(instruction.Address, HardwareInterruptEffectBuilder.Conservative), instruction,
            new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.Memory, IrTokens.Memory, false, true));
        IrEditor.WithoutSsa.Append(block, check);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
            new IrEffects(Eax | CpuValues.Edx, CpuValues.None, Eax | CpuValues.Edx,
                IrTokens.None, IrTokens.None, true, false));
        IrEditor.WithoutSsa.Append(block, divide);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.SetTerminator(block, exit);
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
            new IrEffects(Ecx, CpuValues.None, CpuValues.StatusFlags, IrTokens.None, IrTokens.None,
                false, false));
        IrEditor.WithoutSsa.Append(block, setFlag);
        IrEditor.WithoutSsa.Append(block, shift);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, Fail()));
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
        IrEditor.WithoutSsa.Append(block, setFlag);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, Fail()));
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
        IrEditor.WithoutSsa.Append(block, setFlag);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.Append(firstBlock, write);
        IrEditor.WithoutSsa.SetTerminator(firstBlock, new IrJump(first, new IrEdge(secondBlock, false)));
        IrEditor.WithoutSsa.SetTerminator(secondBlock, new IrExit(second, ReturnValue()));
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
        CpuValues value = valueIndex == 0 ? Eax : Ebx;
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrEditor.WithoutSsa.Append(block, Writes(instruction, Eax));
        IrEditor.WithoutSsa.Append(block, new IrStatement(new CommentNode("s"), instruction, IrEffects.None));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.Append(writer, Writes(instruction, Eax));
        IrEditor.WithoutSsa.SetTerminator(writer, new IrJump(instruction, new IrEdge(reader, false)));
        IrStatement use = Reads(instruction, Ax);
        IrEditor.WithoutSsa.Append(reader, use);
        IrEditor.WithoutSsa.SetTerminator(reader, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.Append(block, declaration);
        IrEditor.WithoutSsa.SetTerminator(block, new IrJump(instruction, new IrEdge(other, false)));
        IrEditor.WithoutSsa.Append(other, read);
        IrEditor.WithoutSsa.SetTerminator(other, new IrExit(instruction, ReturnValue()));
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
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.Append(block, Reads(instruction, Ax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        int version = ssa.Definitions(mov).Single().Version;
        IrStatement replacement = Writes(instruction, Eax);

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, replacement);

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
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.Append(block, Reads(instruction, Ax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        int old = ssa.VersionBefore(block, 0, IrSsaValue.Eax);
        int version = ssa.Definitions(mov).Single().Version;
        IrStatement nonWriter = new(new CommentNode("n"), instruction, IrEffects.None);
        IrStatement writer = Writes(instruction, Eax);

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, [nonWriter, writer]);

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
        IrEditor.WithoutSsa.SetTerminator(block, new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(target, false), new IrEdge(target, false)));
        IrEditor.WithoutSsa.SetTerminator(target, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block, target);
        IrSsa ssa = IrSsa.Build(method);
        int carryVersion = ssa.VersionUsed(block.Terminator, IrSsaValue.Carry);
        IrBranch replacement = new(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(target, false), new IrEdge(target, false));

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).SetTerminator(block, replacement);

        // Assert
        ssa.VersionUsed(replacement, IrSsaValue.Carry).Should().Be(carryVersion);
        carryVersion.Should().NotBe(-1);
    }

    [Fact]
    public void VersionUsed_IgnoresASiteTheSsaDidNotRecord() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrExit notInMethod = new(instruction, ReturnValue());

        // Act / Assert
        ssa.VersionUsed(notInMethod, IrSsaValue.Eax).Should().Be(-1);
        ssa.UsesBySite.ContainsKey(notInMethod).Should().BeFalse();
    }

    [Fact]
    public void Replace_MarksTheSsaStaleWhenTheReplacementDefinesAValueTheOriginalDidNot() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, Writes(instruction, Ebx));

        // Assert
        ssa.IsStale.Should().BeTrue();
        Action act = () => ssa.VersionBefore(block, 0, IrSsaValue.Eax);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Replace_RecordsTheReplacementReadsDerivedFromItsEffects() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, Fail()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrStatement reader = new(new CommentNode("r"), instruction,
            new IrEffects(Ebx, Eax, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, reader);

        // Assert
        IrVerifier.VerifySsa(method, ssa);
        IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
        ssa.UsesBySite[reader].Should().Contain(use => use.Value == IrSsaValue.Ebx);
    }

    [Fact]
    public void Replace_DropsADeadDefinitionNoReplacementWrites() {
        // Arrange: a definition no replacement writes is dropped when the version has no use left.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, Fail()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrStatement plain = new(new CommentNode("n"), instruction, IrEffects.None);

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, plain);

        // Assert
        ssa.IsStale.Should().BeFalse();
        IrVerifier.VerifySsa(method, ssa);
        IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
        ssa.Definitions(plain).Should().BeEmpty();
    }

    [Fact]
    public void VersionBefore_ThrowsOnAnInsertedStatementThatDefinesAValue() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrEditor.WithoutSsa.Append(block, Writes(instruction, Eax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        new IrEditor(ssa, new GenerationStepTimer()).Append(block, Writes(instruction, Eax));

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

    private static IrStatement Writes(ICfgNode origin, CpuValues mask) =>
        new(new CommentNode("w"), origin,
            new IrEffects(CpuValues.None, mask, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

    private static IrStatement Reads(ICfgNode origin, IrTokens tokens) =>
        new(new CommentNode("r"), origin,
            new IrEffects(CpuValues.None, CpuValues.None, CpuValues.None, tokens, IrTokens.None, false, false));

    private static IrStatement Reads(ICfgNode origin, CpuValues values) =>
        new(new CommentNode("r"), origin,
            new IrEffects(values, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));

    private static FailAsUntestedNode Fail() => new([UntestedMessagePart.Literal("untested")]);
}
