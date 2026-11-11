namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The register-local promotion of straight-line webs: a single-block register web prints as a C#
/// local, with a write-back to the register before the first use the local cannot serve.
/// </summary>
public sealed class RegisterLocalPromotionTest {
    [Fact]
    public void WebDyingAfterItsLastExplicitUse_GetsNoWriteBack() {
        // Arrange: mov ax,[0x200] / mov cx,ax / mov eax,0 fully redefines EAX / ret.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction movEax = Parse(helper, 0x0005, 0x66, 0xB8, 0x00, 0x00, 0x00, 0x00);
        CfgInstruction ret = Parse(helper, 0x000B, 0xC3);
        Chain(load, movCx, movEax, ret);
        CfgBlock block = Block(1, load, movCx, movEax, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("CX = ax;");
        program.SourceText.Should().NotContain("AX = ax;");
        AssertCompiles(program);
    }

    [Fact]
    public void UseInAnotherBlock_PromotesWithNumberedName() {
        // Arrange: mov ax,[0x200] / jz over the use / mov cx,ax / mov dx,ax / ret  -  the use sits
        // in a second block and the phi joins the web across it.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction jz = Parse(helper, 0x0003, 0x74, 0x01);
        CfgInstruction nop = Parse(helper, 0x0005, 0x90);
        CfgInstruction movCx = Parse(helper, 0x0006, 0x89, 0xC1);
        CfgInstruction movDx = Parse(helper, 0x0008, 0x89, 0xC2);
        CfgInstruction ret = Parse(helper, 0x000A, 0xC3);
        Chain(load, jz, nop, movCx, movDx, ret);
        jz.Successors.Add(movCx);
        jz.SuccessorsPerType[InstructionSuccessorType.Normal] =
            new HashSet<ICfgNode> { nop, movCx };
        CfgBlock[] blocks = [Block(1, load, jz), Block(2, nop), Block(3, movCx, movDx, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().Contain("ushort ax1 = 0;");
        program.SourceText.Should().Contain("CX = ax1;");
        program.SourceText.Should().Contain("AX = ax1;");
        AssertCompiles(program);
    }

    [Fact]
    public void InterruptCallAfterLastExplicitUse_GetsWriteBackBeforeTheCall() {
        // Arrange: mov ax,[0x200] / mov bx,ax / mov dx,ax / int 60h / ret  -  the interrupt reads
        // every register through its helper statement in the same block.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movBx = Parse(helper, 0x0003, 0x89, 0xC3);
        CfgInstruction movDx = Parse(helper, 0x0005, 0x89, 0xC2);
        CfgInstruction interrupt = Parse(helper, 0x0007, 0xCD, 0x60);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        CfgInstruction nop = Parse(helper, 0x000A, 0x90);
        CfgInstruction endRet = Parse(helper, 0x000B, 0xC3);
        Chain(load, movBx, movDx, interrupt, ret);
        interrupt.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode>();
        interrupt.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { ret };
        Chain(nop, endRet);
        CfgBlock main = Block(1, load, movBx, movDx, interrupt, ret);
        CfgBlock second = Block(2, nop, endRet);
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(main),
            new CfgCodePartitionEntry { Node = nop, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, main, second);

        // Assert
        program.SourceText.Should().Contain("BX = ax;");
        program.SourceText.Should().Contain("AX = ax;");
        AssertCompiles(program);
    }

    [Fact]
    public void ByteReadOfTheLocalRegister_IsImplicit() {
        // Arrange: mov ax,[0x300] / mov cx,ax / mov dx,ax / mov [0x200],al  -  the AL read cannot
        // print the ax local.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x03);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction movDx = Parse(helper, 0x0005, 0x89, 0xC2);
        CfgInstruction store = Parse(helper, 0x0007, 0xA2, 0x00, 0x02);
        CfgInstruction ret = Parse(helper, 0x000A, 0xC3);
        CfgInstruction nop = Parse(helper, 0x000B, 0x90);
        CfgInstruction endRet = Parse(helper, 0x000C, 0xC3);
        Chain(load, movCx, movDx, store, ret);
        Chain(nop, endRet);
        CfgBlock main = Block(1, load, movCx, movDx, store, ret);
        CfgBlock second = Block(2, nop, endRet);
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(main),
            new CfgCodePartitionEntry { Node = nop, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, main, second);

        // Assert
        program.SourceText.Should().Contain("CX = ax;");
        program.SourceText.Should().Contain("AX = ax;");
        AssertCompiles(program);
    }

    [Fact]
    public void FaultSiteInLiveRange_IsImplicit() {
        // Arrange: mov ax,[0x200] / mov cx,ax / mov dx,ax / div cl (with a CPU-fault edge) / ret.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction movDx = Parse(helper, 0x0005, 0x89, 0xC2);
        CfgInstruction div = Parse(helper, 0x0007, 0xF6, 0xF1);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        CfgInstruction nop = Parse(helper, 0x000A, 0x90);
        CfgInstruction endRet = Parse(helper, 0x000B, 0xC3);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0xC3);
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> { faultHandler };
        Chain(load, movCx, movDx, div, ret);
        Chain(nop, endRet);
        CfgBlock main = Block(1, load, movCx, movDx, div, ret);
        CfgBlock second = Block(2, nop, endRet);
        CfgBlock handlerBlock = Block(3, faultHandler);
        CfgCodePartition mainPartition = new() {
            Id = 1, Kind = CfgCodePartitionKind.Observed, Name = "main",
            Blocks = [main, second],
            Entries = [
                .. SingleEntry(main),
                new CfgCodePartitionEntry { Node = nop, Kind = CfgCodePartitionEntryKind.FunctionEntry }
            ]
        };
        CfgCodePartition handler = Partition(2, handlerBlock);
        CfgCodePartitionTransfer faultTransfer = new() {
            FromPartition = mainPartition, ToPartition = handler, FromNode = div,
            TargetNode = faultHandler, Kind = CfgCodePartitionTransferKind.CpuFault
        };
        GeneratedCSharpProgram program = GenerateProgram([mainPartition, handler], [faultTransfer]);

        // Assert: the faulting div reads every register; the write-back precedes it.
        program.SourceText.Should().Contain("AX = ax;");
        AssertCompiles(program);
    }

    [Fact]
    public void DsUsedAsPointerSegment_IsImplicit() {
        // Arrange: mov ds,[0x200] / mov cx,ds / mov dx,ds / mov si,ds / mov bx,[0x100]  -  the
        // pointer segment keeps DS.
        TestInstructionHelper helper = new();
        CfgInstruction loadDs = Parse(helper, 0x0000, 0x8E, 0x1E, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0004, 0x8C, 0xD9);
        CfgInstruction movDx = Parse(helper, 0x0006, 0x8C, 0xDA);
        CfgInstruction movSi = Parse(helper, 0x0008, 0x8C, 0xDE);
        CfgInstruction load = Parse(helper, 0x000A, 0x8B, 0x1E, 0x00, 0x01);
        CfgInstruction ret = Parse(helper, 0x000E, 0xC3);
        CfgInstruction nop = Parse(helper, 0x000F, 0x90);
        CfgInstruction endRet = Parse(helper, 0x0010, 0xC3);
        Chain(loadDs, movCx, movDx, movSi, load, ret);
        Chain(nop, endRet);
        CfgBlock main = Block(1, loadDs, movCx, movDx, movSi, load, ret);
        CfgBlock second = Block(2, nop, endRet);
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(main),
            new CfgCodePartitionEntry { Node = nop, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, main, second);

        // Assert
        program.SourceText.Should().Contain("CX = ds;");
        program.SourceText.Should().Contain("DS = ds;");
        AssertCompiles(program);
    }

    [Fact]
    public void MidBlockEntryAfterTheDefinition_Refuses() {
        // Arrange: a second method entry lands on the instruction using the web.
        TestInstructionHelper helper = new();
        CfgInstruction movAx = Parse(helper, 0x0000, 0xB8, 0x01, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction ret = Parse(helper, 0x0005, 0xC3);
        Chain(movAx, movCx, ret);
        CfgBlock block = Block(1, movAx, movCx, ret);
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(block),
            new CfgCodePartitionEntry { Node = movCx, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, block);

        // Assert
        program.SourceText.Should().Contain("CX = AX;");
        AssertCompiles(program);
    }

    [Fact]
    public void LocalNameEqualToAReservedName_Refuses() {
        // Arrange: the straight-line web of the first test, planned with "ax" reserved.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction movEax = Parse(helper, 0x0005, 0x66, 0xB8, 0x00, 0x00, 0x00, 0x00);
        CfgInstruction ret = Parse(helper, 0x000B, 0xC3);
        Chain(load, movCx, movEax, ret);
        IrMethod method = Lift(Block(1, load, movCx, movEax, ret));
        IrSsa ssa = IrPassPipeline.RunAll(method, false, false, new GenerationStepTimer()).Ssa;

        // Act
        RegisterLocalPlan plan = RegisterLocalPromotion.Plan(method, ssa,
            new HashSet<string> { "ax" }, new HashSet<IVisitableAstNode>(), (_, _) => false);

        // Assert
        plan.Declarations.Should().NotContain(declaration => declaration.VariableName == "ax");
        plan.Reads.Values.Should().NotContain(read => read.Text == "ax");
    }

    [Fact]
    public void SequentialAxWebsInOneBlock_ShareOneDeclaration() {
        // Arrange: mov ax,[0x200] / mov cx,ax / mov eax,0 / mov ax,[0x202] / mov dx,ax / mov bx,ax
        // / ret  -  the full EAX write ends the first web, so the second ax web's live range does
        // not overlap it.
        TestInstructionHelper helper = new();
        CfgInstruction load1 = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction movEax = Parse(helper, 0x0005, 0x66, 0xB8, 0x00, 0x00, 0x00, 0x00);
        CfgInstruction load2 = Parse(helper, 0x000B, 0xA1, 0x02, 0x02);
        CfgInstruction movDx = Parse(helper, 0x000E, 0x89, 0xC2);
        CfgInstruction movBx = Parse(helper, 0x0010, 0x89, 0xC3);
        CfgInstruction ret = Parse(helper, 0x0012, 0xC3);
        Chain(load1, movCx, movEax, load2, movDx, movBx, ret);
        CfgBlock block = Block(1, load1, movCx, movEax, load2, movDx, movBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: one declaration, both definitions print as the local.
        program.SourceText.Should().Contain("CX = ax;");
        program.SourceText.Should().Contain("DX = ax;");
        program.SourceText.Split('\n').Count(line => line.Contains("ushort ax;")).Should().Be(1);
        AssertCompiles(program);
    }

    [Fact]
    public void FullOverwriteAfterLastExplicitUse_KillsTheWebWithoutWriteBack() {
        // Arrange: mov ax,[0x200] / mov cx,ax / mov ax,5 / mov dx,ax / ret  -  the second definition
        // overwrites every byte of AX while reading none, so the first web dies with no write-back
        // and the second web may take the same local name.
        TestInstructionHelper helper = new();
        CfgInstruction load1 = Parse(helper, 0x0000, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction load2 = Parse(helper, 0x0005, 0xB8, 0x05, 0x00);
        CfgInstruction movDx = Parse(helper, 0x0008, 0x89, 0xC2);
        CfgInstruction ret = Parse(helper, 0x000A, 0xC3);
        Chain(load1, movCx, load2, movDx, ret);
        CfgBlock block = Block(1, load1, movCx, load2, movDx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: the killed web takes no write-back before AX = 5 (the mov dx,ax folds to DX = 5).
        program.SourceText.Should().Contain("CX = ax;");
        program.SourceText.Should().Contain("AX = 5;");
        program.SourceText.Should().NotContain("AX = ax;");
        AssertCompiles(program);
    }

    [Fact]
    public void IfElseDefinitionsJoinedByAPhi_PromoteWithNumberedName() {
        // Arrange: cmp ax,0 / jz / mov bx,1 / jmp join / mov bx,2 / join: mov cx,bx / ret  -  the two
        // arm definitions meet in a phi and the read sits after the join.
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x83, 0xF8, 0x00);
        CfgInstruction jz = Parse(helper, 0x0003, 0x74, 0x05);
        CfgInstruction movBx1 = Parse(helper, 0x0005, 0xBB, 0x01, 0x00);
        CfgInstruction jmp = Parse(helper, 0x0008, 0xEB, 0x03);
        CfgInstruction movBx2 = Parse(helper, 0x000A, 0xBB, 0x02, 0x00);
        CfgInstruction movCx = Parse(helper, 0x000D, 0x89, 0xD9);
        CfgInstruction ret = Parse(helper, 0x000F, 0xC3);
        Chain(cmp, jz);
        Branch(jz, movBx1, movBx2);
        Chain(movBx1, jmp);
        jmp.Successors.Add(movCx);
        jmp.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { movCx };
        Chain(movBx2, movCx);
        Chain(movCx, ret);
        CfgBlock[] blocks = [Block(1, cmp, jz), Block(2, movBx1, jmp), Block(3, movBx2),
            Block(4, movCx, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().Contain("ushort bx1 = 0;");
        program.SourceText.Should().Contain("bx1 = 1;");
        program.SourceText.Should().Contain("bx1 = 2;");
        program.SourceText.Should().Contain("CX = bx1;");
        AssertCompiles(program);
    }

    [Fact]
    public void BranchConditionUseInASingleBlock_IsExplicit() {
        // Arrange: mov cx,[0x200] / jcxz over a nop / each arm rewrites ECX and returns  -  the branch
        // condition is the only use of the version, an explicit use the local serves inside one
        // block.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x0E, 0x00, 0x02);
        CfgInstruction jcxz = Parse(helper, 0x0004, 0xE3, 0x08);
        CfgInstruction nop = Parse(helper, 0x0006, 0x90);
        CfgInstruction movEcx = Parse(helper, 0x0007, 0x66, 0xB9, 0x00, 0x00, 0x00, 0x00);
        CfgInstruction ret = Parse(helper, 0x000D, 0xC3);
        CfgInstruction movEcx2 = Parse(helper, 0x000E, 0x66, 0xB9, 0x00, 0x00, 0x00, 0x00);
        CfgInstruction ret2 = Parse(helper, 0x0014, 0xC3);
        Chain(load, jcxz);
        Branch(jcxz, nop, movEcx2);
        Chain(nop, movEcx, ret);
        Chain(movEcx2, ret2);
        CfgBlock[] blocks = [Block(1, load, jcxz), Block(2, nop, movEcx, ret), Block(3, movEcx2, ret2)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert: the condition prints the local and no write-back precedes the branch.
        program.SourceText.Should().Contain("ushort cx;");
        program.SourceText.Should().Contain("cx == 0");
        program.SourceText.Should().NotContain("CX = cx;");
        AssertCompiles(program);
    }

    [Fact]
    public void EventCheckThatMayWriteTheRegister_EndsTheWeb() {
        // Arrange: mov cx,[0x200] / L: nop / jcxz end / jmp L / end: ret  -  the loop head emits an
        // event check and a hardware handler may write CX, so the check defines the version the
        // loop branch reads: the web ends there and the register is printed.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x0E, 0x00, 0x02);
        CfgInstruction nop = Parse(helper, 0x0004, 0x90);
        CfgInstruction jcxz = Parse(helper, 0x0005, 0xE3, 0x02);
        CfgInstruction jmp = Parse(helper, 0x0007, 0xEB, 0xFB);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        Chain(load, nop);
        Chain(nop, jcxz);
        Branch(jcxz, jmp, ret);
        jmp.Successors.Add(nop);
        jmp.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };
        CfgBlock[] blocks = [Block(1, load), Block(2, nop, jcxz), Block(3, jmp), Block(4, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().NotContain("cx1");
        program.SourceText.Should().NotContain("ushort cx");
        AssertCompiles(program);
    }

    [Fact]
    public void WebJoinedByAPhiWithAnEntryOperand_Refuses() {
        // Arrange: mov ax,1 falls into a block that is also a method entry: the phi on AX at the
        // second block merges the definition with the entry version, so the web may not promote.
        TestInstructionHelper helper = new();
        CfgInstruction movAx = Parse(helper, 0x0000, 0xB8, 0x01, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0003, 0x89, 0xC1);
        CfgInstruction ret = Parse(helper, 0x0005, 0xC3);
        Chain(movAx, movCx, ret);
        CfgBlock[] blocks = [Block(1, movAx), Block(2, movCx, ret)];
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(blocks[0]),
            new CfgCodePartitionEntry { Node = movCx, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, blocks);

        // Assert
        program.SourceText.Should().Contain("CX = AX;");
        program.SourceText.Should().NotContain("ushort ax");
        AssertCompiles(program);
    }

    [Fact]
    public void PreservingCall_ContinuesTheWebWithAWriteBackBeforeTheCall() {
        // Arrange: mov bx,[0x200] / call leaf / mov cx,bx / ret  -  a hardware interrupt whose
        // handler is unknown makes the effect conservative: the leaf's entry event check may
        // write every register, so the call is not preserving and the web ends there.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x1E, 0x00, 0x02);
        CfgInstruction call = Parse(helper, 0x0004, 0xE8, 0x09, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0007, 0x89, 0xD9);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        CfgInstruction leafNop = Parse(helper, 0x0010, 0x90);
        CfgInstruction leafRet = Parse(helper, 0x0011, 0xC3);
        Chain(load, call, movCx, ret);
        call.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { leafNop };
        call.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { movCx };
        call.Successors.Add(leafNop);
        Chain(leafNop, leafRet);
        CfgBlock[] blocks = [Block(1, load, call, movCx, ret)];
        CfgCodePartition leaf = Partition(2, Block(2, leafNop, leafRet));
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), [leaf], blocks);

        // Assert
        program.SourceText.Should().NotContain("ushort bx1");
        program.SourceText.Should().Contain("BX = DsGlobals.Word0200;");
        program.SourceText.Should().Contain("CX = BX;");
        AssertCompiles(program);
    }

    [Fact]
    public void PreservingCallUnderAnEffectWritingNothing_ContinuesTheWebWithAWriteBackBeforeTheCall() {
        // Arrange: mov bx,[0x200] / call leaf / mov cx,bx / mov dx,bx / ret  -  the leaf neither
        // reads nor writes BX, so the web crosses the call; the write-back restores BX before it.
        // An empty hardware interrupt setup gives an effect that writes nothing: the leaf's entry
        // event check cannot clobber BX, so it stays preserving.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x1E, 0x00, 0x02);
        CfgInstruction call = Parse(helper, 0x0004, 0xE8, 0x0B, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0007, 0x89, 0xD9);
        CfgInstruction movDx = Parse(helper, 0x0009, 0x89, 0xDA);
        CfgInstruction ret = Parse(helper, 0x000B, 0xC3);
        CfgInstruction leafNop = Parse(helper, 0x0012, 0x90);
        CfgInstruction leafRet = Parse(helper, 0x0013, 0xC3);
        Chain(load, call, movCx, movDx, ret);
        call.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { leafNop };
        call.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { movCx };
        call.Successors.Add(leafNop);
        Chain(leafNop, leafRet);
        CfgBlock[] blocks = [Block(1, load, call, movCx, movDx, ret)];
        CfgCodePartition main = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "test",
            Blocks = blocks,
            Entries = SingleEntry(blocks[0])
        };
        CfgCodePartition leaf = Partition(2, Block(2, leafNop, leafRet));
        CfgPartitionedProgram partitioned = new() {
            Partitions = [main, leaf],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramEntryPoint = main.Entries[0].Address
            }
        };
        GeneratedCSharpProgram program =
            new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(partitioned);

        // Assert
        program.SourceText.Should().Contain("ushort bx1 = 0;");
        program.SourceText.Should().Contain("bx1 = DsGlobals.Word0200;");
        program.SourceText.Should().Contain("BX = bx1;");
        program.SourceText.Should().Contain("CX = bx1;");
        AssertCompiles(program);
    }

    [Fact]
    public void CallToACalleeThatChangesTheRegister_EndsTheWeb() {
        // Arrange: mov bx,[0x200] / call leaf / mov cx,bx / ret  -  the leaf rewrites BX, so the read
        // after the call is the callee's value, not the local's.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x1E, 0x00, 0x02);
        CfgInstruction call = Parse(helper, 0x0004, 0xE8, 0x09, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0007, 0x89, 0xD9);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        CfgInstruction leafMov = Parse(helper, 0x0010, 0xBB, 0x05, 0x00);
        CfgInstruction leafRet = Parse(helper, 0x0013, 0xC3);
        Chain(load, call, movCx, ret);
        call.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { leafMov };
        call.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { movCx };
        call.Successors.Add(leafMov);
        Chain(leafMov, leafRet);
        CfgBlock[] blocks = [Block(1, load, call, movCx, ret)];
        CfgCodePartition leaf = Partition(2, Block(2, leafMov, leafRet));
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), [leaf], blocks);

        // Assert
        program.SourceText.Should().Contain("CX = BX;");
        AssertCompiles(program);
    }

    [Fact]
    public void CallWithARedirectedContinuation_EndsTheWeb() {
        // Arrange: the preserving-call shape, but the call may also return elsewhere: the web ends
        // and the read after it is a register read.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x1E, 0x00, 0x02);
        CfgInstruction call = Parse(helper, 0x0004, 0xE8, 0x09, 0x00);
        CfgInstruction movCx = Parse(helper, 0x0007, 0x89, 0xD9);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        CfgInstruction leafNop = Parse(helper, 0x0010, 0x90);
        CfgInstruction leafRet = Parse(helper, 0x0011, 0xC3);
        CfgInstruction elsewhere = Parse(helper, 0x0020, 0xC3);
        Chain(load, call, movCx, ret);
        call.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { leafNop };
        call.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { movCx };
        call.SuccessorsPerType[InstructionSuccessorType.CallToRedirectedReturn] =
            new HashSet<ICfgNode> { elsewhere };
        call.Successors.Add(leafNop);
        call.Successors.Add(elsewhere);
        Chain(leafNop, leafRet);
        CfgBlock[] blocks = [Block(1, load, call, movCx, ret), Block(3, elsewhere)];
        CfgCodePartition leaf = Partition(2, Block(2, leafNop, leafRet));
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), [leaf], blocks);

        // Assert
        program.SourceText.Should().Contain("CX = BX;");
        AssertCompiles(program);
    }

    [Fact]
    public void DefinitionAndImplicitUseInOneInstruction_Refuses() {
        // Arrange: xadd bx,ax defines AX in the middle of the instruction; its use of the previous
        // version through the sum's AX node is also the new version's assignment target, so no
        // instance may print a local.
        TestInstructionHelper helper = new();
        CfgInstruction movAx = Parse(helper, 0x0000, 0xB8, 0x11, 0x11);
        CfgInstruction movBx = Parse(helper, 0x0003, 0xBB, 0x22, 0x22);
        CfgInstruction xadd = Parse(helper, 0x0006, 0x0F, 0xC1, 0xC3);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        Chain(movAx, movBx, xadd, ret);
        CfgBlock block = Block(1, movAx, movBx, xadd, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert: neither web of the instruction prints a local.
        program.SourceText.Should().NotContain("ushort ax;");
        program.SourceText.Should().NotContain("ushort bx;");
        AssertCompiles(program);
    }

    [Fact]
    public void WebInsideAPushPopRegion_EndsAtThePopWithoutWriteBack() {
        // Arrange: push ax / mov ax,[0x200] / mov [0x202],ax / pop ax / mov [0x204],ax / ret  -  the
        // web inside the region is promoted, the pop is a killing use of it (no write-back before
        // it) and a full plain definition, so the read after it is the popped value. A second entry
        // in a dead block keeps the method untyped so the `ax` name is free.
        TestInstructionHelper helper = new();
        CfgInstruction push = Parse(helper, 0x0000, 0x50);
        CfgInstruction load = Parse(helper, 0x0001, 0xA1, 0x00, 0x02);
        CfgInstruction store = Parse(helper, 0x0004, 0xA3, 0x02, 0x02);
        CfgInstruction pop = Parse(helper, 0x0007, 0x58);
        CfgInstruction storeAfter = Parse(helper, 0x0008, 0xA3, 0x04, 0x02);
        CfgInstruction ret = Parse(helper, 0x000B, 0xC3);
        CfgInstruction ret2 = Parse(helper, 0x0010, 0xC3);
        Chain(push, load, store, pop, storeAfter, ret);
        CfgBlock[] blocks = [Block(1, push, load, store, pop, storeAfter, ret), Block(2, ret2)];
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(blocks[0]),
            new CfgCodePartitionEntry { Node = ret2, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, blocks);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("ax = DsGlobals.Word0200;");
        program.SourceText.Should().Contain("DsGlobals.Word0202 = ax;");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("AX = ax;");
        program.SourceText.Should().Contain("DsGlobals.Word0204 = AX;");
        AssertCompiles(program);
    }

    [Fact]
    public void SegmentWebInsideAPushPopRegion_UsedAsAValueEndsAtThePop() {
        // Arrange: push es / mov ax,[0x200] / mov cx,ax / mov es,ax / mov dx,es / pop es /
        // mov [0x204],es / mov bx,es / ret  -  the region's ES web is read only as a value and
        // the pop ends it with no write-back before it. A second entry in a dead block keeps the
        // method untyped so `es` is free.
        TestInstructionHelper helper = new();
        CfgInstruction push = Parse(helper, 0x0000, 0x06);
        CfgInstruction load = Parse(helper, 0x0001, 0xA1, 0x00, 0x02);
        CfgInstruction movCx = Parse(helper, 0x0004, 0x89, 0xC1);
        CfgInstruction loadEs = Parse(helper, 0x0006, 0x8E, 0xC0);
        CfgInstruction readEs = Parse(helper, 0x0008, 0x8C, 0xC2);
        CfgInstruction pop = Parse(helper, 0x000A, 0x07);
        CfgInstruction storeAfter = Parse(helper, 0x000B, 0x8C, 0x06, 0x04, 0x02);
        CfgInstruction readEsAfter = Parse(helper, 0x000F, 0x8C, 0xC3);
        CfgInstruction ret = Parse(helper, 0x0011, 0xC3);
        CfgInstruction ret2 = Parse(helper, 0x0012, 0xC3);
        Chain(push, load, movCx, loadEs, readEs, pop, storeAfter, readEsAfter, ret);
        CfgBlock[] blocks = [Block(1, push, load, movCx, loadEs, readEs, pop, storeAfter,
            readEsAfter, ret), Block(2, ret2)];
        CfgCodePartitionEntry[] entries = [
            .. SingleEntry(blocks[0]),
            new CfgCodePartitionEntry { Node = ret2, Kind = CfgCodePartitionEntryKind.FunctionEntry }
        ];
        GeneratedCSharpProgram program = Generate(entries, blocks);

        // Assert: the region web promotes and is read as a value, the pop ends it with no
        // write-back before the pop, and the read after it is the popped value (which promotes
        // under the same name  -  `pop es` emits a memory statement plus the SP bump).
        program.SourceText.Should().Contain("es = ax;");
        program.SourceText.Should().Contain("DX = es;");
        program.SourceText.Should().NotContain("ES = es;\n              // F000:000A pop ES");
        program.SourceText.Should().Contain("es = Stack.Pop16();");
        program.SourceText.Should().Contain("DsGlobals.Word0204 = es;");
        AssertCompiles(program);
    }

    [Fact]
    public void SignedReadsOutvotingThePlainOnes_PromoteASignedLocal() {
        // Arrange: movsx ax,byte[0x200] / cmp ax,-5 / jl / jg / jge / mov cx,ax / ret  -  the
        // definition's right side converts from the signed type and three conditional jumps on the
        // same flags read the web under a signed conversion; the subtraction, the plain read and
        // the exit write-back vote unsigned: signed wins 4 to 3.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x0F, 0xBE, 0x06, 0x00, 0x02);
        CfgInstruction cmp = Parse(helper, 0x0005, 0x3D, 0xFB, 0xFF);
        CfgInstruction jl = Parse(helper, 0x0008, 0x7C, 0x08);
        CfgInstruction jg = Parse(helper, 0x000A, 0x7F, 0x06);
        CfgInstruction jge = Parse(helper, 0x000C, 0x7D, 0x04);
        CfgInstruction movCx = Parse(helper, 0x000E, 0x89, 0xC1);
        CfgInstruction ret = Parse(helper, 0x0010, 0xC3);
        Chain(load, cmp, jl);
        jl.Successors.Add(jg);
        jl.Successors.Add(ret);
        jl.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jg, ret };
        jg.Successors.Add(jge);
        jg.Successors.Add(ret);
        jg.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jge, ret };
        jge.Successors.Add(movCx);
        jge.Successors.Add(ret);
        jge.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { movCx, ret };
        Chain(movCx, ret);
        CfgBlock[] blocks = [Block(1, load, cmp, jl), Block(2, jg), Block(3, jge),
            Block(4, movCx), Block(5, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert: a signed local prints bare inside the signed comparisons and with a cast back at
        // the plain reads; the definition from a signed conversion keeps no round trip.
        program.SourceText.Should().Contain("short ax1 = 0;");
        program.SourceText.Should().Contain("ax1 = (short)(sbyte)");
        program.SourceText.Should().Contain("CX = (ushort)ax1;");
        program.SourceText.Should().NotContain("(ushort)((short)");
        program.SourceText.Should().NotContain("(short)((ushort)");
        AssertCompiles(program);
    }

    [Fact]
    public void SignedAndPlainReadsTied_StayUnsigned() {
        // Arrange: movsx ax,byte[0x200] / cmp ax,-5 / jl over a nop / ret  -  a signed definition
        // and one signed read against the subtraction's and the exit write-back's unsigned votes:
        // a tie, so the local keeps the register's type.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x0F, 0xBE, 0x06, 0x00, 0x02);
        CfgInstruction cmp = Parse(helper, 0x0005, 0x3D, 0xFB, 0xFF);
        CfgInstruction jl = Parse(helper, 0x0008, 0x7C, 0x01);
        CfgInstruction nop = Parse(helper, 0x000A, 0x90);
        CfgInstruction ret = Parse(helper, 0x000B, 0xC3);
        Chain(load, cmp, jl);
        jl.Successors.Add(nop);
        jl.Successors.Add(ret);
        jl.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop, ret };
        Chain(nop, ret);
        CfgBlock[] blocks = [Block(1, load, cmp, jl), Block(2, nop), Block(3, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert: the unsigned local is cast back to the signed type inside the comparison.
        program.SourceText.Should().Contain("ushort ax1 = 0;");
        program.SourceText.Should().Contain("(short)ax1");
        AssertCompiles(program);
    }

    [Fact]
    public void WriteBackOfASignedLocal_CastsBackToTheRegisterType() {
        // Arrange: movsx ax,byte[0x200] / cmp ax,-5 / jl / jg / jge / mov [0x204],al / ret  -  a
        // signed web whose low byte is read afterwards: the implicit use writes the register back
        // through a cast.
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x0F, 0xBE, 0x06, 0x00, 0x02);
        CfgInstruction cmp = Parse(helper, 0x0005, 0x3D, 0xFB, 0xFF);
        CfgInstruction jl = Parse(helper, 0x0008, 0x7C, 0x08);
        CfgInstruction jg = Parse(helper, 0x000A, 0x7F, 0x06);
        CfgInstruction jge = Parse(helper, 0x000C, 0x7D, 0x04);
        CfgInstruction store = Parse(helper, 0x000E, 0xA2, 0x04, 0x02);
        CfgInstruction ret = Parse(helper, 0x0011, 0xC3);
        Chain(load, cmp, jl);
        jl.Successors.Add(jg);
        jl.Successors.Add(ret);
        jl.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jg, ret };
        jg.Successors.Add(jge);
        jg.Successors.Add(ret);
        jg.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jge, ret };
        jge.Successors.Add(store);
        jge.Successors.Add(ret);
        jge.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { store, ret };
        Chain(store, ret);
        CfgBlock[] blocks = [Block(1, load, cmp, jl), Block(2, jg), Block(3, jge),
            Block(4, store), Block(5, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().Contain("short ax1 = 0;");
        program.SourceText.Should().Contain("AX = (ushort)ax1;");
        AssertCompiles(program);
    }

    [Fact]
    public void PartialWriteReadingTheLocal_GetsWriteBackBeforeIt() {
        // Arrange: mov dx,[si+4] / mov cx,dx / mov bx,dx / or dl,1 / mov [di+2],dx / ret  -  the or
        // reads DL and partially defines DX: the register must hold the local's value before the
        // write so the preserved high byte is dx's, not the entry's. Two explicit reads before it
        // keep the web promoted: the write-back plus the declaration must cost fewer register
        // accesses than the local saves. (A setcc select would fold `? 1 : 0` to a conversion in
        // the group-2 constants pass; `or dl,1` is a flag-writing ALU call and stays.)
        TestInstructionHelper helper = new();
        CfgInstruction load = Parse(helper, 0x0000, 0x8B, 0x54, 0x04);
        CfgInstruction copy = Parse(helper, 0x0003, 0x89, 0xD1);
        CfgInstruction copy2 = Parse(helper, 0x0005, 0x89, 0xD3);
        CfgInstruction or = Parse(helper, 0x0007, 0x80, 0xCA, 0x01);
        CfgInstruction store = Parse(helper, 0x000A, 0x89, 0x55, 0x02);
        CfgInstruction ret = Parse(helper, 0x000D, 0xC3);
        Chain(load, copy, copy2, or, store, ret);
        CfgBlock block = Block(1, load, copy, copy2, or, store, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("DX = dx;");
        AssertCompiles(program);
    }

    [Fact]
    public void PairWebWithAnUnmatchedDefinition_StaysScalar() {
        // Arrange: like the pairing test, but the else arm redefines only BX. The unmatched
        // BX definition keeps both webs on their scalar plans.
        TestInstructionHelper helper = new();
        CfgInstruction movBx = Parse(helper, 0x0000, 0x89, 0xC3);
        CfgInstruction movBp = Parse(helper, 0x0002, 0x89, 0xD5);
        CfgInstruction cmp = Parse(helper, 0x0004, 0x83, 0xF8, 0x00);
        CfgInstruction jz = Parse(helper, 0x0007, 0x74, 0x02);
        CfgInstruction jmp = Parse(helper, 0x0009, 0xEB, 0x02);
        CfgInstruction elseBx = Parse(helper, 0x000B, 0x89, 0xCB);
        CfgInstruction storeBx = Parse(helper, 0x000D, 0x89, 0x1E, 0x00, 0x02);
        CfgInstruction storeBp = Parse(helper, 0x0011, 0x89, 0x2E, 0x02, 0x02);
        CfgInstruction ret = Parse(helper, 0x0015, 0xC3);
        Chain(movBx, movBp, cmp, jz);
        Branch(jz, jmp, elseBx);
        Chain(jmp, storeBx);
        Chain(elseBx, storeBx);
        Chain(storeBx, storeBp, ret);
        CfgBlock[] blocks = [Block(1, movBx, movBp, cmp, jz), Block(2, jmp),
            Block(3, elseBx), Block(4, storeBx, storeBp, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().NotContain("uint value1");
        AssertCompiles(program);
    }
}
