﻿namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.CPU.Registers;

using System.Text;

using Spice86.Shared.Utils;

/// <summary>
/// Represents the state of the CPU Registers and Flags.
/// </summary>
public class State : IDebuggableComponent {
    /// <summary>
    /// Gets or sets the second byte (high byte) in the general purpose EAX register
    /// <para>
    /// The EAX register is often used as an accumulator register in arithmetic operations, or to store the result of a function.
    /// </para>
    /// </summary>
    public byte AH { get => GeneralRegisters.UInt8High[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.AxIndex] = value; }

    /// <summary>
    /// Gets or sets the first byte (low byte) in the general purpose EAX register
    /// <para>
    /// The EAX register is often used as an accumulator register in arithmetic operations, or to store the result of a function.
    /// </para>
    /// </summary>
    public byte AL { get => GeneralRegisters.UInt8Low[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.AxIndex] = value; }

    /// <summary>
    /// Gets or sets the first word (16 bits) in the general purpose EAX register
    /// <para>
    /// The EAX register is often used as an accumulator register in arithmetic operations, or to store the result of a function.
    /// </para>
    /// </summary>
    public ushort AX { get => GeneralRegisters.UInt16[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.AxIndex] = value; }

    /// <summary>
    /// Gets or sets the full range of the general purpose EAX register
    /// <para>
    /// The "Extended Accumulator" register is often used as an accumulator register in arithmetic operations, or to store the result of a function.
    /// </para>
    /// </summary>
    public uint EAX { get => GeneralRegisters.UInt32[GeneralRegisters.AxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.AxIndex] = value; }

    /// <summary>
    /// Gets or sets the Base Register High Byte
    /// </summary>
    public byte BH { get => GeneralRegisters.UInt8High[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.BxIndex] = value; }
    
    /// <summary>
    /// Gets or sets the Base Register Low Byte
    /// </summary>
    public byte BL { get => GeneralRegisters.UInt8Low[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.BxIndex] = value; }
    
    /// <summary>
    /// Gets or sets the Base Register First Word
    /// </summary>
    public ushort BX { get => GeneralRegisters.UInt16[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.BxIndex] = value; }
    
    /// <summary>
    /// Gets or sets the Extended Base general purpose register
    /// </summary>
    public uint EBX { get => GeneralRegisters.UInt32[GeneralRegisters.BxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.BxIndex] = value; }

    /// <summary>
    /// Gets or sets the Counter High Byte general purpose register
    /// </summary>
    public byte CH { get => GeneralRegisters.UInt8High[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.CxIndex] = value; }

    /// <summary>
    /// Gets or sets the Counter Low Byte general purpose register
    /// </summary>
    public byte CL { get => GeneralRegisters.UInt8Low[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.CxIndex] = value; }
    /// <summary>
    /// Gets or sets the word value of the Counter general purpose register.
    /// </summary>
    public ushort CX { get => GeneralRegisters.UInt16[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.CxIndex] = value; }

    /// <summary>
    /// Gets or sets the Extended Counter general purpose register.
    /// <para>
    /// This general purpose register is often used for loop and string operations, as well as for storing function arguments.
    /// </para>
    /// </summary>
    public uint ECX { get => GeneralRegisters.UInt32[GeneralRegisters.CxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.CxIndex] = value; }

    /// <summary>
    /// Gets or sets the Data High Byte general purpose register.
    /// </summary>
    public byte DH { get => GeneralRegisters.UInt8High[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt8High[GeneralRegisters.DxIndex] = value; }

    /// <summary>
    /// Gets or sets the Data Low Byte general purpose register.
    /// </summary>
    public byte DL { get => GeneralRegisters.UInt8Low[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt8Low[GeneralRegisters.DxIndex] = value; }

    /// <summary>
    /// Gets or sets the word value of the Data general purpose register.
    /// </summary>
    public ushort DX { get => GeneralRegisters.UInt16[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.DxIndex] = value; }
    
    /// <summary>
    /// Extended Data general purpose register.
    /// <para>
    /// This general purpose register is often used for I/O operations and for storing function arguments.
    /// </para>
    /// </summary>
    public uint EDX { get => GeneralRegisters.UInt32[GeneralRegisters.DxIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.DxIndex] = value; }

    /// <summary>
    /// Gets or sets the word value of the Destination Index general purpose register.
    /// </summary>
    public ushort DI { get => GeneralRegisters.UInt16[GeneralRegisters.DiIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.DiIndex] = value; }
    
    /// <summary>
    /// Extended Destination Index general purpose register.
    /// <para>
    /// This general purpose register often used as a pointer to a destination operand in string operations.
    /// </para>
    /// </summary>
    public uint EDI { get => GeneralRegisters.UInt32[GeneralRegisters.DiIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.DiIndex] = value; }

    /// <summary>
    /// Gets or sets the word value of the Source Index general purpose register.
    /// </summary>
    public ushort SI { get => GeneralRegisters.UInt16[GeneralRegisters.SiIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.SiIndex] = value; }

    /// <summary>
    /// Extended Source Index general purpose register.
    /// <para>
    /// This general purpose register is often used as a pointer to a source operand in string operations.
    /// </para>
    /// </summary>
    public uint ESI { get => GeneralRegisters.UInt32[GeneralRegisters.SiIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.SiIndex] = value; }

    /// <summary>
    /// Gets or sets the word value of the Base Pointer general purpose register.
    /// </summary>
    public ushort BP { get => GeneralRegisters.UInt16[GeneralRegisters.BpIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.BpIndex] = value; }
    /// <summary>
    /// Gets or sets the value of the Extended Base Pointer general purpose register.
    /// <para>
    /// This general purpose register is often used as a pointer to the base of the current stack frame.
    /// </para>
    /// </summary>
    public uint EBP { get => GeneralRegisters.UInt32[GeneralRegisters.BpIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.BpIndex] = value; }

    /// <summary>
    /// Gets or sets the word value of the Stack Pointer general purpose register.
    /// </summary>
    public ushort SP { get => GeneralRegisters.UInt16[GeneralRegisters.SpIndex]; set => GeneralRegisters.UInt16[GeneralRegisters.SpIndex] = value; }
    /// <summary>
    /// Gets or sets the value of the Extended Stack Pointer general purpose register.
    /// <para>
    /// This general purpose register is often used as a pointer to the top of the current stack frame.
    /// </para>
    /// </summary>
    public uint ESP { get => GeneralRegisters.UInt32[GeneralRegisters.SpIndex]; set => GeneralRegisters.UInt32[GeneralRegisters.SpIndex] = value; }

    /// <summary>
    /// Code Segment Register. CS:IP points to the current instruction being executed.
    /// </summary>
    public ushort CS { get => SegmentRegisters.UInt16[SegmentRegisters.CsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.CsIndex] = value; }

    /// <summary>
    /// Gets or sets the Data Segment Register value.
    /// </summary>
    public ushort DS { get => SegmentRegisters.UInt16[SegmentRegisters.DsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.DsIndex] = value; }

    /// <summary>
    /// Gets or sets the Extra segment register value.
    /// </summary>
    public ushort ES { get => SegmentRegisters.UInt16[SegmentRegisters.EsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.EsIndex] = value; }
    /// <summary>
    /// Gets or sets the FS segment register value.
    /// </summary>
    public ushort FS { get => SegmentRegisters.UInt16[SegmentRegisters.FsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.FsIndex] = value; }
    /// <summary>
    /// Gets or sets the GS segment register value.
    /// </summary>
    public ushort GS { get => SegmentRegisters.UInt16[SegmentRegisters.GsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.GsIndex] = value; }

    /// <summary>
    /// Gets or sets the Stack Segment Register value.
    /// </summary>
    public ushort SS { get => SegmentRegisters.UInt16[SegmentRegisters.SsIndex]; set => SegmentRegisters.UInt16[SegmentRegisters.SsIndex] = value; }

    /// <summary>
    /// Gets or sets the Instruction Pointer segment register.
    /// </summary>
    public ushort IP { get; set; }

    /// <summary>
    /// Contains the flags of the CPU. This is the flags register.
    /// </summary>
    public Flags Flags { get; } = new();

    /// <summary>
    /// Gets or sets the value of the Overflow Flag. Set if result is too large a positive number or too small a negative number (excluding sign-bit) to fit in destination operand; cleared otherwise.
    /// </summary>
    public bool OverflowFlag { get => Flags.GetFlag(Flags.Overflow); set => Flags.SetFlag(Flags.Overflow, value); }

    /// <summary>
    /// Gets or sets the value of the Direction Flag. It controls the direction in which string instructions are processed. <br/>
    /// If set, string operations will decrement their pointer registers after processing. <br/>
    /// If unset, string operations will increment their pointer registers after processing. <br/>
    /// In this context, a 'string' refers to a sequence of bytes, words, or doublewords, depending on the instruction being executed and the setting of the Operand Size attribute.
    /// </summary>
    public bool DirectionFlag { get => Flags.GetFlag(Flags.Direction); set => Flags.SetFlag(Flags.Direction, value); }

    /// <summary>
    /// Gets or sets the value of the Interrupt Flag. If set, an interrupt must be serviced. <br/>
    /// See also: STI and CLI instructions.
    /// </summary>
    public bool InterruptFlag { get => Flags.GetFlag(Flags.Interrupt); set => Flags.SetFlag(Flags.Interrupt, value); }

    /// <summary>
    /// Gets or sets the value of the Trap Flag.
    /// <para>
    /// If set, the processor will generate a debug exception after the execution of each instruction. <br/>
    /// Setting TF puts the processor into single-step mode for debugging. In
    /// this mode, the CPU automatically generates an exception after each
    /// instruction, allowing a program to be inspected as it executes each
    /// instruction. <br/>
    /// </para> 
    /// </summary>
    public bool TrapFlag { get => Flags.GetFlag(Flags.Trap); set => Flags.SetFlag(Flags.Trap, value); }
    
    /// <summary>
    /// Gets or sets the sign flag. Set equal to high-order bit of result (0 is positive, 1 if negative).
    /// </summary>
    public bool SignFlag { get => Flags.GetFlag(Flags.Sign); set => Flags.SetFlag(Flags.Sign, value); }
    
    /// <summary>
    /// Gets or sets the value of the Zero Flag. Set if result is zero; cleared otherwise.
    /// </summary>
    public bool ZeroFlag { get => Flags.GetFlag(Flags.Zero); set => Flags.SetFlag(Flags.Zero, value); }
    
    /// <summary>
    /// Gets or sets the value of the Auxiliary Flag. Set if there is a carry from bit 3 to bit 4 of the result; cleared otherwise. <br/>
    /// </summary>
    public bool AuxiliaryFlag { get => Flags.GetFlag(Flags.Auxiliary); set => Flags.SetFlag(Flags.Auxiliary, value); }
    
    /// <summary>
    /// Gets or sets the value of the Parity Flag. <br/> Set if low-order eight bits of result contain an even number of 1 bits; cleared otherwise.
    /// </summary>
    public bool ParityFlag { get => Flags.GetFlag(Flags.Parity); set => Flags.SetFlag(Flags.Parity, value); }
    /// <summary>
    /// Gets or sets the value of the Carry Flag. <br/> Set on high-order bit carry or borrow; cleared otherwise
    /// </summary>
    public bool CarryFlag { get => Flags.GetFlag(Flags.Carry); set => Flags.SetFlag(Flags.Carry, value); }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 8 bit CPU instructions.
    /// </summary>
    public short Direction8 => (short)(DirectionFlag ? -1 : 1);

    /// <summary>
    /// Gets the offset value of the Direction Flag for 16 bit CPU instructions.
    /// </summary>
    public short Direction16 => (short)(DirectionFlag ? -2 : 2);

    /// <summary>
    /// Gets the offset value of the Direction Flag for 32 bit CPU instructions.
    /// </summary>
    public short Direction32 => (short)(DirectionFlag ? -4 : 4);

    /// <summary>
    /// Gets or sets the value of the Zero Flag instruction prefix.
    /// </summary>
    public bool? ContinueZeroFlagValue { get; set; }

    /// <summary>
    /// Gets or sets the value of the Segment Override instruction prefix.
    /// </summary>
    public uint? SegmentOverrideIndex { get; set; }

    /// <summary>
    /// The number of CPU cycles, incremented on each new instruction.
    /// </summary>
    public long Cycles { get; private set; }

    /// <summary>
    /// The physical address of the instruction pointer in memory
    /// </summary>
    public uint IpPhysicalAddress => MemoryUtils.ToPhysicalAddress(CS, IP);

    /// <summary>
    /// The physical address of the stack in memory
    /// </summary>
    public uint StackPhysicalAddress => MemoryUtils.ToPhysicalAddress(SS, SP);

    /// <summary>
    /// The CPU registers
    /// </summary>
    public GeneralRegisters GeneralRegisters { get; } = new();

    /// <summary>
    /// The CPU segment registers <br/>
    /// The segment registers are registers that store segment selectors, which are used to access different parts of memory.
    /// </summary>
    public SegmentRegisters SegmentRegisters { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the CPU is running.
    /// </summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>
    /// Sets <see cref="ContinueZeroFlagValue"/> and <see cref="SegmentOverrideIndex"/> to <c>null</c>.
    /// </summary>
    public void ClearPrefixes() {
        ContinueZeroFlagValue = null;
        SegmentOverrideIndex = null;
    }

    /// <summary>
    /// Increments the <see cref="Cycles"/> count.
    /// </summary>
    public void IncCycles() {
        Cycles++;
    }

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    public string DumpedRegFlags {
        get {
            StringBuilder res = new();
            res.Append(nameof(Cycles)).Append('=');
            res.Append(Cycles);
            res.Append(" CS:IP=").Append(ConvertUtils.ToSegmentedAddressRepresentation(CS, IP)).Append('/').Append(ConvertUtils.ToHex(MemoryUtils.ToPhysicalAddress(CS, IP)));
            res.Append(" EAX=").Append(ConvertUtils.ToHex32(EAX));
            res.Append(" EBX=").Append(ConvertUtils.ToHex32(EBX));
            res.Append(" ECX=").Append(ConvertUtils.ToHex32(ECX));
            res.Append(" EDX=").Append(ConvertUtils.ToHex32(EDX));
            res.Append(" ESI=").Append(ConvertUtils.ToHex32(ESI));
            res.Append(" EDI=").Append(ConvertUtils.ToHex32(EDI));
            res.Append(" EBP=").Append(ConvertUtils.ToHex32(EBP));
            res.Append(" ESP=").Append(ConvertUtils.ToHex32(ESP));
            res.Append(" SS=").Append(ConvertUtils.ToHex16(SS));
            res.Append(" DS=").Append(ConvertUtils.ToHex16(DS));
            res.Append(" ES=").Append(ConvertUtils.ToHex16(ES));
            res.Append(" FS=").Append(ConvertUtils.ToHex16(FS));
            res.Append(" GS=").Append(ConvertUtils.ToHex16(GS));
            res.Append(" flags=").Append(ConvertUtils.ToHex32(Flags.FlagRegister));
            res.Append(" (");
            res.Append(Flags);
            res.Append(')');
            return res.ToString();
        }
    }

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    public override string ToString() {
        return DumpedRegFlags;
    }

    /// <inheritdoc/>
    public void Accept(IEmulatorDebugger emulatorDebugger) {
        emulatorDebugger.VisitCpuState(this);
    }
}