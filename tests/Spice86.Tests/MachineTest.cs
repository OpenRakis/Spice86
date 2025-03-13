namespace Spice86.Tests;

using JetBrains.Annotations;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.IO;
using System.Text;

using Xunit;

public class MachineTest
{
    static MachineTest()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    public static IEnumerable<object[]> GetCfgCpuConfigurations()
    {
        yield return new object[] { false };
        yield return new object[] { true };
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestAdd(bool enableCfgCpu)
    {
        TestOneBin("add", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestBcdcnv(bool enableCfgCpu)
    {
        TestOneBin("bcdcnv", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestBitwise(bool enableCfgCpu)
    {
        byte[] expected = GetExpected("bitwise");
        // dosbox values
        expected[0x9F] = 0x12;
        expected[0x9D] = 0x12;
        expected[0x9B] = 0x12;
        expected[0x99] = 0x12;
        TestOneBin("bitwise", expected, enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestCmpneg(bool enableCfgCpu)
    {
        TestOneBin("cmpneg", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestControl(bool enableCfgCpu)
    {
        byte[] expected = GetExpected("control");
        // dosbox values
        expected[0x1] = 0x78;
        TestOneBin("control", expected, enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestDatatrnf(bool enableCfgCpu)
    {
        TestOneBin("datatrnf", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestDiv(bool enableCfgCpu)
    {
        TestOneBin("div", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestInterrupt(bool enableCfgCpu)
    {
        TestOneBin("interrupt", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJump1(bool enableCfgCpu)
    {
        TestOneBin("jump1", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJump2(bool enableCfgCpu)
    {
        TestOneBin("jump2", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJmpmov(bool enableCfgCpu)
    {
        // 0x4001 in little endian
        byte[] expected = new byte[] { 0x01, 0x40 };
        Machine emulator = TestOneBin("jmpmov", expected, enableCfgCpu);
        State state = emulator.CpuState;
        uint endAddress = MemoryUtils.ToPhysicalAddress(state.CS, state.IP);
        // Last instruction HLT is one byte long and is at 0xF400C
        Assert.Equal((uint)0xF400D, endAddress);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestMul(bool enableCfgCpu)
    {
        byte[] expected = GetExpected("mul");
        // dosbox values
        expected[0xA2] = 0x2;
        expected[0x9E] = 0x2;
        expected[0x9C] = 0x3;
        expected[0x9A] = 0x3;
        expected[0x98] = 0x2;
        expected[0x96] = 0x2;
        expected[0x92] = 0x2;
        expected[0x73] = 0x2;
        TestOneBin("mul", expected, enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestRep(bool enableCfgCpu)
    {
        TestOneBin("rep", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestRotate(bool enableCfgCpu)
    {
        TestOneBin("rotate", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSegpr(bool enableCfgCpu)
    {
        Machine machine = TestOneBin("segpr", enableCfgCpu);
        if (enableCfgCpu) {
            // Here, a division by 0 occurred causing a CPU fault. It is handled by an interrupt handler.
            CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
            CfgInstruction? divBy0 = currentInstructions.GetAtAddress(new(0xF000, 0x005F));
            CfgInstruction? divBy0HandlerEntry = currentInstructions.GetAtAddress(new(0xF000, 0x1100));
            CfgInstruction? divBy0HandlerIret = currentInstructions.GetAtAddress(new(0xF000, 0x1111));
            CfgInstruction? divBy0NextInstruction = currentInstructions.GetAtAddress(new(0xF000, 0x0065));
            Assert.NotNull(divBy0);
            Assert.NotNull(divBy0HandlerEntry);
            Assert.NotNull(divBy0HandlerIret);
            Assert.NotNull(divBy0NextInstruction);
            // Check that the int handler is linked to the division by 0 as a normal successor
            Assert.Contains(divBy0HandlerEntry, divBy0.Successors);
            Assert.Contains(divBy0HandlerEntry, divBy0.SuccessorsPerType[InstructionSuccessorType.Normal]);
            // Check that the instruction next to the div by 0 to which the handler returned to  is linked to the division by 0 as a regular "Call to return" link.
            // Side-note, normally, div by 0 int handler should return to the div instruction. However, here the handler edits the call stack making it return to the next instruction which is how a regular function call in a high level language would behave
            Assert.Contains(divBy0NextInstruction, divBy0.Successors);
            Assert.Contains(divBy0NextInstruction, divBy0.SuccessorsPerType[InstructionSuccessorType.CallToReturn]);
            // Check that IRET is normally connected to the return target
            Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.Successors);
            Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.SuccessorsPerType[InstructionSuccessorType.Normal]);
        }
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestShifts(bool enableCfgCpu)
    {
        TestOneBin("shifts", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestStrings(bool enableCfgCpu)
    {
        TestOneBin("strings", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSub(bool enableCfgCpu)
    {
        TestOneBin("sub", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSelfModifyValue(bool enableCfgCpu)
    {
        byte[] expected = new byte[4];
        expected[0x00] = 0x01;
        expected[0x01] = 0x00;
        expected[0x02] = 0xff;
        expected[0x03] = 0xff;
        Machine machine = TestOneBin("selfmodifyvalue", expected, enableCfgCpu);
        if (enableCfgCpu) {
            CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
            CfgInstruction? instruction = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x008));
            Assert.NotNull(instruction);
            if (instruction is MovRegImm16 movAxModifiedImm) {
                InstructionField<ushort> immField = movAxModifiedImm.ValueField;
                // Code should have been modified so instruction should use memory and not stored value
                Assert.False(immField.UseValue);
                Assert.Equal(instruction.Address.Linear + 1, immField.PhysicalAddress);
            } else {
                Assert.Fail("Should have been MOV AX, xxx");
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSelfModifyInstructions(bool enableCfgCpu)
    {
        byte[] expected = new byte[6];
        expected[0x00] = 0x03;
        expected[0x01] = 0x00;
        expected[0x02] = 0x02;
        expected[0x03] = 0x00;
        expected[0x04] = 0x01;
        expected[0x05] = 0x00;
        TestOneBin("selfmodifyinstructions", expected, enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestExternalInt(bool enableCfgCpu)
    {
        byte[] expected = new byte[6];
        expected[0x00] = 0x01;
        TestOneBin("externalint", expected, enableCfgCpu, 0xFFFFFFF, true);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, bool enableCfgCpu)
    {
        byte[] expected = GetExpected(binName);
        return TestOneBin(binName, expected, enableCfgCpu);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, byte[] expected, bool enableCfgCpu, long maxCycles = 100000L, bool enablePit = false)
    {
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(binName: binName, enableCfgCpu: enableCfgCpu, maxCycles: maxCycles, enablePit: enablePit, recordData: false).Create();
        spice86DependencyInjection.ProgramExecutor.Run();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        CompareMemoryWithExpected(memory, expected, 0, expected.Length);
        return machine;
    }

    /// <summary>
    /// Test 386 but not protected mode. <br/>
    /// test386.asm was assembled with 'make' which invokes NASM <br/>
    /// The environement was GNU/Linux Ubuntu 24.04.1, and make and NASM were installed from the software repositories with apt package manager. <br/>
    /// </summary>
    /// <remarks>
    /// The binary assembled file must be installed at physical address 0xf0000 and
    /// aliased at physical address 0xffff0000. <br/> The jump at resetVector should align
    /// with the CPU reset address 0xfffffff0, which will transfer control to f000:0045.<br/><br/>
    /// All memory accesses will remain within the first 1MB.
    /// </remarks>
    /// <param name="enableCfgCpu"></param>
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void Test386ButNotProtectedMode(bool enableCfgCpu) {
        //Arrange
        string binName = "test386";
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: binName, enableCfgCpu: enableCfgCpu,
            enablePit: false, recordData: false, maxCycles: long.MaxValue,
            failOnUnhandledPort: true).Create();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        List<(ushort Port, byte Value)> portValues = new();
        var debugPortsHandler = new Test386ButNotProtectedModeHandler(machine.IoPortDispatcher,
            (port, value) => portValues.Add((port, value)));

        //Act
        spice86DependencyInjection.ProgramExecutor.Run();


        //Assert
        Assert.Equal(0x999, portValues.Last().Port);
        //Normally, we should test if 0xFF has been posted last, at port 0x999
        if (enableCfgCpu) {
            //test3865.asm stops at 2 tests...?!
            Assert.Equal(2, portValues.Count);
        } else {
            //test3865.asm stops at 6 tests...?!
            Assert.Equal(6, portValues.Count);
        }
    }

    private class Test386ButNotProtectedModeHandler : IIOPortHandler {
        private readonly IOPortDispatcher _ioPortDispatcher;
        private readonly Action<ushort, byte> _assert;
        public Test386ButNotProtectedModeHandler(IOPortDispatcher ioPortDispatcher, Action<ushort, byte> assert) {
            this._ioPortDispatcher = ioPortDispatcher;
            this._assert = assert;
            AddIoPortHandlers();
        }

        private void AddIoPortHandlers() {
            _ioPortDispatcher.AddIOPortHandler(0x999, this);
            _ioPortDispatcher.AddIOPortHandler(0x998, this);
        }

        public ushort ReadWord(ushort port) {
            throw new NotImplementedException();
        }

        public uint ReadDWord(ushort port) {
            throw new NotImplementedException();
        }

        public void WriteWord(ushort port, ushort value) {
            throw new NotImplementedException();
        }

        public void WriteDWord(ushort port, uint value) {
            throw new NotImplementedException();
        }

        public void UpdateLastPortRead(ushort port) {
            //NOP
        }

        public void UpdateLastPortWrite(ushort port, uint value) {
            //NOP
        }

        public byte ReadByte(ushort port) {
            throw new NotImplementedException();
        }

        public void WriteByte(ushort port, byte value) {
            if(port == 0x998) {
                string asciiValue = Encoding.ASCII.GetString(new byte[] { value });
                Log.Logger.Warning($"Port 0x400: {asciiValue}");
            }
            _assert(port, value);
        }
    }

    private static byte[] GetExpected(string binName)
    {
        string resPath = $"Resources/cpuTests/res/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    [AssertionMethod]
    private void CompareMemoryWithExpected(IMemory memory, byte[] expected, int start, int end)
    {
        byte[] actual = memory.ReadRam();
        Assert.Equal(expected[start..end], actual[start..end]);
    }
}
