namespace Spice86.Tests;

using FluentAssertions;

using JetBrains.Annotations;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

using System.IO;

using Xunit;

public class MachineTest {
    static MachineTest() {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    public static IEnumerable<object[]> GetCfgCpuConfigurations() {
        yield return new object[] { false };
        yield return new object[] { true };
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestAdd(bool enableCfgCpu) {
        TestOneBin("add", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestBcdcnv(bool enableCfgCpu) {
        TestOneBin("bcdcnv", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestBitwise(bool enableCfgCpu) {
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
    public void TestCmpneg(bool enableCfgCpu) {
        TestOneBin("cmpneg", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestControl(bool enableCfgCpu) {
        byte[] expected = GetExpected("control");
        // dosbox values
        expected[0x1] = 0x78;
        TestOneBin("control", expected, enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestDatatrnf(bool enableCfgCpu) {
        TestOneBin("datatrnf", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestDiv(bool enableCfgCpu) {
        TestOneBin("div", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestInterrupt(bool enableCfgCpu) {
        TestOneBin("interrupt", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJump1(bool enableCfgCpu) {
        TestOneBin("jump1", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJump2(bool enableCfgCpu) {
        TestOneBin("jump2", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestJmpmov(bool enableCfgCpu) {
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
    public void TestMul(bool enableCfgCpu) {
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
    public void TestRep(bool enableCfgCpu) {
        TestOneBin("rep", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestRotate(bool enableCfgCpu) {
        TestOneBin("rotate", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSegpr(bool enableCfgCpu) {
        TestOneBin("segpr", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestShifts(bool enableCfgCpu) {
        TestOneBin("shifts", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestStrings(bool enableCfgCpu) {
        TestOneBin("strings", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSub(bool enableCfgCpu) {
        TestOneBin("sub", enableCfgCpu);
    }

    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSelfModifyValue(bool enableCfgCpu) {
        byte[] expected = new byte[4];
        expected[0x00] = 0x01;
        expected[0x01] = 0x00;
        expected[0x02] = 0xff;
        expected[0x03] = 0xff;
        TestOneBin("selfmodifyvalue", expected, enableCfgCpu);
    }
    
    [Theory]
    [MemberData(nameof(GetCfgCpuConfigurations))]
    public void TestSelfModifyInstructions(bool enableCfgCpu) {
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
    public void TestExternalInt(bool enableCfgCpu) {
        byte[] expected = new byte[6];
        expected[0x00] = 0x01;
        TestOneBin("externalint", expected, enableCfgCpu, 0xFFFFFFF, true);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, bool enableCfgCpu) {
        byte[] expected = GetExpected(binName);
        return TestOneBin(binName, expected, enableCfgCpu);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, byte[] expected, bool enableCfgCpu, long maxCycles=100000L, bool enablePit = false) {
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(binName:binName, enableCfgCpu:enableCfgCpu, maxCycles:maxCycles, enablePit:enablePit, recordData: false).Create();
        spice86DependencyInjection.ProgramExecutor.Run();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        CompareMemoryWithExpected(memory, expected, 0, expected.Length);
        return machine;
    }

    private static byte[] GetExpected(string binName) {
        string resPath = $"Resources/cpuTests/res/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    [AssertionMethod]
    private void CompareMemoryWithExpected(IMemory memory, byte[] expected, int start, int end) {
        byte[] actual = memory.ReadRam();
        actual[start..end].Should().BeEquivalentTo(expected[start..end]);
    }
}