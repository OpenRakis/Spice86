namespace Spice86.Tests.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Direct tests of <see cref="RegisterWriteSet.MayWrite"/> on hand-assembled instructions: the
/// mask must name exactly the written registers where the shape is understood, and the full
/// all-but-CS mask anywhere it is not.
/// </summary>
public class RegisterWriteSetTest : SpeculativeTestBase {
    private const ushort Seg = 0xF000;

    private CfgInstruction Parse(params byte[] opcodes) {
        uint physical = (uint)(Seg * 16 + 0x100);
        for (int i = 0; i < opcodes.Length; i++) {
            Memory.UInt8[physical + (uint)i] = opcodes[i];
        }
        return Parser.ParseInstructionAt(new SegmentedAddress(Seg, 0x100));
    }

    private static ushort Regs(params RegisterBits[] bits) {
        ushort mask = 0;
        foreach (RegisterBits bit in bits) {
            mask |= (ushort)bit;
        }
        return mask;
    }

    private static ushort AllExcept(params RegisterBits[] bits) =>
        (ushort)((ushort)RegisterBits.All & ~Regs(bits));

    [Fact]
    public void MovRegImmediateMarksOnlyThatRegister() {
        // mov di,0x1234
        Assert.Equal(Regs(RegisterBits.Di), RegisterWriteSet.MayWrite(Parse(0xBF, 0x34, 0x12)));
    }

    [Fact]
    public void MovReg8HighMarksWholeRegister() {
        // mov ah,0x05
        Assert.Equal(Regs(RegisterBits.Ax), RegisterWriteSet.MayWrite(Parse(0xB4, 0x05)));
    }

    [Fact]
    public void MovToSegmentRegisterMarksSegment() {
        // mov ds,ax
        Assert.Equal(Regs(RegisterBits.Ds), RegisterWriteSet.MayWrite(Parse(0x8E, 0xD8)));
    }

    [Fact]
    public void XchgMarksSwappedRegisters() {
        // xchg ax,bx - expands to temp/assign statements naming AX and BX
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Bx), RegisterWriteSet.MayWrite(Parse(0x93)));
    }

    [Fact]
    public void AddRegImmediateMarksRegister() {
        // add ax,0x0300
        Assert.Equal(Regs(RegisterBits.Ax), RegisterWriteSet.MayWrite(Parse(0x05, 0x00, 0x03)));
    }

    [Fact]
    public void CompareWritesNothing() {
        // cmp ax,0x0002
        Assert.Equal(0, RegisterWriteSet.MayWrite(Parse(0x3D, 0x02, 0x00)));
    }

    [Fact]
    public void MemoryWriteMarksNoRegister() {
        // mov [bx],ax
        Assert.Equal(0, RegisterWriteSet.MayWrite(Parse(0x89, 0x07)));
    }

    [Fact]
    public void PopRegisterMarksRegisterAndSp() {
        // pop ax - the popped value is a StackCallValueNode that increments SP
        Assert.Equal(Regs(RegisterBits.Ax, RegisterBits.Sp), RegisterWriteSet.MayWrite(Parse(0x58)));
    }

    [Fact]
    public void PopMemoryMarksSp() {
        // pop word [bx] - the memory target marks nothing, the pop still writes SP
        Assert.Equal(Regs(RegisterBits.Sp), RegisterWriteSet.MayWrite(Parse(0x8F, 0x07)));
    }

    [Fact]
    public void MovRegRegMarksOnlyTarget() {
        // mov ax,bx - the source register is a pure read
        Assert.Equal(Regs(RegisterBits.Ax), RegisterWriteSet.MayWrite(Parse(0x89, 0xD8)));
    }

    [Fact]
    public void MovRegMemMarksOnlyTarget() {
        // mov ax,[bx+si+4] - pointer segment/offset reads are pure
        Assert.Equal(Regs(RegisterBits.Ax), RegisterWriteSet.MayWrite(Parse(0x8B, 0x40, 0x04)));
    }

    [Fact]
    public void PushMarksOnlySp() {
        // push ax - pushing a pure register read only moves SP
        Assert.Equal(Regs(RegisterBits.Sp), RegisterWriteSet.MayWrite(Parse(0x50)));
    }

    [Fact]
    public void PushaMarksOnlySp() {
        // pusha - the pushed values are reads; only SP changes
        Assert.Equal(Regs(RegisterBits.Sp), RegisterWriteSet.MayWrite(Parse(0x60)));
    }

    [Fact]
    public void PopaMarksAllGeneralRegisters() {
        // popa - every general register is reloaded, SP included
        Assert.Equal((ushort)RegisterBits.AllGeneralPurpose, RegisterWriteSet.MayWrite(Parse(0x61)));
    }

    [Fact]
    public void LeaveMarksSpAndBp() {
        // leave - SP takes BP's value and BP is popped
        Assert.Equal(Regs(RegisterBits.Sp, RegisterBits.Bp), RegisterWriteSet.MayWrite(Parse(0xC9)));
    }

    [Fact]
    public void CallIsConservative() {
        // call 0x0200
        Assert.Equal((ushort)RegisterBits.AllExceptCs, RegisterWriteSet.MayWrite(Parse(0xE8, 0x00, 0x01)));
    }

    [Fact]
    public void IntIsConservative() {
        // int 0x21
        Assert.Equal((ushort)RegisterBits.AllExceptCs, RegisterWriteSet.MayWrite(Parse(0xCD, 0x21)));
    }

    [Fact]
    public void StringOpMarksIndexRegisters() {
        // movsb expands to explicit index increments; the assignments name SI and DI
        Assert.Equal(Regs(RegisterBits.Si, RegisterBits.Di), RegisterWriteSet.MayWrite(Parse(0xA4)));
    }

    [Fact]
    public void NearJumpWritesNothing() {
        // jmp 0x0200
        Assert.Equal(0, RegisterWriteSet.MayWrite(Parse(0xE9, 0x00, 0x01)));
    }
}
