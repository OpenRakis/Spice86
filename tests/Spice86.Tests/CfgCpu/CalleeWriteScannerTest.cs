namespace Spice86.Tests.CfgCpu;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
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

    private CalleeWriteScanner CreateScanner(bool returnsBalanced) =>
        new(NodeIndex, InstalledInterrupts, InterruptVectors, _ => returnsBalanced);

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
    public void PushPopPairIsBalanced() {
        // push si; mov si,1; pop si; ret - the push only moves SP, the pop restores both
        DecodeRoutine(0x100, 0x56, 0xBE, 0x01, 0x00, 0x5E, 0xC3);
        Assert.Equal(Regs(RegisterBits.Si),
            CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
    }

    [Fact]
    public void IndirectJumpIsUnknown() {
        // mov ax,1; jmp ax - the path leaves through a register
        DecodeRoutine(0x100, 0xB8, 0x01, 0x00, 0xFF, 0xE0);
        Assert.Equal(Unknown, CreateScanner(returnsBalanced: true).MayWrite(At(0x100)));
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
