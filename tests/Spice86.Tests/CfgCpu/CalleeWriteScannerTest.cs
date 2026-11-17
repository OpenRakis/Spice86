namespace Spice86.Tests.CfgCpu;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Tests of <see cref="CalleeWriteScanner"/>: the register-write mask of a decoded subgraph is
/// exact where every path is understood, and the all-but-CS mask as soon as one is not. Routines
/// are written as bytes and decoded into the node index like the other speculative tests.
/// </summary>
public class CalleeWriteScannerTest : SpeculativeTestBase {
    private const ushort Seg = 0xF000;
    private const ushort Unknown = (ushort)RegisterBits.AllExceptCs;

    private readonly ObservedCallPreservation _observedPreservation;

    public CalleeWriteScannerTest() {
        _observedPreservation = new ObservedCallPreservation(State);
        State.SS = 0x1000;
        State.SP = 0x0100;
    }

    private CalleeWriteScanner CreateScanner(bool returnsBalanced) =>
        new(NodeIndex, InstalledInterrupts, InterruptVectors, _ => returnsBalanced, _observedPreservation,
            LoggerService);

    /// <summary>Records one observed balanced call from F000:0F00 to <paramref name="calleeOffset"/>
    /// during which <paramref name="inside"/> changes registers.</summary>
    private void ObserveCall(ushort calleeOffset, Action inside) {
        CfgInstruction continuation = EnterObservedCall(_observedPreservation, 0x0F00, calleeOffset);
        inside();
        ReturnFromObservedCall(_observedPreservation, continuation);
    }

    [Fact]
    public void UnknownCalleeUsesRegistersObservedUnchanged() {
        // jmp far F000:0300 - the static scan cannot follow a far jump
        DecodeRoutine(0x100, 0xEA, 0x00, 0x03, 0x00, 0xF0);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));

        ObserveCall(0x100, () => State.AX = 3);
        ObserveCall(0x100, () => State.AX = 4);

        Assert.Equal(Regs(RegisterBits.Ax), CreateScanner(returnsBalanced: false).MayWrite(At(0x100)));
    }

    [Fact]
    public void CachedScanThatUsedObservedEvidenceSeesAShrunkMask() {
        // inner: jmp far F000:0300 (statically unknown)   outer: call inner; ret
        DecodeRoutine(0x200, 0xEA, 0x00, 0x03, 0x00, 0xF0);
        DecodeRoutine(0x100, 0xE8, 0xFD, 0x00, 0xC3);
        CalleeWriteScanner scanner = CreateScanner(returnsBalanced: true);
        ObserveCall(0x200, () => State.AX = 1);
        Assert.Equal(Regs(RegisterBits.Ax), scanner.MayWrite(At(0x100)));

        ObserveCall(0x200, () => State.BX = 2);

        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx), scanner.MayWrite(At(0x100)));
    }

    [Fact]
    public void KnownStaticResultWinsOverObservedEvidence() {
        // mov ax,1; mov bx,2; ret
        DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xBB, 0x02, 0x00, 0xC3);
        ObserveCall(0x100, () => { });

        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    /// <summary>Writes <paramref name="bytes"/> at <paramref name="offset"/> in segment
    /// <see cref="Seg"/>, decodes each instruction in order and inserts it into the node index.
    /// Returns the decoded nodes in address order.</summary>
    private List<CfgInstruction> DecodeRoutine(ushort offset, params byte[] bytes) {
        uint physical = MemoryUtils.ToPhysicalAddress(Seg, offset);
        for (int i = 0; i < bytes.Length; i++) {
            Memory.UInt8[physical + (uint)i] = bytes[i];
        }
        List<CfgInstruction> nodes = [];
        ushort cursor = offset;
        ushort end = (ushort)(offset + bytes.Length);
        while (cursor < end) {
            CfgInstruction node = Parser.ParseInstructionAt(new SegmentedAddress(Seg, cursor));
            NodeIndex.Insert(node);
            nodes.Add(node);
            cursor += (ushort)node.Length;
        }
        return nodes;
    }

    private static SegmentedAddress At(ushort offset) => new(Seg, offset);

    private static ushort Regs(params RegisterBits[] bits) {
        ushort mask = 0;
        foreach (RegisterBits bit in bits) {
            mask |= (ushort)bit;
        }
        return mask;
    }

    [Fact]
    public void LeafWritingAxAndBxGivesAxBx() {
        // mov ax,1; mov bx,2; ret
        DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xBB, 0x02, 0x00, 0xC3);
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx, RegisterBits.Sp),
            CreateScanner(returnsBalanced: false).MayWrite(At(0x100)));
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void RetWithImmediateKeepsSp() {
        // mov ax,1; ret 2 - SP comes back off balance even for a callee known to return
        DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xC2, 0x02, 0x00);
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Sp),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void NestedCallAddsNestedMask() {
        // entry: mov cx,1; call leaf2; ret   leaf2: mov dx,1; ret
        DecodeRoutine(0x200, 0xBA, 0x01, 0x00, 0xC3);
        DecodeRoutine(0x100, 0xB9, 0x01, 0x00, 0xE8, 0xFA, 0x00, 0xC3);
        Assert.Equal(Regs(RegisterBits.Cx, RegisterBits.Dx),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void SavedAndRestoredRegisterIsNotWritten() {
        // push si; mov si,1; pop si; ret - the write is undone by the restore
        DecodeRoutine(0x100, 0x56, 0xBE, 0x01, 0x00, 0x5E, 0xC3);
        Assert.Equal(0, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void SavedRegisterKeptWhenOneReturnLacksPop() {
        // push di; mov di,1; jz L; pop di; ret; L: ret - the second return does not
        // restore DI, so the write must stay in the mask
        DecodeRoutine(0x100, 0x57, 0xBF, 0x01, 0x00, 0x74, 0x02, 0x5F, 0xC3, 0xC3);
        Assert.Equal(Regs(RegisterBits.Di),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void PopsInWrongOrderKeepRegisters() {
        // push si; push di; mov si,1; mov di,1; pop si; pop di; ret - the pops do not
        // mirror the pushes, so neither register is restored
        DecodeRoutine(0x100, 0x56, 0x57, 0xBE, 0x01, 0x00, 0xBF, 0x01, 0x00, 0x5E, 0x5F, 0xC3);
        Assert.Equal(Regs(RegisterBits.Si, RegisterBits.Di),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void PushaPopaCalleeWritesNoGeneralRegister() {
        // pusha; mov ax,1; mov di,2; popa; ret - every general register is restored
        DecodeRoutine(0x100, 0x60, 0xB8, 0x01, 0x00, 0xBF, 0x02, 0x00, 0x61, 0xC3);
        Assert.Equal(0, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void FrameCalleeSavesDiAndBp() {
        // push bp; mov bp,sp; push di; mov di,1; pop di; mov sp,bp; pop bp; ret
        DecodeRoutine(0x100, 0x55, 0x89, 0xE5, 0x57, 0xBF, 0x01, 0x00, 0x5F, 0x89, 0xEC, 0x5D, 0xC3);
        Assert.Equal(0, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void LeaveCalleeSavesDiAndBp() {
        // push bp; mov bp,sp; push di; mov di,1; pop di; leave; ret
        DecodeRoutine(0x100, 0x55, 0x89, 0xE5, 0x57, 0xBF, 0x01, 0x00, 0x5F, 0xC9, 0xC3);
        Assert.Equal(0, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void UnbalancedStackAdjustKeepsSavedRegister() {
        // push di; mov di,1; add sp,2; pop di; ret - the pop no longer reads the
        // saved slot, so DI is not provably restored
        DecodeRoutine(0x100, 0x57, 0xBF, 0x01, 0x00, 0x83, 0xC4, 0x02, 0x5F, 0xC3);
        Assert.Equal(Regs(RegisterBits.Di),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void SavedRegisterKeptWhenNotBalanced() {
        // push si; mov si,1; pop si; ret - without the balance evidence nothing is removed
        DecodeRoutine(0x100, 0x56, 0xBE, 0x01, 0x00, 0x5E, 0xC3);
        Assert.Equal(Regs(RegisterBits.Si, RegisterBits.Sp),
            CreateScanner(returnsBalanced: false).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectJumpIsUnknown() {
        // mov ax,1; jmp ax - the path leaves through a register
        DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xFF, 0xE0);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectCallInsideCalleeUnionsObservedTargets() {
        // entry: call word [cs:0x0500]; ret - the observed targets write AX and BX
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x2E, 0xFF, 0x16, 0x00, 0x05, 0xC3);
        List<CfgInstruction> targetAx = DecodeRoutine(0x200, 0xB8, 0x01, 0x00, 0xC3);
        List<CfgInstruction> targetBx = DecodeRoutine(0x300, 0xBB, 0x01, 0x00, 0xC3);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], targetAx[0]);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], targetBx[0]);
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectCallWithUnknownTargetIsUnknown() {
        // entry: call word [cs:0x0500]; ret - the observed target (jmp ax) cannot be scanned
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x2E, 0xFF, 0x16, 0x00, 0x05, 0xC3);
        List<CfgInstruction> target = DecodeRoutine(0x200, 0xFF, 0xE0);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], target[0]);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectCallWithoutObservedTargetIsUnknown() {
        // entry: call word [cs:0x0500]; ret - the site was never observed
        DecodeRoutine(0x100, 0x2E, 0xFF, 0x16, 0x00, 0x05, 0xC3);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectCallWithOnlySpeculativeTargetIsUnknown() {
        // entry: call word [cs:0x0500]; ret - the only linked target is speculative
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x2E, 0xFF, 0x00, 0x05, 0xC3);
        List<CfgInstruction> target = DecodeRoutine(0x200, 0xB8, 0x01, 0x00, 0xC3);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], target[0]);
        routine[0].SpeculativelyWiredSuccessors.Add(target[0]);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectJumpInsideCalleeCrossesObservedTarget() {
        // entry: jmp word [cs:0x0500] - the observed target's return is the callee's return
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x2E, 0xFF, 0x26, 0x00, 0x05);
        List<CfgInstruction> target = DecodeRoutine(0x200, 0xB9, 0x01, 0x00, 0xC3);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], target[0]);
        Assert.Equal(Regs(RegisterBits.Cx),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void LinkedObservedTargetInvalidatesCachedScan() {
        // entry: call word [cs:0x0500]; ret - a second observed target linked later adds DI
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x2E, 0xFF, 0x16, 0x00, 0x05, 0xC3);
        List<CfgInstruction> targetAx = DecodeRoutine(0x200, 0xB8, 0x01, 0x00, 0xC3);
        List<CfgInstruction> targetDi = DecodeRoutine(0x300, 0xBF, 0x01, 0x00, 0xC3);
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], targetAx[0]);
        CalleeWriteScanner scanner = CreateScanner(returnsBalanced: true);
        Assert.Equal(Regs(RegisterBits.Ax), scanner.MayWrite(At(0x100)));
        NodeLinker.Link(InstructionSuccessorType.Normal, routine[0], targetDi[0]);
        scanner.ForgetScansThrough(routine[0].Address);
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Di), scanner.MayWrite(At(0x100)));
    }

    [Fact]
    public void UndecodedSuccessorIsUnknown() {
        // jz +10 (into an undecoded gap); mov ax,1; ret - the taken arm is nowhere in the index
        DecodeRoutine(0x100, 0x74, 0x0A, 0xB8, 0x01, 0x00, 0xC3);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void RecursionIsUnknown() {
        // call self; ret - the callee's own scan is still in progress when its call asks for it
        DecodeRoutine(0x100, 0xE8, 0xFD, 0xFF, 0xC3);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    /// <summary>The <see cref="RegisterBits"/> mask a contract's <see cref="CpuValues"/> writes
    /// convert to, by the scanner's own conversion rule.</summary>
    private static ushort ContractMask(CpuValues writes) {
        ushort mask = 0;
        for (int register = 0; register < 8; register++) {
            if ((((ulong)writes >> (4 * register)) & 0x3UL) != 0) {
                mask |= (ushort)(1 << register);
            }
        }
        for (int segment = 0; segment < 6; segment++) {
            if ((((ulong)writes >> (32 + segment)) & 1UL) != 0) {
                mask |= (ushort)(1 << (8 + segment));
            }
        }
        return mask;
    }

    /// <summary>Installs a stub int 21h handler whose services are the real
    /// <see cref="DosInt21Handler"/> declarations, so the test reads the same contracts the
    /// emulator ships. Returns the service entry AH selects.</summary>
    private InterruptServiceEntry InstallDos21(out SegmentedAddress stubAddress) {
        InterruptServiceDeclarations declarations =
            InterruptServiceAttributes.Of(typeof(DosInt21Handler), ServiceSelector.Ah);
        IInterruptHandler handler = Substitute.For<IInterruptHandler>();
        handler.VectorNumber.Returns((byte)0x21);
        handler.Services.Returns(new HandlerServices.SelectedServices(ServiceSelector.Ah,
            declarations.Services.ToDictionary(pair => pair.Entry.SelectorValue,
                pair => pair.Entry)));
        stubAddress = new SegmentedAddress(Seg, 0x900);
        InstalledInterrupts.Register(handler, stubAddress);
        InterruptVectors[0x21] = stubAddress;
        return declarations.Services.Single(pair => pair.Entry.SelectorValue == 0x30).Entry;
    }

    [Fact]
    public void IntWithContractGivesContractWrites() {
        // mov ah,30h; int 21h; ret - AH selects the service whose contract applies
        InterruptServiceEntry service30 = InstallDos21(out SegmentedAddress _);
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0xB4, 0x30, 0xCD, 0x21, 0xC3);
        CfgInstruction movAh = routine[0];
        CfgInstruction intNode = routine[1];
        CfgBlock block = new(1, movAh);
        movAh.ContainingBlock = block;
        intNode.ContainingBlock = block;
        intNode.Predecessors.Add(movAh);

        // The routine's own mov ah also writes AX on top of the contract's declared writes.
        ushort expected = (ushort)(ContractMask(service30.Contract.Registers.Writes)
            | (ushort)RegisterBits.Ax);
        Assert.Equal(expected, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
        Assert.Equal((ushort)(expected | (ushort)RegisterBits.Sp),
            CreateScanner(returnsBalanced: false).MayWrite(At(0x100)));
    }

    /// <summary>Installs a stub int 33h handler whose services are the real
    /// <see cref="MouseInt33Handler"/> declarations (selected by AX). Returns the reset service.</summary>
    private InterruptServiceEntry InstallMouse33() {
        InterruptServiceDeclarations declarations =
            InterruptServiceAttributes.Of(typeof(MouseInt33Handler), ServiceSelector.Ax);
        IInterruptHandler handler = Substitute.For<IInterruptHandler>();
        handler.VectorNumber.Returns((byte)0x33);
        handler.Services.Returns(new HandlerServices.SelectedServices(ServiceSelector.Ax,
            declarations.Services.ToDictionary(pair => pair.Entry.SelectorValue, pair => pair.Entry)));
        SegmentedAddress stubAddress = new(Seg, 0x900);
        InstalledInterrupts.Register(handler, stubAddress);
        InterruptVectors[0x33] = stubAddress;
        return declarations.Services.Single(pair => pair.Entry.SelectorValue == 0x00).Entry;
    }

    /// <summary>Puts <paramref name="nodes"/> in one block, each the predecessor of the next.</summary>
    private static void ChainInOneBlock(IReadOnlyList<CfgInstruction> nodes) {
        CfgBlock block = new(1, nodes[0]);
        for (int i = 0; i < nodes.Count; i++) {
            nodes[i].ContainingBlock = block;
            if (i > 0) {
                nodes[i].Predecessors.Add(nodes[i - 1]);
            }
        }
    }

    [Fact]
    public void IntSelectedByAxConstantGivesContractWrites() {
        // xor ax,ax; int 33h; ret - the mouse reset service (AX = 0) applies
        InterruptServiceEntry reset = InstallMouse33();
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x31, 0xC0, 0xCD, 0x33, 0xC3);
        ChainInOneBlock(routine.Take(2).ToList());
        ushort expected = (ushort)(ContractMask(reset.Contract.Registers.Writes) | (ushort)RegisterBits.Ax);
        Assert.Equal(expected, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IntSelectedByAxFromRegisterIsUnknown() {
        // mov ax,bx; int 33h; ret - AX is not a constant
        InstallMouse33();
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0x89, 0xD8, 0xCD, 0x33, 0xC3);
        ChainInOneBlock(routine.Take(2).ToList());
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IntSelectedByAxSeesConstantsAcrossTheStraightLineRun() {
        // mov al,3; mov dx,1; mov ah,0; int 33h; ret - AX = 0003 from two byte loads around an unrelated mov
        InstallMouse33();
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0xB0, 0x03, 0xBA, 0x01, 0x00, 0xB4, 0x00, 0xCD, 0x33, 0xC3);
        ChainInOneBlock(routine.Take(4).ToList());
        Assert.NotEqual(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IntWithForeignVectorIsUnknown() {
        // The IVT entry no longer names the emulator's stub: the handler may be anything.
        InterruptServiceEntry _ = InstallDos21(out SegmentedAddress _);
        InterruptVectors[0x21] = new SegmentedAddress(0x1234, 0x5678);
        List<CfgInstruction> routine = DecodeRoutine(0x100, 0xB4, 0x30, 0xCD, 0x21, 0xC3);
        CfgInstruction movAh = routine[0];
        CfgInstruction intNode = routine[1];
        CfgBlock block = new(1, movAh);
        movAh.ContainingBlock = block;
        intNode.ContainingBlock = block;
        intNode.Predecessors.Add(movAh);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void NestedCalleeChangeDropsOuterMask() {
        // entry: mov cx,1; call leaf2; ret   leaf2: mov dx,1; ret - the cached outer scan
        // depends on leaf2's nodes: removing leaf2's entry makes the outer unknown too.
        List<CfgInstruction> leaf2 = DecodeRoutine(0x200, 0xBA, 0x01, 0x00, 0xC3);
        DecodeRoutine(0x100, 0xB9, 0x01, 0x00, 0xE8, 0xFA, 0x00, 0xC3);
        CalleeWriteScanner scanner = CreateScanner(returnsBalanced: true);
        Assert.Equal(Regs(RegisterBits.Cx, RegisterBits.Dx), scanner.MayWrite(At(0x100)));
        ReplacerRegistry.RemoveInstruction(leaf2[0]);
        Assert.Equal(Unknown, scanner.MayWrite(At(0x100)));
    }

    [Fact]
    public void NestedIntIsNotCachedInOuter() {
        // entry: call sub; ret   sub: mov ah,30h; int 21h; ret - the int result depends on
        // the IVT, so the outer that used it must not be cached either.
        InterruptServiceEntry service30 = InstallDos21(out SegmentedAddress _);
        List<CfgInstruction> sub = DecodeRoutine(0x200, 0xB4, 0x30, 0xCD, 0x21, 0xC3);
        CfgInstruction movAh = sub[0];
        CfgInstruction intNode = sub[1];
        CfgBlock block = new(1, movAh);
        movAh.ContainingBlock = block;
        intNode.ContainingBlock = block;
        intNode.Predecessors.Add(movAh);
        DecodeRoutine(0x100, 0xE8, 0xFD, 0x00, 0xC3);
        CalleeWriteScanner scanner = CreateScanner(returnsBalanced: true);
        ushort expected = (ushort)(ContractMask(service30.Contract.Registers.Writes)
            | (ushort)RegisterBits.Ax);
        Assert.Equal(expected, scanner.MayWrite(At(0x100)));
        InterruptVectors[0x21] = new SegmentedAddress(0x1234, 0x5678);
        Assert.Equal(Unknown, scanner.MayWrite(At(0x100)));
    }

    [Fact]
    public void RemovedNodeDropsCachedMask() {
        // mov ax,1; ret at leaf, then a node of the routine leaves the index through the same
        // removal path a sweep uses: the next scan is incomplete, hence unknown.
        List<CfgInstruction> leaf = DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xC3);
        CalleeWriteScanner scanner = CreateScanner(returnsBalanced: true);
        Assert.Equal(Regs(RegisterBits.Ax), scanner.MayWrite(At(0x100)));
        ReplacerRegistry.RemoveInstruction(leaf[1]);
        Assert.Equal(Unknown, scanner.MayWrite(At(0x100)));
    }
}
