namespace Spice86.Tests;

using FluentAssertions;

using JetBrains.Annotations;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

using Xunit;

public class MachineTest
{
    private readonly ListingExtractor _dumper = new(new(AsmRenderingConfig.CreateSpice86Style()));

    static MachineTest()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    [Fact]
    public void TestAdd()
    {
        TestOneBin("add");
    }

    [Fact]
    public void TestBcdcnv()
    {
        TestOneBin("bcdcnv");
    }

    [Fact]
    public void TestBitwise()
    {
        byte[] expected = GetExpected("bitwise");
        // dosbox values
        expected[0x9F] = 0x12;
        expected[0x9D] = 0x12;
        expected[0x9B] = 0x12;
        expected[0x99] = 0x12;
        TestOneBin("bitwise", expected);
    }

    [Fact]
    public void TestCmpneg()
    {
        TestOneBin("cmpneg");
    }

    [Fact]
    public void TestControl()
    {
        byte[] expected = GetExpected("control");
        // dosbox values
        expected[0x1] = 0x78;
        TestOneBin("control", expected);
    }

    [Fact]
    public void TestDatatrnf()
    {
        TestOneBin("datatrnf");
    }

    [Fact]
    public void TestDiv()
    {
        TestOneBin("div");
    }

    [Fact]
    public void TestInterrupt()
    {
        TestOneBin("interrupt");
    }

    [Fact]
    public void TestJump1()
    {
        TestOneBin("jump1");
    }

    [Fact]
    public void TestJump2()
    {
        TestOneBin("jump2");
    }

    [Fact]
    public void TestJmpmov()
    {
        // 0x4001 in little endian
        byte[] expected = new byte[] { 0x01, 0x40 };
        Machine emulator = TestOneBin("jmpmov", expected);
        State state = emulator.CpuState;
        uint endAddress = MemoryUtils.ToPhysicalAddress(state.CS, state.IP);
        // Last instruction HLT is one byte long and is at 0xF400C
        Assert.Equal((uint)0xF400D, endAddress);
    }

    [Fact]
    public void TestMul()
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
        expected[0xAA] = 0x42;
        expected[0xAE] = 0x2;
        expected[0xB0] = 0x3;
        expected[0xB2] = 0x2;
        expected[0xB4] = 0x3;
        expected[0xB6] = 0x42;
        expected[0xBA] = 0x2;
        TestOneBin("mul", expected);
    }

    [Fact]
    public void TestRep()
    {
        TestOneBin("rep");
    }

    [Fact]
    public void TestRotate()
    {
        TestOneBin("rotate");
    }

    [Fact]
    public void TestSegpr()
    {
        Machine machine = TestOneBin("segpr");
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
        // Check that the int handler is linked to the division by 0 as a cpu fault type successor
        Assert.Contains(divBy0HandlerEntry, divBy0.Successors);
        Assert.Contains(divBy0HandlerEntry, divBy0.SuccessorsPerType[InstructionSuccessorType.CpuFault]);
        // Check that the instruction next to the div by 0 to which the handler returned to  is linked to the division by 0 as a regular "Call to return" link.
        // Side-note, normally, div by 0 int handler should return to the div instruction. However, here the handler edits the call stack making it return to the next instruction which is how a regular function call in a high level language would behave
        Assert.Contains(divBy0NextInstruction, divBy0.Successors);
        Assert.Contains(divBy0NextInstruction, divBy0.SuccessorsPerType[InstructionSuccessorType.CallToReturn]);
        // Check that IRET is normally connected to the return target
        Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.Successors);
        Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.SuccessorsPerType[InstructionSuccessorType.Normal]);
    }

    [Fact]
    public void TestShifts()
    {
        byte[] expected = GetExpected("shifts");
        expected[0x6F] = 0x08;
        expected[0x79] = 0x08;
        TestOneBin("shifts", expected);
    }

    [Fact]
    public void TestStrings()
    {
        TestOneBin("strings");
    }

    [Fact]
    public void TestSub()
    {
        TestOneBin("sub");
    }

    [Fact]
    public void TestSelfModifyValue()
    {
        byte[] expected = new byte[4];
        expected[0x00] = 0x01;
        expected[0x01] = 0x00;
        expected[0x02] = 0xff;
        expected[0x03] = 0xff;
        Machine machine = TestOneBin("selfmodifyvalue", expected);
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

    [Fact]
    public void TestSelfModifyInstructions()
    {
        byte[] expected = new byte[6];
        expected[0x00] = 0x03;
        expected[0x01] = 0x00;
        expected[0x02] = 0x02;
        expected[0x03] = 0x00;
        expected[0x04] = 0x01;
        expected[0x05] = 0x00;
        TestOneBin("selfmodifyinstructions", expected);
    }

    [Fact]
    public void TestExternalInt()
    {
        byte[] expected = new byte[6];
        expected[0x00] = 0x01;
        TestOneBin("externalint", expected, 0xFFFFFFF, true);
    }

    [Fact]
    public void TestLinearAddressSameButSegmentedDifferent()
    {
        byte[] expected = new byte[2];
        expected[0x00] = 0x02;
        expected[0x01] = 0x00;
        TestOneBin("linearsamesegmenteddifferent", expected, enableA20Gate:true);
    }

    [Fact]
    public void TestCallbacks() {
        string comFileName = Path.GetFullPath("Resources/cpuTests/intchain.com");
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(binName: comFileName, maxCycles: 1000, enablePit: false, installInterruptVectors: true, enableA20Gate: false).Create();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        SegmentedAddress entryPoint = machine.CpuState.IpSegmentedAddress;
        spice86DependencyInjection.ProgramExecutor.Run();

        InterruptVectorTable ivt = new(memory);
        CurrentInstructions currentInstructions =
            machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;

        // Entry INT 8 in the COM
        CfgInstruction? int8Entry = currentInstructions.GetAtAddress(entryPoint);
        Assert.NotNull(int8Entry);

        // Post-INT8 instruction in COM: int 8 is 2 bytes long
        SegmentedAddress postInt8Addr = entryPoint + 2;
        CfgInstruction? postInt8 = currentInstructions.GetAtAddress(postInt8Addr);
        Assert.NotNull(postInt8);

        // INT 8 handler entry: callback at IVT[8]
        CfgInstruction? int8HandlerEntry = currentInstructions.GetAtAddress(ivt[8]);
        Assert.NotNull(int8HandlerEntry);

        // INT 1C handler entry: IRET-only at IVT[1C]
        CfgInstruction? int1CHandlerEntry = currentInstructions.GetAtAddress(ivt[0x1C]);
        Assert.NotNull(int1CHandlerEntry);

        // A) Entry INT8 has exactly two successors: handler entry and post-INT8 (call-to-return link)
        int8Entry.Successors.Should().BeEquivalentTo([int8HandlerEntry, postInt8]);

        // Inside INT8 handler exact layout:
        // [0] callback at IVT[8] (4 bytes)
        // [3] INT 1C (2 bytes)
        // [5] EOI callback (4 bytes)
        // [8] IRET (1 byte)

        SegmentedAddress addrInt1C = int8HandlerEntry.Address + 4;
        SegmentedAddress addrEoiCallback = addrInt1C + 2;
        SegmentedAddress addrIret8 = addrEoiCallback + 4;

        CfgInstruction? intNode1C = currentInstructions.GetAtAddress(addrInt1C);
        CfgInstruction? eoiCallback = currentInstructions.GetAtAddress(addrEoiCallback);
        CfgInstruction? iret8 = currentInstructions.GetAtAddress(addrIret8);

        Assert.NotNull(intNode1C);
        Assert.NotNull(eoiCallback);
        Assert.NotNull(iret8);

        // B) Callback (tick++) must fall through to INT 1C node only
        int8HandlerEntry.Successors.Should().BeEquivalentTo([intNode1C]);

        // C) INT 1C node must have exactly two successors:
        //    - INT 1C handler entry (invoke)
        //    - fallthrough to EOI callback (return target after INT)
        intNode1C.Successors.Should().BeEquivalentTo([int1CHandlerEntry, eoiCallback]);

        // D) INT 1C handler (IRET-only) must return to EOI callback only
        int1CHandlerEntry.Successors.Should().BeEquivalentTo([eoiCallback]);

        // E) EOI callback must fall through to IRET of INT8 handler only
        eoiCallback.Successors.Should().BeEquivalentTo([iret8]);

        // F) INT8 IRET must return to post-INT8 instruction only
        iret8.Successors.Should().BeEquivalentTo([postInt8]);

        // G) No direct edge from entry INT8 to INT1C handler
        int8Entry.Successors.Should().NotContain(int1CHandlerEntry);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName)
    {
        byte[] expected = GetExpected(binName);
        return TestOneBin(binName, expected);
    }

    [AssertionMethod]
    private Machine TestOneBin(string binName, byte[] expected, long maxCycles = 100000L, bool enablePit = false, bool enableA20Gate = false)
    {
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(binName: binName, maxCycles: maxCycles, enablePit: enablePit, enableA20Gate: enableA20Gate).Create();
        spice86DependencyInjection.ProgramExecutor.Run();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        CompareMemoryWithExpected(memory, expected);
        CompareListingWithExpected(binName, machine);
        return machine;
    }

    private void CompareListingWithExpected(string binName, Machine machine) {
        List<string> expectedLines = GetExpectedListing(binName);
        List<string> actualLines = _dumper.ToAssemblyListing(machine.CfgCpu);
        //WriteExpectedListing(binName, actualLines);
        Assert.Equal(expectedLines, actualLines);
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
    [Fact]
    public void Test386ButNotProtectedMode() {
        //Arrange
        string binName = "test386";
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: binName,
            enablePit: false, maxCycles: long.MaxValue,
            failOnUnhandledPort: true).Create();
        Machine machine = spice86DependencyInjection.Machine;
        IMemory memory = machine.Memory;
        Test386ButNotProtectedModeHandler debugPortsHandler = new Test386ButNotProtectedModeHandler(machine.CpuState, new LoggerService(), machine.IoPortDispatcher);

        //Act
        try {
            spice86DependencyInjection.ProgramExecutor.Run();
        } finally {
            Log.Information("Reached POST values {portValues}. Ascii Error is {asciiError}", debugPortsHandler.PostValues, debugPortsHandler.AsciiError);
        }

        //Assert
        Assert.Equal(8, debugPortsHandler.PostValues.Count);
        // FF means test finished normally
        Assert.Equal(0xFF, debugPortsHandler.PostValues.Last());
        CompareListingWithExpected(binName, machine);
    }

    private class Test386ButNotProtectedModeHandler : DefaultIOPortHandler {
        private const int PostPort = 0x999;
        private const int AsciiOutPort = 0x998;

        public List<ushort> PostValues { get; } = new();
        public string AsciiError { get; private set; } = "";

        public Test386ButNotProtectedModeHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(PostPort, this);
            ioPortDispatcher.AddIOPortHandler(AsciiOutPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == AsciiOutPort) {
                AsciiError += Encoding.ASCII.GetString(new byte[] { value });
            } else if (port == PostPort) {
                if (PostValues.Contains(value)) {
                    throw new UnhandledOperationException(_state, $"POST value {value} already sent. Is test looping?");
                }

                PostValues.Add(value);
            }
        }
    }

    private static byte[] GetExpected(string binName)
    {
        string resPath = $"Resources/cpuTests/res/MemoryDumps/{binName}.bin";
        return File.ReadAllBytes(resPath);
    }

    private static List<string> GetExpectedListing(string binName)
    {
        string resPath = $"Resources/cpuTests/res/DumpedListing/{binName}.txt";
        return File.ReadAllLines(resPath).ToList();
    }

    private static void WriteExpectedListing(string binName, List<string> expected) {
        string resPath = $"Resources/cpuTests/res/DumpedListing/{binName}.txt";
        File.WriteAllLines(resPath, expected);
    }

    [AssertionMethod]
    private static void CompareMemoryWithExpected(IMemory memory, byte[] expected)
    {
        byte[] actual = memory.ReadRam((uint)expected.Length);
        Assert.Equal(expected, actual);
    }
}




