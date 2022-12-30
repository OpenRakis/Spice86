namespace Spice86.Tests;

using FluentAssertions;

using JetBrains.Annotations;

using Serilog;

using System;
using System.IO;

using Xunit;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

public class MachineTest {
    static MachineTest() {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    [Fact]
    public void TestExecutionBreakpoints() {
        ProgramExecutor programExecutor = CreateProgramExecutor("add");
        Machine machine = programExecutor.Machine;
        machine.RecordData = true;
        State state = machine.Cpu.State;
        MachineBreakpoints machineBreakpoints = machine.MachineBreakpoints;
        int triggers = 0;
        machineBreakpoints.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CYCLES, 10, breakpoint => {
            Assert.Equal(10, state.Cycles);
            triggers++;
        }, true), true);
        // Address of cycle 10 to test multiple breakpoints
        machineBreakpoints.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.EXECUTION, 0xF001C, breakpoint => {
            Assert.Equal(0xF001C, (int)state.IpPhysicalAddress);
            triggers++;
        }, true), true);
        machineBreakpoints.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.MACHINE_STOP, 0, breakpoint => {
            Assert.Equal(0xF01A9, (int)state.IpPhysicalAddress);
            Assert.False(machine.Cpu.IsRunning);
            triggers++;
        }, true), true);
        programExecutor.Run();
        Assert.Equal(3, triggers);
    }

    [Fact]
    public void TestMemoryBreakpoints() {
        ProgramExecutor programExecutor = CreateProgramExecutor("add");
        Machine machine = programExecutor.Machine;
        MachineBreakpoints machineBreakpoints = machine.MachineBreakpoints;
        Memory memory = machine.Memory;

        // simple read
        // 2 reads, but breakpoint is removed after first
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.READ, 0, 1, true, () => {
            memory.GetUint8(0);
            memory.GetUint8(0);
        });

        // simple write
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 0, 1, true, () => {
            memory.SetUint8(0, 0);
        });

        // read / write with remove
        int readWrite0Triggered = 0;
        AddressBreakPoint readWrite0 = new AddressBreakPoint(BreakPointType.ACCESS, 0, breakpoint => { readWrite0Triggered++; }, false);
        machineBreakpoints.ToggleBreakPoint(readWrite0, true);
        memory.GetUint8(0);
        memory.SetUint8(0, 0);
        machineBreakpoints.ToggleBreakPoint(readWrite0, false);
        // Should not trigger
        memory.GetUint8(0);
        Assert.Equal(2, readWrite0Triggered);

        // Memset
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 5, 1, true, () => {
            memory.Memset(0, 0, 6);
            // Should not trigger for this
            memory.Memset(0, 0, 5);
            memory.Memset(6, 0, 5);
        });
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 5, 1, true, () => {
            memory.Memset(5, 0, 5);
        });

        // GetData
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.READ, 5, 1, true, () => {
            memory.GetSpan(5, 10);
            // Should not trigger for this
            memory.GetSpan(0, 5);
            memory.GetSpan(6, 5);
        });
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.READ, 5, 1, true, () => {
            memory.GetSpan(0, 6);
        });

        // LoadData
        byte[] data = new byte[] { 1, 2, 3 };
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 5, 1, true, () => {
            memory.LoadData(5, data);
            // Should not trigger for this
            memory.LoadData(2, data);
            memory.LoadData(6, data);
        });
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 5, 1, true, () => {
            memory.LoadData(3, data);
        });
        // Bonus test for search
        uint? address = memory.SearchValue(0, 10, data);
        Assert.NotNull(address);
        Assert.Equal(3, (int)address!);

        //MemCopy
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 10, 1, true, () => {
            memory.MemCopy(0, 10, 10);
            // Should not trigger for this
            memory.MemCopy(0, 20, 10);
        });
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.READ, 0, 1, true, () => {
            memory.MemCopy(0, 10, 10);
            // Should not trigger for this
            memory.MemCopy(1, 10, 10);
        });

        // Long reads
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.READ, 1, 2, false, () => {
            memory.GetUint16(0);
            memory.GetUint32(0);
        });

        // Long writes
        AssertAddressMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 1, 2, false, () => {
            memory.SetUint16(0, 0);
            memory.SetUint32(0, 0);
        });
        
        // Range
        AssertAddressRangeMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 0, 2, 3, false, () => {
            memory.SetUint8(0, 0);
            memory.SetUint8(1, 0);
            memory.SetUint8(2, 0);
        });
        AssertAddressRangeMemoryBreakPoint(machineBreakpoints, BreakPointType.WRITE, 1, 3, 11, false, () => {
            // Inclusion of breakpoint range
            memory.Memset(0, 0, 5);
            // Start is the same
            memory.Memset(1, 0, 5);
            // End is the same
            memory.Memset(2, 0, 1);
            // Start in range
            memory.Memset(2, 0, 10);
            // End in range
            memory.Memset(0, 0, 3);
            // Not triggered
            memory.Memset(10, 0, 10);
        });
    }
    
    [AssertionMethod]
    private void AssertAddressMemoryBreakPoint(MachineBreakpoints machineBreakpoints, BreakPointType breakPointType, uint address, int expectedTriggers, bool isRemovedOnTrigger, Action action) {
        int count = 0;
        AddressBreakPoint breakPoint = new AddressBreakPoint(breakPointType, address, breakpoint => { count++; }, isRemovedOnTrigger);
        machineBreakpoints.ToggleBreakPoint(breakPoint, true);
        action.Invoke();
        machineBreakpoints.ToggleBreakPoint(breakPoint, false);
        Assert.Equal(expectedTriggers, count);
    }

    [AssertionMethod]
    private void AssertAddressRangeMemoryBreakPoint(MachineBreakpoints machineBreakpoints, BreakPointType breakPointType, uint startAddress, uint endAddress, int expectedTriggers, bool isRemovedOnTrigger, Action action) {
        int count = 0;
        AddressRangeBreakPoint breakPoint = new AddressRangeBreakPoint(breakPointType, startAddress, endAddress, breakpoint => { count++; }, isRemovedOnTrigger);
        machineBreakpoints.ToggleBreakPoint(breakPoint, true);
        action.Invoke();
        machineBreakpoints.ToggleBreakPoint(breakPoint, false);
        Assert.Equal(expectedTriggers, count);
    }

    [Fact]
    public void TestAdd() {
        TestOneBin("add");
    }

    [Fact]
    public void TestBcdcnv() {
        TestOneBin("bcdcnv");
    }

    [Fact]
    public void TestBitwise() {
        byte[] expected = GetExpected("bitwise");
        // dosbox values
        expected[0x9F] = 0x12;
        expected[0x9D] = 0x12;
        expected[0x9B] = 0x12;
        expected[0x99] = 0x12;
        TestOneBin("bitwise", expected);
    }

    [Fact]
    public void TestCmpneg() {
        TestOneBin("cmpneg");
    }

    [Fact]
    public void TestControl() {
        byte[] expected = GetExpected("control");
        // dosbox values
        expected[0x1] = 0x78;
        TestOneBin("control", expected);
    }

    [Fact]
    public void TestDatatrnf() {
        TestOneBin("datatrnf");
    }

    [Fact]
    public void TestDiv() {
        TestOneBin("div");
    }

    [Fact]
    public void TestInterrupt() {
        TestOneBin("interrupt");
    }

    [Fact]
    public void TestJump1() {
        TestOneBin("jump1");
    }

    [Fact]
    public void TestJump2() {
        TestOneBin("jump2");
    }

    [Fact]
    public void TestJmpmov() {
        // 0x4001 in little endian
        byte[] expected = new byte[] { 0x01, 0x40 };
        Machine emulator = TestOneBin("jmpmov", expected);
        State state = emulator.Cpu.State;
        uint endAddress = MemoryUtils.ToPhysicalAddress(state.CS, state.IP);
        // Last instruction HLT is one byte long and is at 0xF400C
        Assert.Equal((uint)0xF400D, endAddress);
    }

    [Fact]
    public void TestMul() {
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
        TestOneBin("mul", expected);
    }

    [Fact]
    public void TestRep() {
        TestOneBin("rep");
    }

    [Fact]
    public void TestRotate() {
        TestOneBin("rotate");
    }

    [Fact]
    public void TestSegpr() {
        TestOneBin("segpr");
    }

    [Fact]
    public void TestShifts() {
        TestOneBin("shifts");
    }

    [Fact]
    public void TestStrings() {
        TestOneBin("strings");
    }

    [Fact]
    public void TestSub() {
        TestOneBin("sub");
    }

    [Theory]
    [InlineData(0b0011110000000000, 0b0010000000000001, 0, 0b0011110000000000, true, true)] // result is same as dest, flags unaffected
    [InlineData(0b0000000000000001, 0b0000000000000000, 1, 0b0000000000000010, false, false)] // shift one bit 
    [InlineData(0b0000000000000001, 0b1000000000000000, 1, 0b0000000000000011, false, false)] // shift in a 1 from the source
    [InlineData(0b0000000000000001, 0b1000000000000000, 2, 0b0000000000000110, false, false)] // shift more than 1 position
    [InlineData(0b0010000000000010, 0b0100000000000000, 3, 0b0000000000010010, true, false)] // last shifted bit is 1
    [InlineData(0b0000100000000000, 0b0000000000000000, 4, 0b1000000000000000, false, true)] // last shifted bit is 0 and sign changed
    [InlineData(0b1000000000000000, 0b0000000000000001, 5, 0b0000000000000000, false, true)] // last shifted bit is 0 and sign changed  
    [InlineData(0b1111110000000000, 0b1000000000000001, 6, 0b0000000000100000, true, true)] // last shifted bit is 1 and sign changed 
    [InlineData(0b0011110000000000, 0b0010000000000001, 16, 0b0010000000000001, false, false)] // complete shift
    [InlineData(0b0011110000000000, 0b0010000000000001, 17, 0b0100000000000010, true, true)] // count > size is undefined
    public void TestShld16(ushort destination, ushort source, byte count, ushort expected, bool cf, bool of) {
        // Arrange
        var state = new State {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu(state);

        // Act
        ushort result = alu.Shld16(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }
    
    [Theory]
    [InlineData(0b00111100000000000000000000000000, 0b00100000000000000000000000000001, 0, 0b00111100000000000000000000000000, true, true)] // result is same as dest, flags unaffected
    [InlineData(0b00000000000000000000000000000001, 0b00000000000000000000000000000000, 1, 0b00000000000000000000000000000010, false, false)] // shift one bit 
    [InlineData(0b00000000000000000000000000000001, 0b10000000000000000000000000000000, 1, 0b00000000000000000000000000000011, false, false)] // shift in a 1 from the source
    [InlineData(0b00000000000000000000000000000001, 0b10000000000000000000000000000000, 2, 0b00000000000000000000000000000110, false, false)] // shift more than 1 position
    [InlineData(0b00100000000000100000000000000000, 0b01000000000000000000000000000000, 3, 0b00000000000100000000000000000010, true, false)] // last shifted bit is 1
    [InlineData(0b00001000000000000000000000000000, 0b00000000000000000000000000000000, 4, 0b10000000000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed
    [InlineData(0b10000000000000000000000000000000, 0b00000000000000000000000000000001, 5, 0b00000000000000000000000000000000, false, true)] // last shifted bit is 0 and sign changed  
    [InlineData(0b11111100000000000000000000000000, 0b10000000000000010000000000000000, 6, 0b00000000000000000000000000100000, true, true)] // last shifted bit is 1 and sign changed 
    [InlineData(0b00110000000000000000110000000000, 0b00100000000000000000000000000001, 32, 0b00110000000000000000110000000000, true, true)] // only lowest 5 bits of count are used (so it's 0)
    public void TestShld32(uint destination, uint source, byte count, uint expected, bool cf, bool of) {
        // Arrange
        var state = new State {
            CarryFlag = true,
            OverflowFlag = true
        };
        var alu = new Alu(state);

        // Act
        uint result = alu.Shld32(destination, source, count);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(cf, state.CarryFlag);
        Assert.Equal(of, state.OverflowFlag);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName) {
        byte[] expected = GetExpected(binName);
        return TestOneBin(binName, expected);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, byte[] expected) {
        Machine machine = Execute(binName);
        Memory memory = machine.Memory;
        CompareMemoryWithExpected(memory, expected, 0, expected.Length - 1);
        return machine;
    }

    private ProgramExecutor CreateProgramExecutor(string binName) {
        return new MachineCreator().CreateProgramExecutorFromBinName(binName);
    }

    private Machine Execute(string binName) {
        using ProgramExecutor programExecutor = CreateProgramExecutor(binName);
        programExecutor.Run();
        //new StringsOverrides(new(), programExecutor.Machine).entry_F000_FFF0_FFFF0(0);
        return programExecutor.Machine;
    }

    private byte[] GetExpected(string binName) {
        string resPath = $"Resources/cpuTests/res/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    [AssertionMethod]
    private void CompareMemoryWithExpected(Memory memory, byte[] expected, int start, int end) {
        byte[] actual = memory.Ram;
        actual[start..end].Should().BeEquivalentTo(expected[start..end]);
    }

    private string HexValueWithFlagsB(byte value) {
        return ConvertUtils.ToHex8(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }

    private string HexValueWithFlagsW(ushort value) {
        return ConvertUtils.ToHex16(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }
}