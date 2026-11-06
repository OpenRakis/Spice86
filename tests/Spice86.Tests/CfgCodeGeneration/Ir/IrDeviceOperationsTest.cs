namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The device-operations pass replaces a recognized port sequence by the device call the sequence
/// performs (<c>Cmos.ReadRegister</c> / <c>Cmos.WriteRegister</c>, <c>Pit.ProgramChannel</c>,
/// <c>Vga.SetPaletteEntry</c>, the sequencer and graphics controller index/data pairs,
/// <c>Opl.WriteRegister</c>). The positive cases are covered end to end by the
/// <c>device_operations</c> ASM fixture and its golden; these tests pin the refusal conditions:
/// something between the accesses could reach the machine (DO-2), a moved value changed under it
/// (DO-3), a block boundary (DO-4), a port or control word hidden behind a narrowing cast (DO-5),
/// an access that can fault or deliver an interrupt on the last statement, a write argument that
/// is not pure, a word-write argument that cannot be evaluated twice (DO-6), and a port the
/// dumped machine did not map to the expected device (DO-7).
/// </summary>
public sealed class IrDeviceOperationsTest {
    /// <summary>The handler types a machine maps its device ports to; machine-less tests supply it
    /// explicitly so a refusal comes from the tested condition, not from an empty port map.</summary>
    private static readonly IReadOnlyDictionary<ushort, Type> DevicePorts =
        new Dictionary<ushort, Type> {
            [CmosPorts.Address] = typeof(RealTimeClock),
            [CmosPorts.Data] = typeof(RealTimeClock),
            [PitTimer.PitChannel0Port] = typeof(PitTimer),
            [PitTimer.PitChannel2Port] = typeof(PitTimer),
            [PitTimer.PitControlPort] = typeof(PitTimer),
            [(ushort)Ports.SequencerAddress] = typeof(VgaIoPortHandler),
            [(ushort)Ports.SequencerData] = typeof(VgaIoPortHandler),
            [(ushort)Ports.DacAddressWriteIndex] = typeof(VgaIoPortHandler),
            [(ushort)Ports.DacData] = typeof(VgaIoPortHandler),
            [(ushort)Ports.GraphicsControllerAddress] = typeof(VgaIoPortHandler),
            [(ushort)Ports.GraphicsControllerData] = typeof(VgaIoPortHandler),
            [0x388] = typeof(Opl3Fm),
            [0x389] = typeof(Opl3Fm)
        };

    private static PortWriteCallNode Write(ushort port, IVisitableAstNode value) =>
        new(BitWidth.BYTE_8, new ConstantNode(DataType.UINT16, port), value);

    private static PortWriteCallNode WriteWord(ushort port, IVisitableAstNode value) =>
        new(BitWidth.WORD_16, new ConstantNode(DataType.UINT16, port), value);

    private static BinaryOperationNode ReadAssign(ushort port) =>
        new(DataType.UINT8, new RegisterNode(DataType.UINT8, 0), BinaryOperation.ASSIGN,
            new PortReadCallNode(BitWidth.BYTE_8, new ConstantNode(DataType.UINT16, port)));

    /// <summary>One instruction per distinct origin, a block holding <paramref name="nodes"/> in
    /// order and the method over it; entries are the first statement's instruction.</summary>
    private static (IrMethod Method, IrBlock Block) MethodOf(params IVisitableAstNode[] nodes) =>
        MethodOf(nodes.Select(_ => (ICfgNode?)null).ToArray(), nodes);

    private static (IrMethod Method, IrBlock Block) MethodOf(ICfgNode?[] origins,
        IVisitableAstNode[] nodes) {
        CfgInstruction fallback = CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0));
        IrBlock block = new(0);
        HashSet<ICfgNode> instructions = [];
        for (int i = 0; i < nodes.Length; i++) {
            ICfgNode origin = origins[i] ?? fallback;
            instructions.Add(origin);
            block.Statements.Add(IrStatement.Create(nodes[i], origin));
        }
        CfgInstruction entry = instructions.OfType<CfgInstruction>().FirstOrDefault() ?? fallback;
        block.Terminator = new IrExit(entry, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = new("M", [block], [new IrEntry(entry, block)], instructions,
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, block);
    }

    /// <summary>An instruction with a CPU fault edge, so a statement of its body is
    /// <see cref="IrEffects.MayFault"/>.</summary>
    private static CfgInstruction Faulting(ushort offset) {
        CfgInstruction instruction =
            CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));
        instruction.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> {
                CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, 0x900))
            };
        return instruction;
    }

    [Fact]
    public void UnrelatedPortBetweenTheAccessesKeepsSinglePortCalls() {
        // mov al,0x0B / out 0x70,al / out 0x80,al (another device) / in al,0x71 / hlt
        TestInstructionHelper helper = new();
        CfgInstruction mov = Parse(helper, 0x0000, 0xB0, 0x0B);
        CfgInstruction select = Parse(helper, 0x0002, 0xE6, 0x70);
        CfgInstruction other = Parse(helper, 0x0004, 0xE6, 0x80);
        CfgInstruction read = Parse(helper, 0x0006, 0xE4, 0x71);
        CfgInstruction hlt = Parse(helper, 0x0008, 0xF4);
        IrMethod method = Lift(Linear(mov, select, other, read, hlt));

        IrPassPipeline.Run(method, IrPass.DeviceOperations, verify: true, DevicePorts);

        Nodes(method).OfType<DeviceOperationValueNode>().Should().BeEmpty(
            "another device's access between the select and the read blocks the merge (DO-2)");
        Nodes(method).OfType<PortReadCallNode>().Should().NotBeEmpty();
    }

    [Fact]
    public void MemoryAccessBetweenTheAccessesKeepsSinglePortCalls() {
        // mov al,0x0B / out 0x70,al / mov cl,[bx] / in al,0x71 / hlt
        TestInstructionHelper helper = new();
        CfgInstruction mov = Parse(helper, 0x0000, 0xB0, 0x0B);
        CfgInstruction select = Parse(helper, 0x0002, 0xE6, 0x70);
        CfgInstruction load = Parse(helper, 0x0004, 0x8A, 0x0F);
        CfgInstruction read = Parse(helper, 0x0006, 0xE4, 0x71);
        CfgInstruction hlt = Parse(helper, 0x0008, 0xF4);
        IrMethod method = Lift(Linear(mov, select, load, read, hlt));

        IrPassPipeline.Run(method, IrPass.DeviceOperations, verify: true, DevicePorts);

        Nodes(method).OfType<DeviceOperationValueNode>().Should().BeEmpty(
            "a memory read between the select and the read blocks the merge (DO-2)");
        Nodes(method).OfType<PortReadCallNode>().Should().NotBeEmpty();
    }

    [Fact]
    public void EventCheckBetweenTheAccessesKeepsSinglePortCalls() {
        // DO-2: a hardware event check between the select and the read may run a handler.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            new CheckExternalEventsNode(new SegmentedAddress(0x1000, 4),
                HardwareInterruptEffectBuilder.Conservative),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the event check may run an interrupt handler mid-sequence");
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void SpeculativeGuardBetweenTheAccessesKeepsSinglePortCalls() {
        // DO-2: a speculative guard reads memory, which may be device-backed.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            new SpeculativeGuardNode(new SegmentedAddress(0x1000, 4), ["f0"]),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the guard's memory read may observe device state");
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void CallBetweenTheAccessesKeepsSinglePortCalls() {
        // DO-2: an opaque call may itself reach the device.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            new HltHelperNode(),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "a call between the select and the read may do anything");
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void FaultBetweenTheAccessesKeepsSinglePortCalls() {
        // DO-2: a faultable statement between the select and the read can hand the machine to the
        // interpreter, which re-runs the interpreter from the faulting instruction - the merged
        // register access must not have moved past it.
        CfgInstruction faultingAssign = Faulting(0x0003);
        (IrMethod method, IrBlock block) = MethodOf(
            [null, faultingAssign, null],
            [Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
                new BinaryOperationNode(DataType.UINT8, new RegisterNode(DataType.UINT8, 1),
                    BinaryOperation.ASSIGN, new ConstantNode(DataType.UINT8, 7)),
                ReadAssign(CmosPorts.Data)]);
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the mid-sequence fault edge must keep the single-port calls");
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void FaultingLastReadKeepsSinglePortCalls() {
        // DO-1 puts the merge at the last access: when that access can fault, moving the earlier
        // port effects to it would run them on the fault path.
        CfgInstruction faultingRead = Faulting(0x0004);
        (IrMethod method, IrBlock block) = MethodOf(
            [null, faultingRead],
            [Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
                ReadAssign(CmosPorts.Data)]);
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the read's fault edge must keep the earlier select untouched");
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeOfType<BinaryOperationNode>();
    }

    [Fact]
    public void FaultingLastWriteKeepsSinglePortCalls() {
        CfgInstruction faultingWrite = Faulting(0x0004);
        (IrMethod method, IrBlock block) = MethodOf(
            [null, faultingWrite],
            [Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
                Write(CmosPorts.Data, new ConstantNode(DataType.UINT8, 0x55))]);
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the write's fault edge must keep the earlier select untouched");
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void ImpureLastWriteArgumentKeepsSinglePortCalls() {
        // A write argument evaluated at the last access still must be pure: an ALU call writes
        // flags and cannot move.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            Write(CmosPorts.Data,
                new AluCallValueNode(DataType.UINT8, AluOperation.Add, BitWidth.BYTE_8,
                    new RegisterNode(DataType.UINT8, 0), new ConstantNode(DataType.UINT8, 1))));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the ALU argument changes flags and cannot move to the merge");
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void MemoryReadingWordValueKeepsThePortWrite() {
        // A word write's argument is evaluated twice by the indexed operation, so a memory read
        // (which can observe the first byte's device side effect) cannot serve as it.
        (IrMethod method, IrBlock block) = MethodOf(
            WriteWord((ushort)Ports.GraphicsControllerAddress,
                new AbsolutePointerNode(DataType.UINT16, new ConstantNode(DataType.UINT32, 0x500))));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the memory read cannot be duplicated by the merge");
        block.Statements.Should().HaveCount(1);
        block.Statements[0].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void FlagWritingWordValueKeepsThePortWrite() {
        // An ALU value call writes flags: duplicating it on the index and data arguments would run
        // the flag update twice.
        (IrMethod method, IrBlock block) = MethodOf(
            WriteWord((ushort)Ports.GraphicsControllerAddress,
                new AluCallValueNode(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16,
                    new RegisterNode(DataType.UINT16, 0), new ConstantNode(DataType.UINT16, 1))));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the flag-writing value cannot be duplicated by the merge");
        block.Statements.Should().HaveCount(1);
        block.Statements[0].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void UnmappedPortKeepsSinglePortCalls() {
        // DO-7: the machine the program was dumped from has no handler on 0x70/0x71, so the
        // generated Cmos call would never reach a real clock.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, new Dictionary<ushort, Type>());

        combined.Should().Be(0, "an unmapped port never reaches the CMOS device");
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void WrongPortOwnerKeepsSinglePortCalls() {
        // DO-7: 0x71 is mapped, but to a different device than the clock the call drives.
        Dictionary<ushort, Type> ports = new(DevicePorts) {
            [CmosPorts.Data] = typeof(PitTimer)
        };
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new ConstantNode(DataType.UINT8, 0x0B)),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, ports);

        combined.Should().Be(0, "a port owned by another device keeps its single-port calls");
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void DataPortStartKeepsSinglePortCalls() {
        // DO-7: the chip treats even ports as the address and odd as data, so a write starting on
        // 0x389 is a data write, not the start of a register pair - even when 0x38A is owned too.
        Dictionary<ushort, Type> ports = new(DevicePorts) {
            [0x38A] = typeof(Opl3Fm)
        };
        (IrMethod method, IrBlock block) = MethodOf(
            Write(0x389, new ConstantNode(DataType.UINT8, 0x20)),
            Write(0x38A, new ConstantNode(DataType.UINT8, 0xF1)));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, ports);

        combined.Should().Be(0, "an odd start port is the data half of a pair, never an address");
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void NarrowedPortValueKeepsSinglePortCalls() {
        // (byte)0x3C4 is port 0xC4, not the sequencer address: only a real numeric evaluation may
        // recognize the port, the raw inner constant must not.
        (IrMethod method, IrBlock block) = MethodOf(
            new PortWriteCallNode(BitWidth.BYTE_8,
                new TypeConversionNode(DataType.UINT8,
                    new ConstantNode(DataType.UINT16, (ushort)Ports.SequencerAddress)),
                new ConstantNode(DataType.UINT8, 0x02)),
            Write((ushort)Ports.SequencerData, new ConstantNode(DataType.UINT8, 0x0F)));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the narrowed 0xC4 is not the sequencer index port");
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void ChangedOperandKeepsSinglePortCalls() {
        // DO-3: the select's argument still reads the AL version the sequence saw; the guard is
        // checked on a hand-built block.
        (IrMethod method, IrBlock block) = MethodOf(
            Write(CmosPorts.Address, new RegisterNode(DataType.UINT8, 0)),
            new BinaryOperationNode(DataType.UINT8, new RegisterNode(DataType.UINT8, 0),
                BinaryOperation.ASSIGN, new ConstantNode(DataType.UINT8, 0)),
            ReadAssign(CmosPorts.Data));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the select's AL version no longer holds at the read");
        block.Statements.Should().HaveCount(3);
        block.Statements[0].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void LocalTempOverwriteKeepsSinglePortCalls() {
        // The other half of DO-3: the select's argument reads a temporary a statement between the
        // accesses rewrites (SSA does not cover temporaries).
        (IrMethod method, IrBlock block) = MethodOf(
            new VariableDeclarationNode(DataType.UINT8, "t0", new ConstantNode(DataType.UINT8, 0x0B)),
            Write(CmosPorts.Address, new VariableReferenceNode(DataType.UINT8, "t0")),
            new BinaryOperationNode(DataType.UINT8, new VariableReferenceNode(DataType.UINT8, "t0"),
                BinaryOperation.ASSIGN, new ConstantNode(DataType.UINT8, 0)),
            Write(CmosPorts.Data, new ConstantNode(DataType.UINT8, 0x55)));
        IrSsa ssa = IrSsa.Build(method);

        int combined = IrDeviceOperations.Apply(method, ssa, DevicePorts);

        combined.Should().Be(0, "the select's temporary was rewritten before the data write");
        block.Statements.Should().HaveCount(4);
        block.Statements[3].Node.Should().BeOfType<PortWriteCallNode>();
    }

    [Fact]
    public void NonLowHighControlWordKeepsSinglePortCalls() {
        // mov al,0x00 / out 0x43,al (latch command, not low/high) / mov al,0x05 / out 0x40,al /
        // mov al,0x00 / out 0x40,al / hlt
        TestInstructionHelper helper = new();
        CfgInstruction control = Parse(helper, 0x0000, 0xB0, 0x00);
        CfgInstruction writeControl = Parse(helper, 0x0002, 0xE6, 0x43);
        CfgInstruction low = Parse(helper, 0x0004, 0xB0, 0x05);
        CfgInstruction writeLow = Parse(helper, 0x0006, 0xE6, 0x40);
        CfgInstruction high = Parse(helper, 0x0008, 0xB0, 0x00);
        CfgInstruction writeHigh = Parse(helper, 0x000A, 0xE6, 0x40);
        CfgInstruction hlt = Parse(helper, 0x000C, 0xF4);
        IrMethod method = Lift(Linear(control, writeControl, low, writeLow, high, writeHigh, hlt));

        IrPassPipeline.Run(method, IrPass.DeviceOperations, verify: true, DevicePorts);

        Nodes(method).OfType<DeviceOperationCallNode>().Should().BeEmpty(
            "a latch command is not channel programming");
        Nodes(method).OfType<PortWriteCallNode>().Should().NotBeEmpty();
    }

    [Fact]
    public void BlockBoundaryKeepsSinglePortCalls() {
        // mov al,0x0B / out 0x70,al in one block; in al,0x71 / hlt in the block the jump reaches:
        // matching never crosses a block boundary (DO-4).
        TestInstructionHelper helper = new();
        CfgInstruction mov = Parse(helper, 0x0000, 0xB0, 0x0B);
        CfgInstruction select = Parse(helper, 0x0002, 0xE6, 0x70);
        CfgInstruction jump = Parse(helper, 0x0004, 0xEB, 0x00);
        CfgInstruction read = Parse(helper, 0x0006, 0xE4, 0x71);
        CfgInstruction hlt = Parse(helper, 0x0008, 0xF4);
        CfgBlock firstBlock = Block(1, Chain(mov, select, jump));
        CfgBlock secondBlock = Block(2, Chain(read, hlt));
        jump.Successors.Add(read);
        jump.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { read };
        IrMethod method = Lift(firstBlock, secondBlock);

        IrPassPipeline.Run(method, IrPass.DeviceOperations, verify: true, DevicePorts);

        Nodes(method).OfType<DeviceOperationValueNode>().Should().BeEmpty(
            "the select and the read live in different blocks");
        Nodes(method).OfType<PortReadCallNode>().Should().NotBeEmpty();
    }
}
