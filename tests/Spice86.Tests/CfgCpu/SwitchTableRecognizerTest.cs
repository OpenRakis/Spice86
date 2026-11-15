namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Direct tests of <see cref="SwitchTableRecognizer"/> on hand-assembled guard->transfer chains.
/// </summary>
public class SwitchTableRecognizerTest : SpeculativeTestBase {
    private const ushort Seg = 0xF000;
    private const ushort Table = 0x0E24;

    private static SegmentedAddress At(ushort offset) => new(Seg, offset);

    /// <summary>Writes the byte sequence and links every adjacent pair so the recognizer can walk
    /// back through the chain. Returns the last (transfer) instruction.</summary>
    private CfgInstruction Chain(ushort start, params byte[] opcodes) {
        uint physical = (uint)(Seg * 16 + start);
        for (int i = 0; i < opcodes.Length; i++) {
            Memory.UInt8[physical + (uint)i] = opcodes[i];
        }
        // Parse instructions linearly until the end of the written region.
        List<CfgInstruction> instructions = new();
        ushort offset = start;
        while (offset < start + opcodes.Length) {
            CfgInstruction instruction = Parser.ParseInstructionAt(At(offset));
            instructions.Add(instruction);
            offset += (ushort)instruction.Length;
        }
        Assert.Equal(start + opcodes.Length, offset);
        for (int i = 1; i < instructions.Count; i++) {
            WireEdge(instructions[i - 1], instructions[i]);
        }
        return instructions[^1];
    }

    [Fact]
    public void RecognizesWatcomByteForm() {
        // cmp al,0x37 ; ja +6 ; xor ah,ah ; mov bx,ax ; shl bx,1 ; jmp [cs:bx+0x0E24]
        CfgInstruction transfer = Chain(0x100,
            0x3C, 0x37,
            0x77, 0x06,
            0x30, 0xE4,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(0x37, shape.Bound);
        Assert.Equal(Table, shape.BaseOffset);
        Assert.False(shape.IsCall);
    }

    [Fact]
    public void RecognizesDosReXchgForm() {
        // cmp ax,2 ; ja +5 ; xchg bx,ax ; shl bx,1 ; jmp [cs:bx+T]
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x05,
            0x93,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
    }

    [Fact]
    public void RecognizesJbeForm() {
        // cmp ax,2 ; jbe mov ; hlt ; mov bx,ax ; shl bx,1 ; jmp [cs:bx+T]
        // The mov is in a separate block whose only predecessor is the taken jbe edge.
        uint physical = (uint)(Seg * 16 + 0x100);
        byte[] bytes = [0x83, 0xF8, 0x02, 0x76, 0x01, 0xF4, 0x89, 0xC3, 0xD1, 0xE3, 0x2E, 0xFF, 0xA7, 0x24, 0x0E];
        for (int i = 0; i < bytes.Length; i++) {
            Memory.UInt8[physical + (uint)i] = bytes[i];
        }
        CfgInstruction cmp = Parser.ParseInstructionAt(At(0x100));
        CfgInstruction jbe = Parser.ParseInstructionAt(At(0x103));
        CfgInstruction mov = Parser.ParseInstructionAt(At(0x106));
        CfgInstruction shl = Parser.ParseInstructionAt(At(0x108));
        CfgInstruction transfer = Parser.ParseInstructionAt(At(0x10A));
        WireEdge(cmp, jbe);
        WireEdge(jbe, mov);
        WireEdge(mov, shl);
        WireEdge(shl, transfer);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
    }

    [Fact]
    public void RecognizesCompareOnPreTransformRegister() {
        // mov bx,ax ; shl bx,1 ; cmp ax,2 ; ja ; jmp [cs:bx+T]
        // The compare bounds the register the index was copied from: bx = 2*ax <= 4, so only the
        // even entries 0,2,4 are reachable. The fixed chain refused this order on purpose; the
        // backward walk carries the bound on AX through the mov and resolves it.
        CfgInstruction transfer = Chain(0x100,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x83, 0xF8, 0x02,
            0x77, 0x02,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
        Assert.Equal(2, shape.Step);
    }

    [Fact]
    public void RefusesForeignWriteToTrackedRegister() {
        // cmp ax,2 ; ja ; mov bx,ax ; inc bx ; shl bx,1 ; jmp
        // inc writes the tracked index register and is not an accepted transformation.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x07,
            0x89, 0xC3,
            0x43,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void RefusesCbwBoundAtLeast80() {
        // cmp al,0xC8 ; ja ; cbw ; mov bx,ax ; shl bx,1 ; jmp
        CfgInstruction transfer = Chain(0x100,
            0x3C, 0xC8,
            0x77, 0x06,
            0x98,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void RecognizesCbwBoundBelow80() {
        // cmp al,0x20 ; ja ; cbw ; mov bx,ax ; shl bx,1 ; jmp
        CfgInstruction transfer = Chain(0x100,
            0x3C, 0x20,
            0x77, 0x06,
            0x98,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        Assert.NotNull(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void RecognizesPushImmBeforeTransfer() {
        // cmp ax,2 ; ja +7 ; mov bx,ax ; shl bx,1 ; push 0x0030 ; jmp [cs:bx+T]
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x07,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x68, 0x30, 0x00,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
        Assert.Equal(Table, shape.BaseOffset);
    }

    [Fact]
    public void RecognizesPushRegSetByMovImm() {
        // cmp ax,2 ; ja +9 ; mov bx,ax ; mov dx,0x0030 ; shl bx,1 ; push dx ; jmp [cs:bx+T]
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x09,
            0x89, 0xC3,
            0xBA, 0x30, 0x00,
            0xD1, 0xE3,
            0x52,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
    }

    [Fact]
    public void RefusesPushRegSetByNonMovImm() {
        // cmp ax,2 ; ja +8 ; mov dx,ax ; mov bx,ax ; shl bx,1 ; push dx ; jmp [cs:bx+T]
        // dx's last write is `mov dx,ax`, not an immediate setter: argument push.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x08,
            0x89, 0xC2,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x52,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void RefusesPushOfIndexRegister() {
        // cmp ax,2 ; ja +6 ; mov bx,ax ; shl bx,1 ; push bx ; jmp [cs:bx+T]
        // Pushing the index register itself can never be a manufactured return.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x06,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x53,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void RecognizesIndirectCall() {
        // cmp ax,2 ; ja +5 ; mov bx,ax ; shl bx,1 ; call [cs:bx+T]
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xF8, 0x02,
            0x77, 0x05,
            0x89, 0xC3,
            0xD1, 0xE3,
            0x2E, 0xFF, 0x97, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.True(shape.IsCall);
    }

    [Fact]
    public void RecognizesCompareOnScaledValue() {
        // mov al,[500] ; mov ah,0 ; shl ax,1 ; cmp ax,2 ; ja +5 ; xchg bx,ax ; shl bx,1 ; jmp
        // The compare bounds the already scaled ax, so the reachable bx indices are the even
        // multiples {0,4}: bound 1, step 4.
        CfgInstruction transfer = Chain(0x100,
            0xA0, 0x00, 0x05,
            0xB4, 0x00,
            0xD1, 0xE0,
            0x83, 0xF8, 0x02,
            0x77, 0x05,
            0x93,
            0xD1, 0xE3,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(1, shape.Bound);
        Assert.Equal(4, shape.Step);
        Assert.Equal(Table, shape.BaseOffset);
    }

    [Fact]
    public void RecognizesLowerAndUpperBounds() {
        // mov si,bx ; shl si,1 ; cmp si,4 ; jb out ; cmp si,8 ; ja out ; jmp [es:si+T]
        // The scaled si stays in [4,8], so the unscaled index stays in [2,4]: three entries
        // read at T+4, T+6, T+8 - the lower bound folds into BaseOffset.
        CfgInstruction transfer = Chain(0x100,
            0x89, 0xDE,
            0xD1, 0xE6,
            0x83, 0xFE, 0x04,
            0x72, 0x0A,
            0x83, 0xFE, 0x08,
            0x77, 0x05,
            0x26, 0xFF, 0xA4, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(2, shape.Bound);
        Assert.Equal(2, shape.Step);
        Assert.Equal(Table + 4, shape.BaseOffset);
    }

    [Fact]
    public void RefusesSignedGuardJump() {
        // cmp si,4 ; jl out ; cmp si,8 ; ja out ; jmp [es:si+T]
        // jl is a signed condition the polarity table does not cover: the bound of the second
        // guard is still valid, but the jl compare has no verifiable guard.
        CfgInstruction transfer = Chain(0x100,
            0x89, 0xDE,
            0xD1, 0xE6,
            0x83, 0xFE, 0x04,
            0x7C, 0x0A,
            0x83, 0xFE, 0x08,
            0x77, 0x05,
            0x26, 0xFF, 0xA4, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }

    [Fact]
    public void UnscaledMaskIndexReadsWholeWordEntries() {
        // and bx,0x1F ; jmp [cs:bx+T]
        // The index value is a byte offset into a word table, so only the even offsets
        // 0,2,...,30 are entries: bound 15, step 2, base offset T.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xE3, 0x1F,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(15, shape.Bound);
        Assert.Equal(2, shape.Step);
        Assert.Equal(Table, shape.BaseOffset);
    }

    [Fact]
    public void ScaledMaskIndexKeepsEveryEntry() {
        // and bx,0x1F ; add bx,bx ; jmp [cs:bx+T]
        // The index is scaled by the add, so all 32 entries stay reachable.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xE3, 0x1F,
            0x01, 0xDB,
            0x2E, 0xFF, 0xA7, 0x24, 0x0E);
        SwitchTableRecognizer.TableShape? shape = new SwitchTableRecognizer().Recognize(transfer);
        Assert.NotNull(shape);
        Assert.Equal(31, shape.Bound);
        Assert.Equal(2, shape.Step);
        Assert.Equal(Table, shape.BaseOffset);
    }

    [Fact]
    public void RefusesGuardWithBothEdgesReachingTransfer() {
        // cmp si,4 ; jbe +0 ; jmp [es:si+T]
        // jbe jumps to its own fallthrough: both edges reach the transfer, so the compare
        // constrains nothing and no bound exists.
        CfgInstruction transfer = Chain(0x100,
            0x83, 0xFE, 0x04,
            0x76, 0x00,
            0x26, 0xFF, 0xA4, 0x24, 0x0E);
        Assert.Null(new SwitchTableRecognizer().Recognize(transfer));
    }
}

