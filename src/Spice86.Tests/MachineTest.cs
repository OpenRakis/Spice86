namespace Spice86.Tests;

using Emulator;
using Emulator.CPU;
using Emulator.VM;
using Emulator.Memory;

using Serilog;

using System;
using System.IO;

using Utils;

using Xunit;

public class MachineTest {

    static MachineTest() {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
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
        State state = emulator.GetCpu().GetState();
        uint endAddress = MemoryUtils.ToPhysicalAddress(state.GetCS(), state.GetIP());
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
    private Machine TestOneBin(string binName) {
        byte[] expected = GetExpected(binName);
        return this.TestOneBin(binName, expected);
    }

    private Machine TestOneBin(string binName, byte[] expected) {
        Machine machine = Execute(binName);
        Memory memory = machine.GetMemory();
        CompareMemoryWithExpected(memory, expected, 0, expected.Length - 1);
        return machine;
    }

    private Machine Execute(string binName) {
        Configuration configuration = new Configuration();
        // making sure int8 is not going to be triggered during the tests
        configuration.InstructionsPerSecond = 10000000;
        configuration.Exe = GetBinPath(binName);
        // Don't expect any hash for the exe
        configuration.ExpectedChecksumValue = Array.Empty<byte>();
        configuration.InstallInterruptVector = false;

        using ProgramExecutor programExecutor = new ProgramExecutor(null, configuration);
        Machine machine = programExecutor.Machine;
        Cpu cpu = machine.GetCpu();
        // Disabling custom IO handling
        cpu.SetIoPortDispatcher(null);
        cpu.SetErrorOnUninitializedInterruptHandler(false);
        State state = cpu.GetState();
        state.GetFlags().SetDosboxCompatibility(false);
        programExecutor.Run();
        return machine;
    }

    private byte[] GetExpected(string binName) {
        string resPath = $"Resources/cpuTests/res/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    private string GetBinPath(string binName) {
        return $"Resources/cpuTests/{binName}.bin";
    }

    private void CompareMemoryWithExpected(Memory memory, byte[] expected, int start, int end) {
        byte[] actual = memory.Ram;
        for (uint i = 0; i < end; i++) {
            byte actualByte = actual[i];
            byte expectedByte = expected[i];
            if (actualByte != expectedByte) {
                uint wordIndex = i;
                if (wordIndex % 2 == 1) {
                    wordIndex--;
                }
                ushort actualWord = MemoryUtils.GetUint16(actual, wordIndex);
                ushort expectedWord = MemoryUtils.GetUint16(expected, wordIndex);
                Assert.True(false, "Byte value differs at " + CreateMessageByteDiffer(i, expectedByte, actualByte) + ". If words, "
                    + CreateMessageWordDiffer(wordIndex, expectedWord, actualWord));
            }
        }
    }

    private string CreateMessageByteDiffer(uint address, byte expected, byte actual) {
        return "address " + ConvertUtils.ToHex(address) + " Expected " + HexValueWithFlagsB(expected) + " but got "
            + HexValueWithFlagsB(actual);
    }

    private string HexValueWithFlagsB(byte value) {
        return ConvertUtils.ToHex8(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }

    private string CreateMessageWordDiffer(uint address, ushort expected, ushort actual) {
        return "address " + ConvertUtils.ToHex(address) + " Expected " + HexValueWithFlagsW(expected) + " but got "
            + HexValueWithFlagsW(actual);
    }

    private string HexValueWithFlagsW(ushort value) {
        return ConvertUtils.ToHex16(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }
}