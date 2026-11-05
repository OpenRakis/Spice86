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
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The save / restore pair removal: a <c>push R</c> ... <c>pop R</c> of one register prints as a
/// save local only when nothing can observe the removed slot, and every refused shape keeps the
/// <c>Stack.Push16</c> / <c>Stack.Pop16</c> lines.
/// </summary>
public sealed class SaveRestorePairTest {
    [Fact]
    public void PairFollowedByACoveringPush_BecomesASavedLocal() {
        // Arrange: push ax / mov cx,ax / pop ax / push bx / pop bx / ret  -  the push bx lands on
        // the abandoned slot, so nothing can read it; the bx pair stays because ret follows.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popAx = Parse(helper, 0x0003, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0004, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0005, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, movCx, popAx, pushBx, popBx, ret);
        CfgBlock block = Block(1, pushAx, movCx, popAx, pushBx, popBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("ushort savedAx_0000 = 0;");
        program.SourceText.Should().Contain("savedAx_0000 = AX;");
        program.SourceText.Should().Contain("AX = savedAx_0000;");
        program.SourceText.Should().Contain("Stack.Push16(BX);");
        program.SourceText.Should().Contain("BX = Stack.Pop16();");
        AssertCompiles(program);
    }

    [Fact]
    public void AdjacentPushPopDifferentRegisters_PrintsDirectly() {
        // Arrange: push ax / pop bx / push cx / pop cx / ret  -  nothing sits between the push and
        // the pop, so the pop's value prints as the pushed expression; push cx covers the slot.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction popBx = Parse(helper, 0x0001, 0x5B);
        CfgInstruction pushCx = Parse(helper, 0x0002, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0003, 0x59);
        CfgInstruction ret = Parse(helper, 0x0004, 0xC3);
        Chain(pushAx, popBx, pushCx, popCx, ret);
        CfgBlock block = Block(1, pushAx, popBx, pushCx, popCx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert  -  the trailing cx pair keeps its stack forms (ret follows it, so its own
        // abandoned word is never covered), while ax / bx print directly.
        program.SourceText.Should().Contain("BX = AX;");
        program.SourceText.Should().NotContain("Stack.Push16(AX);");
        program.SourceText.Should().NotContain("saved");
        AssertCompiles(program);
    }

    [Fact]
    public void PushPopDifferentRegistersNonAdjacent_PrintsSavedLocal() {
        // Arrange: push ax / mov cx,ax / pop bx / push dx / pop dx / ret  -  a statement between
        // them asks for the save local even though the registers differ.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popBx = Parse(helper, 0x0003, 0x5B);
        CfgInstruction pushDx = Parse(helper, 0x0004, 0x52);
        CfgInstruction popDx = Parse(helper, 0x0005, 0x5A);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, movCx, popBx, pushDx, popDx, ret);
        CfgBlock block = Block(1, pushAx, movCx, popBx, pushDx, popDx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("ushort savedBx_0000 = 0;");
        program.SourceText.Should().Contain("savedBx_0000 = AX;");
        program.SourceText.Should().Contain("BX = savedBx_0000;");
        AssertCompiles(program);
    }

    [Fact]
    public void PushPopWithAChangedPushedValueBetween_UsesTheSavedLocal() {
        // Arrange: push ax / mov ax,0 / pop bx / push cx / pop cx / ret  -  the pushed register
        // changes between the two, so the pop cannot read it directly: bx keeps the saved value.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movAx = Parse(helper, 0x0001, 0xB8, 0x00, 0x00);
        CfgInstruction popBx = Parse(helper, 0x0004, 0x5B);
        CfgInstruction pushCx = Parse(helper, 0x0005, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0006, 0x59);
        CfgInstruction ret = Parse(helper, 0x0007, 0xC3);
        Chain(pushAx, movAx, popBx, pushCx, popCx, ret);
        CfgBlock block = Block(1, pushAx, movAx, popBx, pushCx, popCx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("savedBx_0000 = AX;");
        program.SourceText.Should().Contain("BX = savedBx_0000;");
        program.SourceText.Should().NotContain("BX = AX;");
        AssertCompiles(program);
    }

    [Fact]
    public void AdjacentPushSegmentPopSegment_PrintsDirectlyAndDropsTheAdvance() {
        // Arrange: push es / pop ds / push cx / pop cx / ret  -  the segment pop's own SP advance
        // leaves with the push, and push cx covers the slot.
        TestInstructionHelper helper = new();
        CfgInstruction pushEs = Parse(helper, 0x0000, 0x06);
        CfgInstruction popDs = Parse(helper, 0x0001, 0x1F);
        CfgInstruction pushCx = Parse(helper, 0x0002, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0003, 0x59);
        CfgInstruction ret = Parse(helper, 0x0004, 0xC3);
        Chain(pushEs, popDs, pushCx, popCx, ret);
        CfgBlock block = Block(1, pushEs, popDs, pushCx, popCx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("DS = ES;");
        program.SourceText.Should().NotContain("UInt16[SS, SP]");
        program.SourceText.Should().NotContain("SP += 2");
        AssertCompiles(program);
    }

    [Fact]
    public void PushSegmentPopSegmentNonAdjacent_PrintsSavedLocalAndDropsTheAdvance() {
        // Arrange: push es / mov cx,ax / pop ds / push bx / pop bx / ret  -  the read of the slot
        // becomes the save local and the segment pop's SP advance leaves with the push.
        TestInstructionHelper helper = new();
        CfgInstruction pushEs = Parse(helper, 0x0000, 0x06);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popDs = Parse(helper, 0x0003, 0x1F);
        CfgInstruction pushBx = Parse(helper, 0x0004, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0005, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushEs, movCx, popDs, pushBx, popBx, ret);
        CfgBlock block = Block(1, pushEs, movCx, popDs, pushBx, popBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("ushort savedDs_0000 = 0;");
        program.SourceText.Should().Contain("DS = savedDs_0000;");
        program.SourceText.Should().NotContain("UInt16[SS, SP]");
        program.SourceText.Should().NotContain("SP += 2");
        AssertCompiles(program);
    }

    [Fact]
    public void AdjacentPushfPopRegister_PrintsDirectly() {
        // Arrange: pushf / pop ax / push cx / pop cx / ret  -  the flag register is the pushed
        // expression.
        TestInstructionHelper helper = new();
        CfgInstruction pushF = Parse(helper, 0x0000, 0x9C);
        CfgInstruction popAx = Parse(helper, 0x0001, 0x58);
        CfgInstruction pushCx = Parse(helper, 0x0002, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0003, 0x59);
        CfgInstruction ret = Parse(helper, 0x0004, 0xC3);
        Chain(pushF, popAx, pushCx, popCx, ret);
        CfgBlock block = Block(1, pushF, popAx, pushCx, popCx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("AX = FlagRegister16;");
        program.SourceText.Should().NotContain("Stack.Push16(FlagRegister16)");
        AssertCompiles(program);
    }

    [Fact]
    public void PushfPopRegisterNonAdjacent_PrintsSavedLocal() {
        // Arrange: pushf / mov cx,ax / pop bx / push dx / pop dx / ret.
        TestInstructionHelper helper = new();
        CfgInstruction pushF = Parse(helper, 0x0000, 0x9C);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popBx = Parse(helper, 0x0003, 0x5B);
        CfgInstruction pushDx = Parse(helper, 0x0004, 0x52);
        CfgInstruction popDx = Parse(helper, 0x0005, 0x5A);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushF, movCx, popBx, pushDx, popDx, ret);
        CfgBlock block = Block(1, pushF, movCx, popBx, pushDx, popDx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("ushort savedBx_0000 = 0;");
        program.SourceText.Should().Contain("savedBx_0000 = FlagRegister16;");
        program.SourceText.Should().Contain("BX = savedBx_0000;");
        AssertCompiles(program);
    }

    [Fact]
    public void PushSegmentPopfReadingTheAbandonedWord_KeepsThePair() {
        // Arrange: push es / popf / mov di,[bx] / ret  -  the popf ends its block with an event
        // check, and the read in the next block may alias the word it abandoned: the pair stays.
        TestInstructionHelper helper = new();
        CfgInstruction pushEs = Parse(helper, 0x0000, 0x06);
        CfgInstruction popF = Parse(helper, 0x0001, 0x9D);
        CfgInstruction loadDi = Parse(helper, 0x0002, 0x8B, 0x3F);
        CfgInstruction ret = Parse(helper, 0x0004, 0xC3);
        Chain(pushEs, popF, loadDi, ret);
        CfgBlock[] blocks = [Block(1, pushEs, popF), Block(2, loadDi, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().Contain("FlagRegister16 = Stack.Pop16();");
        program.SourceText.Should().NotContain("saved");
        AssertCompiles(program);
    }

    [Fact]
    public void WriteThatMayAliasTheSlot_KeepsThePair() {
        // Arrange: push ax / mov [di],bx / pop ax / push bx / pop bx / ret  -  the store through DS
        // cannot be proved disjoint from the pushed word, so the pop's read is not the push's alone.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction store = Parse(helper, 0x0001, 0x89, 0x1D);
        CfgInstruction popAx = Parse(helper, 0x0003, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0004, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0005, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, store, popAx, pushBx, popBx, ret);
        CfgBlock block = Block(1, pushAx, store, popAx, pushBx, popBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void EarlyExitBeforeThePop_KeepsThePair() {
        // Arrange: push ax / jz to the ret block / nop / pop ax / push bx / pop bx / ret  -  a
        // path reaches the end without popping, so the pop cannot close the region.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction jz = Parse(helper, 0x0001, 0x74, 0x04);
        CfgInstruction nop = Parse(helper, 0x0003, 0x90);
        CfgInstruction popAx = Parse(helper, 0x0004, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0005, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0006, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0007, 0xC3);
        Chain(pushAx, jz, nop, popAx, pushBx, popBx);
        jz.Successors.Add(ret);
        jz.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop, ret };
        CfgBlock[] blocks = [Block(1, pushAx, jz), Block(2, nop, popAx, pushBx, popBx), Block(3, ret)];
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), blocks);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void CallBetween_KeepsThePair() {
        // Arrange: push ax / call leaf / pop ax / push bx / pop bx / ret  -  a call inside the
        // region could run on the shifted stack.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction call = Parse(helper, 0x0001, 0xE8, 0x09, 0x00);
        CfgInstruction popAx = Parse(helper, 0x0004, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0005, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0006, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0007, 0xC3);
        CfgInstruction leafMov = Parse(helper, 0x000D, 0xBB, 0x05, 0x00);
        CfgInstruction leafRet = Parse(helper, 0x0010, 0xC3);
        Chain(pushAx, call, popAx, pushBx, popBx, ret);
        call.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { leafMov };
        call.SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
            new HashSet<ICfgNode> { popAx };
        call.Successors.Add(leafMov);
        Chain(leafMov, leafRet);
        CfgBlock[] blocks = [Block(1, pushAx, call, popAx, pushBx, popBx, ret)];
        CfgCodePartition leaf = Partition(2, Block(2, leafMov, leafRet));
        GeneratedCSharpProgram program = Generate(SingleEntry(blocks[0]), [leaf], blocks);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void FaultSiteBetween_KeepsThePair() {
        // Arrange: push ax / div cl (with an observed CPU-fault edge) / pop ax / push bx /
        // pop bx / ret  -  a fault inside the region delivers a handler that could read the
        // shifted stack. The fault is not a memory boundary, so this is the condition that
        // actually decides here.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movCl = Parse(helper, 0x0001, 0xB1, 0x01);
        CfgInstruction div = Parse(helper, 0x0003, 0xF6, 0xF1);
        CfgInstruction popAx = Parse(helper, 0x0005, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0006, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0007, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0008, 0xC3);
        CfgInstruction nop = Parse(helper, 0x0009, 0x90);
        CfgInstruction endRet = Parse(helper, 0x000A, 0xC3);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0xC3);
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> { faultHandler };
        Chain(pushAx, movCl, div, popAx, pushBx, popBx, ret);
        Chain(nop, endRet);
        CfgBlock main = Block(1, pushAx, movCl, div, popAx, pushBx, popBx, ret);
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

        // Act
        GeneratedCSharpProgram program = GenerateProgram([mainPartition, handler], [faultTransfer]);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void ReadOfSpBetween_KeepsThePair() {
        // Arrange: push ax / mov bx,sp / pop ax / push bx / pop bx / ret  -  anything reading SP
        // between them would see the shifted stack pointer.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movBxSp = Parse(helper, 0x0001, 0x89, 0xE3);
        CfgInstruction popAx = Parse(helper, 0x0003, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0004, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0005, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, movBxSp, popAx, pushBx, popBx, ret);
        CfgBlock block = Block(1, pushAx, movBxSp, popAx, pushBx, popBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void ReadThatMayAliasTheSlotBetween_KeepsThePair() {
        // Arrange: push ax / mov cx,[di] / pop ax / push bx / pop bx / ret  -  a read through DS
        // cannot be proved disjoint from the pushed word.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction load = Parse(helper, 0x0001, 0x8B, 0x0D);
        CfgInstruction popAx = Parse(helper, 0x0003, 0x58);
        CfgInstruction pushBx = Parse(helper, 0x0004, 0x53);
        CfgInstruction popBx = Parse(helper, 0x0005, 0x5B);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, load, popAx, pushBx, popBx, ret);
        CfgBlock block = Block(1, pushAx, load, popAx, pushBx, popBx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void TwoPairsAtTheSameOffsetInDifferentSegments_GetNumberedNames() {
        // Arrange: two push ax / pop ax pairs in one method  -  the pushes sit at the same
        // offset in different segments, so their save locals share a stem and get numbered.
        TestInstructionHelper helper = new();
        CfgInstruction pushA1 = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0010),
            writer => writer.WriteBytes(new byte[] { 0x50 }));
        CfgInstruction movCx = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0011),
            writer => writer.WriteBytes(new byte[] { 0x89, 0xC1 }));
        CfgInstruction popA1 = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0013),
            writer => writer.WriteBytes(new byte[] { 0x58 }));
        CfgInstruction pushC1 = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0014),
            writer => writer.WriteBytes(new byte[] { 0x51 }));
        CfgInstruction popC1 = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0015),
            writer => writer.WriteBytes(new byte[] { 0x59 }));
        CfgInstruction ret1 = helper.WriteAndParse(new SegmentedAddress(0xF000, 0x0016),
            writer => writer.WriteBytes(new byte[] { 0xC3 }));
        CfgInstruction pushA2 = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0010),
            writer => writer.WriteBytes(new byte[] { 0x50 }));
        CfgInstruction movDx = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0011),
            writer => writer.WriteBytes(new byte[] { 0x89, 0xC2 }));
        CfgInstruction popA2 = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0013),
            writer => writer.WriteBytes(new byte[] { 0x58 }));
        CfgInstruction pushD2 = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0014),
            writer => writer.WriteBytes(new byte[] { 0x52 }));
        CfgInstruction popD2 = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0015),
            writer => writer.WriteBytes(new byte[] { 0x5A }));
        CfgInstruction ret2 = helper.WriteAndParse(new SegmentedAddress(0x2000, 0x0016),
            writer => writer.WriteBytes(new byte[] { 0xC3 }));
        Chain(pushA1, movCx, popA1, pushC1, popC1, ret1);
        Chain(pushA2, movDx, popA2, pushD2, popD2, ret2);
        CfgBlock first = Block(1, pushA1, movCx, popA1, pushC1, popC1, ret1);
        CfgBlock second = Block(2, pushA2, movDx, popA2, pushD2, popD2, ret2);
        CfgCodePartition partition = new() {
            Id = 1, Kind = CfgCodePartitionKind.Observed, Name = "main",
            Blocks = [first, second],
            Entries = [
                new CfgCodePartitionEntry { Node = first.Entry, Kind = CfgCodePartitionEntryKind.FunctionEntry },
                new CfgCodePartitionEntry { Node = second.Entry, Kind = CfgCodePartitionEntryKind.FunctionEntry }
            ]
        };

        // Act
        GeneratedCSharpProgram program = GenerateProgram([partition], []);

        // Assert: both pairs print  -  the second save local is numbered.
        program.SourceText.Should().Contain("savedAx_0010 = AX;");
        program.SourceText.Should().Contain("savedAx_0010_2 = AX;");
        program.SourceText.Should().Contain("AX = savedAx_0010;");
        program.SourceText.Should().Contain("AX = savedAx_0010_2;");
        AssertCompiles(program);
    }

    [Fact]
    public void ReadOfTheAbandonedWordAfterThePop_KeepsThePair() {
        // Arrange: push ax / mov cx,ax / pop ax / mov bp,sp / mov bx,[bp-2] / ret  -  the read
        // through BP observes the word the rewrite would leave stale.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popAx = Parse(helper, 0x0003, 0x58);
        CfgInstruction movBpSp = Parse(helper, 0x0004, 0x89, 0xE5);
        CfgInstruction read = Parse(helper, 0x0006, 0x8B, 0x5E, 0xFE);
        CfgInstruction ret = Parse(helper, 0x0009, 0xC3);
        Chain(pushAx, movCx, popAx, movBpSp, read, ret);
        CfgBlock block = Block(1, pushAx, movCx, popAx, movBpSp, read, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().Contain("AX = Stack.Pop16();");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void ReadOfThePushedWordInTheBranchCondition_KeepsThePair() {
        // Arrange: mov bx,sp / sub bx,2 / push si / cmp word [bx],1234h / je L / inc ax /
        // L: pop si / push cx / sub dx,dx / ret. The cmp reads exactly the word push si writes;
        // flag propagation moves that read into the je's branch condition, and the sub kills the
        // flags the cmp wrote so nothing else keeps it a statement.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushSi = Parse(helper, 0x0005, 0x56);
        CfgInstruction cmp = Parse(helper, 0x0006, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000A, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000C, 0x40);
        CfgInstruction popSi = Parse(helper, 0x000D, 0x5E);
        CfgInstruction pushCx = Parse(helper, 0x000E, 0x51);
        CfgInstruction subDx = Parse(helper, 0x000F, 0x29, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0011, 0xC3);
        Chain(movBxSp, subBx, pushSi, cmp, je);
        Branch(je, incAx, popSi);
        Chain(incAx, popSi, pushCx, subDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushSi, cmp, je),
            Block(2, incAx), Block(3, popSi, pushCx, subDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock pushBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Origin is CfgInstruction { Address.Offset: 0x0005 }));

        // Assert preconditions: the push block's terminator is a branch whose condition reads
        // memory, and no statement between the push and the pop reads it - the scan sees nothing.
        IrBranch branch = pushBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert: the read in the terminator may observe the pushed word, so no pair can form.
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push si wrote; the pair must stay");
    }

    [Fact]
    public void SameRegionWithoutTheMemoryRead_FormsThePair() {
        // Arrange: the same shape with cmp ax,1234h - no memory read anywhere near the slot, so
        // the region otherwise qualifies and exactly one pair is found.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushSi = Parse(helper, 0x0005, 0x56);
        CfgInstruction cmp = Parse(helper, 0x0006, 0x3D, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x0009, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000B, 0x40);
        CfgInstruction popSi = Parse(helper, 0x000C, 0x5E);
        CfgInstruction pushCx = Parse(helper, 0x000D, 0x51);
        CfgInstruction subDx = Parse(helper, 0x000E, 0x29, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0010, 0xC3);
        Chain(movBxSp, subBx, pushSi, cmp, je);
        Branch(je, incAx, popSi);
        Chain(incAx, popSi, pushCx, subDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushSi, cmp, je),
            Block(2, incAx), Block(3, popSi, pushCx, subDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert
        plan.Pairs.Should().ContainSingle(
            "with no read of the slot the push si / pop si region qualifies");
    }

    [Fact]
    public void ReadOfTheAbandonedWordInTheBranchCondition_KeepsThePairForASegmentPop() {
        // Arrange: mov bx,sp / sub bx,2 / push ax / mov cx,ax / pop es / cmp word [bx],1234h /
        // je L / inc ax / L: push cx / pop cx / xor dx,dx / ret. Flag propagation moves the
        // cmp's [bx] read - the word push ax abandoned - into the je's condition, which the
        // pop's coverage walk crosses on its way to the covering push cx.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushAx = Parse(helper, 0x0005, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0006, 0x89, 0xC1);
        CfgInstruction popEs = Parse(helper, 0x0008, 0x07);
        CfgInstruction cmp = Parse(helper, 0x0009, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000D, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000F, 0x40);
        CfgInstruction pushCx = Parse(helper, 0x0010, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0011, 0x59);
        CfgInstruction xorDx = Parse(helper, 0x0012, 0x31, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0014, 0xC3);
        Chain(movBxSp, subBx, pushAx, movCx, popEs, cmp, je);
        Branch(je, incAx, pushCx);
        Chain(incAx, pushCx, popCx, xorDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushAx, movCx, popEs, cmp, je),
            Block(2, incAx), Block(3, pushCx, popCx, xorDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock popBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Node is BinaryOperationNode
                { Left: SegmentRegisterNode }
                && statement.Origin is CfgInstruction { Address.Offset: 0x0008 }));
        int popIndex = popBlock.Statements.FindIndex(statement =>
            statement.Node is BinaryOperationNode { Left: SegmentRegisterNode }
            && statement.Origin is CfgInstruction { Address.Offset: 0x0008 });

        // Assert preconditions: pop es is a single Stack.Pop assignment (so the pair shape
        // exists), and the pop block's terminator reads the pushed word in its condition.
        popBlock.Statements[popIndex].Node.Should().BeOfType<BinaryOperationNode>()
            .Which.Right.Should().BeOfType<StackCallValueNode>()
            .Which.Operation.Should().Be(StackOperation.Pop,
                "pop es must be a Stack.Pop call");
        IrBranch branch = popBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert: the read in the terminator may observe the abandoned word, so no pair can form.
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push ax abandoned; the pair must stay");
    }

    [Fact]
    public void ReadOfTheAbandonedWordInTheBranchCondition_KeepsThePairForAPopf() {
        // Arrange: the same shape with popf as the pop of the pair.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushAx = Parse(helper, 0x0005, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0006, 0x89, 0xC1);
        CfgInstruction popf = Parse(helper, 0x0008, 0x9D);
        CfgInstruction cmp = Parse(helper, 0x0009, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000D, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000F, 0x40);
        CfgInstruction pushCx = Parse(helper, 0x0010, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0011, 0x59);
        CfgInstruction xorDx = Parse(helper, 0x0012, 0x31, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0014, 0xC3);
        Chain(movBxSp, subBx, pushAx, movCx, popf, cmp, je);
        Branch(je, incAx, pushCx);
        Chain(incAx, pushCx, popCx, xorDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushAx, movCx, popf, cmp, je),
            Block(2, incAx), Block(3, pushCx, popCx, xorDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock popBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Origin is CfgInstruction { Address.Offset: 0x0008 }));

        // Assert precondition: the pop block's terminator reads the pushed word in its condition.
        IrBranch branch = popBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push ax abandoned; the pair must stay");
    }

    [Fact]
    public void ReadOfTheAbandonedWordInTheBranchCondition_KeepsAnAdjacentPair() {
        // Arrange: mov bx,sp / sub bx,2 / push ax / pop dx / cmp word [bx],1234h / je L /
        // inc ax / L: push cx / pop cx / xor dx,dx / ret. The push and the pop are adjacent
        // (the direct form, printed Y = X); the walk to the covering push cx crosses the je
        // whose condition reads the word push ax abandoned.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushAx = Parse(helper, 0x0005, 0x50);
        CfgInstruction popDx = Parse(helper, 0x0006, 0x5A);
        CfgInstruction cmp = Parse(helper, 0x0007, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000B, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000D, 0x40);
        CfgInstruction pushCx = Parse(helper, 0x000E, 0x51);
        CfgInstruction popCx = Parse(helper, 0x000F, 0x59);
        CfgInstruction xorDx = Parse(helper, 0x0010, 0x31, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0012, 0xC3);
        Chain(movBxSp, subBx, pushAx, popDx, cmp, je);
        Branch(je, incAx, pushCx);
        Chain(incAx, pushCx, popCx, xorDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushAx, popDx, cmp, je),
            Block(2, incAx), Block(3, pushCx, popCx, xorDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock popBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Node is BinaryOperationNode
                { Left: RegisterNode, Right: StackCallValueNode { Operation: StackOperation.Pop } }
                && statement.Origin is CfgInstruction { Address.Offset: 0x0006 }));

        // Assert precondition: the pop block's terminator reads the pushed word in its
        // condition.
        IrBranch branch = popBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert: the read in the terminator may observe the abandoned word, so no pair can form.
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push ax abandoned; the pair must stay");
    }

    [Fact]
    public void ReadOfTheAbandonedWordInTheBranchCondition_KeepsAnAdjacentPairForASegmentPop() {
        // Arrange: the same shape with pop es as the adjacent pop of the pair.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushAx = Parse(helper, 0x0005, 0x50);
        CfgInstruction popEs = Parse(helper, 0x0006, 0x07);
        CfgInstruction cmp = Parse(helper, 0x0007, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000B, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000D, 0x40);
        CfgInstruction pushCx = Parse(helper, 0x000E, 0x51);
        CfgInstruction popCx = Parse(helper, 0x000F, 0x59);
        CfgInstruction xorDx = Parse(helper, 0x0010, 0x31, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0012, 0xC3);
        Chain(movBxSp, subBx, pushAx, popEs, cmp, je);
        Branch(je, incAx, pushCx);
        Chain(incAx, pushCx, popCx, xorDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushAx, popEs, cmp, je),
            Block(2, incAx), Block(3, pushCx, popCx, xorDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock popBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Node is BinaryOperationNode { Left: SegmentRegisterNode }
                && statement.Origin is CfgInstruction { Address.Offset: 0x0006 }));
        int popIndex = popBlock.Statements.FindIndex(statement =>
            statement.Node is BinaryOperationNode { Left: SegmentRegisterNode }
            && statement.Origin is CfgInstruction { Address.Offset: 0x0006 });

        // Assert preconditions: pop es is a single Stack.Pop assignment, and the pop block's
        // terminator reads the pushed word in its condition.
        popBlock.Statements[popIndex].Node.Should().BeOfType<BinaryOperationNode>()
            .Which.Right.Should().BeOfType<StackCallValueNode>()
            .Which.Operation.Should().Be(StackOperation.Pop,
                "pop es must be a Stack.Pop call");
        IrBranch branch = popBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push ax abandoned; the pair must stay");
    }

    [Fact]
    public void ReadOfTheAbandonedWordInTheBranchCondition_KeepsAnAdjacentPairForAPopf() {
        // Arrange: the same shape with popf as the adjacent pop of the pair.
        TestInstructionHelper helper = new();
        CfgInstruction movBxSp = Parse(helper, 0x0000, 0x89, 0xE3);
        CfgInstruction subBx = Parse(helper, 0x0002, 0x83, 0xEB, 0x02);
        CfgInstruction pushAx = Parse(helper, 0x0005, 0x50);
        CfgInstruction popf = Parse(helper, 0x0006, 0x9D);
        CfgInstruction cmp = Parse(helper, 0x0007, 0x81, 0x3F, 0x34, 0x12);
        CfgInstruction je = Parse(helper, 0x000B, 0x74, 0x01);
        CfgInstruction incAx = Parse(helper, 0x000D, 0x40);
        CfgInstruction pushCx = Parse(helper, 0x000E, 0x51);
        CfgInstruction popCx = Parse(helper, 0x000F, 0x59);
        CfgInstruction xorDx = Parse(helper, 0x0010, 0x31, 0xD2);
        CfgInstruction ret = Parse(helper, 0x0012, 0xC3);
        Chain(movBxSp, subBx, pushAx, popf, cmp, je);
        Branch(je, incAx, pushCx);
        Chain(incAx, pushCx, popCx, xorDx, ret);
        IrMethod method = Lift(Block(1, movBxSp, subBx, pushAx, popf, cmp, je),
            Block(2, incAx), Block(3, pushCx, popCx, xorDx, ret));
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrFullPipelineResult pipeline =
            IrPassPipeline.RunAll(method, verify: true, buildWebs: false, new GenerationStepTimer());
        foreach (IrBlock block in method.Blocks) {
            block.Statements.RemoveAll(statement => statement.Node is CheckExternalEventsNode);
        }
        IrBlock popBlock = method.Blocks.Single(block => block.Statements
            .Any(statement => statement.Origin is CfgInstruction { Address.Offset: 0x0006 }));

        // Assert precondition: the pop block's terminator reads the pushed word in its
        // condition (popf is followed by an event check, which the walk refuses first; the
        // statement removal above clears it so the terminator read is what keeps the pair).
        IrBranch branch = popBlock.Terminator.Should().BeOfType<IrBranch>()
            .Which;
        IrAstChildren.Descendants(branch.Condition)
            .OfType<SegmentedPointerNode>().Should().NotBeEmpty(
                "the cmp's [bx] read must sit in the branch condition, not in a statement");

        // Act
        SaveRestorePairs.SavedPairPlan plan = SaveRestorePairs.Plan(method, pipeline.Ssa);

        // Assert
        plan.Pairs.Should().BeEmpty(
            "the branch condition reads ss:[bx], the word push ax abandoned; the pair must stay");
    }

    [Fact]
    public void PopSs_KeepsThePair() {
        // Arrange: push ax / mov cx,ax / pop ss / push cx / pop cx / ret - a pop ss blocks
        // interrupt delivery until the next instruction retires, which an assignment to the
        // save local does not reproduce, so the pair never forms.
        TestInstructionHelper helper = new();
        CfgInstruction pushAx = Parse(helper, 0x0000, 0x50);
        CfgInstruction movCx = Parse(helper, 0x0001, 0x89, 0xC1);
        CfgInstruction popSs = Parse(helper, 0x0003, 0x17);
        CfgInstruction pushCx = Parse(helper, 0x0004, 0x51);
        CfgInstruction popCx = Parse(helper, 0x0005, 0x59);
        CfgInstruction ret = Parse(helper, 0x0006, 0xC3);
        Chain(pushAx, movCx, popSs, pushCx, popCx, ret);
        CfgBlock block = Block(1, pushAx, movCx, popSs, pushCx, popCx, ret);
        GeneratedCSharpProgram program = Generate(SingleEntry(block), block);

        // Assert
        program.SourceText.Should().Contain("Stack.Push16(AX);");
        program.SourceText.Should().NotContain("savedAx_0000");
        AssertCompiles(program);
    }

    [Fact]
    public void Apply_ThrowsWhenARemovedStatementIsNotADirectBlockChild() {
        // Arrange: a plan whose push node is reachable only inside another call's argument
        // list - the rewrite drops `removed` nodes at block top level only.
        DataType type = DataType.UnsignedFromBitWidth(BitWidth.WORD_16);
        MethodScopedVariableReferenceNode variable = new(type, "savedAx_0000");
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        StackCallNode push = new(StackOperation.Push, BitWidth.WORD_16, ax);
        BinaryOperationNode pop = new(type, ax, BinaryOperation.ASSIGN, variable);
        SaveRestorePairs.Pair pair = new(push, pop, true, ax, variable,
            new LocalDeclarationNode(type, "savedAx_0000", new ConstantNode(type, 0)), null);
        StackCallNode wrapper = new(StackOperation.Push, BitWidth.WORD_16, push);
        BlockNode body = new([wrapper]);

        // Act
        Action apply = () => SaveRestorePairs.Apply(body, new SaveRestorePairs.SavedPairPlan([pair]));

        // Assert
        apply.Should().Throw<InvalidOperationException>();
    }
}
