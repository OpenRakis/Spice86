namespace Spice86.Tests;

using Emulator.Cpu;
using Emulator.Machine;
using Emulator.Memory;

using System;
using System.IO;

using Utils;

using Xunit;

public class MachineTest {

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
        byte[] expected = GetExpected("bitwise" + ".bin");
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
        int endAddress = MemoryUtils.ToPhysicalAddress(state.GetCS(), state.GetIP());
        // Last instruction HLT is one byte long and is at 0xF400C
        Assert.Equal(0xF400D, endAddress);
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
    private Machine TestOneBin(String binName) {
        byte[] expected = GetExpected(binName);
        return this.TestOneBin(binName, expected);
    }

    private Machine TestOneBin(String binName, byte[] expected) {
        Machine machine = Execute(binName);
        Memory memory = machine.GetMemory();
        CompareMemoryWithExpected(memory, expected, 0, expected.Length - 1);
        return machine;
    }

    private Machine Execute(String binName) {
        Configuration configuration = new Configuration();
        // making sure int8 is not going to be triggered during the tests
        configuration.SetInstructionsPerSecond(10000000);
        configuration.SetExe(GetBinPath(binName));
        /*using ProgramExecutor programExecutor = new ProgramExecutor(null, configuration);
        Machine machine = programExecutor.getMachine();
        Cpu cpu = machine.GetCpu();
        // Disabling custom IO handling
        cpu.SetIoPortDispatcher(null);
        cpu.SetErrorOnUninitializedInterruptHandler(false);
        State state = cpu.GetState();
        state.GetFlags().SetDosboxCompatibility(false);
        programExecutor.run();
        return machine;*/
        return null;
    }

    private byte[] GetExpected(String binName) {
        String resPath = $"Resources/cpuTests/res/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    private String GetBinPath(String binName) {
        return $"Resources/cpuTests/{binName}.bin";
    }

    private void CompareMemoryWithExpected(Memory memory, byte[] expected, int start, int end) {
        byte[] actual = memory.GetRam();
        for (int i = 0; i < end; i++) {
            byte actualByte = actual[i];
            byte expectedByte = expected[i];
            if (actualByte != expectedByte) {
                int wordIndex = i;
                if (wordIndex % 2 == 1) {
                    wordIndex--;
                }
                int actualWord = MemoryUtils.GetUint16(actual, wordIndex);
                int expectedWord = MemoryUtils.GetUint16(expected, wordIndex);
                Assert.True(false, "Byte value differs at " + CreateMessageByteDiffer(i, expectedByte, actualByte) + ". If words, "
                    + CreateMessageWordDiffer(wordIndex, expectedWord, actualWord));
            }
        }
    }

    private String CreateMessageByteDiffer(int address, int expected, int actual) {
        return "address " + ConvertUtils.ToHex(address) + " Expected " + HexValueWithFlagsB(expected) + " but got "
            + HexValueWithFlagsB(actual);
    }

    private String HexValueWithFlagsB(int value) {
        return ConvertUtils.ToHex8(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }

    private String CreateMessageWordDiffer(int address, int expected, int actual) {
        return "address " + ConvertUtils.ToHex(address) + " Expected " + HexValueWithFlagsW(expected) + " but got "
            + HexValueWithFlagsW(actual);
    }

    private String HexValueWithFlagsW(int value) {
        return ConvertUtils.ToHex16(value) + " (if flags=" + Flags.DumpFlags(value) + ")";
    }
}