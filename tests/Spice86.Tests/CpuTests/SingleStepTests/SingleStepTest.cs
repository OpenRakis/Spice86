namespace Spice86.Tests.CpuTests.SingleStepTests;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using System.IO.Compression;
using System.Text.Json;

using Xunit;

/// <summary>
/// Example usage of the CpuTest model classes with compressed JSON file support
/// </summary>
public class SingleStepTest {
    [Theory]
    [InlineData("8086")]
    [InlineData("80286")]
    public void TestCpu(string cpuModel) {
        string zipFilePath = $"Resources/SingleStepTests/{cpuModel}.zip";
        using FileStream fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries) {
            List<CpuTest>? cpuTests = ReadCpuTests(entry);
            if (cpuTests is null) {
                continue;
            }

            int index = 0;
            foreach (CpuTest cpuTest in cpuTests) {
                RunCpuTest(cpuTest, index++, entry.Name, cpuModel);
            }
        }
    }

    private List<CpuTest>? ReadCpuTests(ZipArchiveEntry entry) {
        using Stream entryStream = entry.Open();
        using StreamReader reader = new StreamReader(entryStream);
        string jsonContent = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<CpuTest>>(jsonContent);
    }

    private State InitializeRegistersFromTest(CpuState cpuState) {
        Registers registers = cpuState.Registers;
        State state = new State();
        state.AX = ConvertReg(registers.AX);
        state.BX = ConvertReg(registers.BX);
        state.CX = ConvertReg(registers.CX);
        state.DX = ConvertReg(registers.DX);
        state.BP = ConvertReg(registers.BP);
        state.SP = ConvertReg(registers.SP);
        state.SI = ConvertReg(registers.SI);
        state.DI = ConvertReg(registers.DI);
        
        state.CS = ConvertReg(registers.CS);
        state.DS = ConvertReg(registers.DS);
        state.ES = ConvertReg(registers.ES);
        state.SS = ConvertReg(registers.SS);
        state.FS = ConvertReg(registers.FS);
        state.GS = ConvertReg(registers.GS);

        state.IP = ConvertReg(registers.IP);
        state.Flags.FlagRegister = ConvertReg(registers.Flags);

        return state;
    }

    private Memory InitializeMemoryFromTest(CpuState cpuState) {
        uint[][] ram = cpuState.Ram;
        Memory memory = new(new(), new Ram(/*0x10FFEF*/1024 * 1024), new A20Gate(false));
        foreach (uint[] ramLine in ram) {
            uint physicalAddress = ramLine[0];
            uint value = ramLine[1];
            if (value > 0xFF) {
                throw new ArgumentOutOfRangeException("ram", $"Value {value} is not a byte for address {physicalAddress}");
            }
            
            memory.UInt8[physicalAddress] = (byte)value;
        }

        return memory;
    }
    
    private void CompareRegistersWithExpected(CpuState cpuState, State state) {
        Registers registers = cpuState.Registers;
        CompareReg(registers.AX, state.AX);
        CompareReg(registers.BX, state.BX);
        CompareReg(registers.CX, state.CX);
        CompareReg(registers.DX, state.DX);
        CompareReg(registers.BP, state.BP);
        CompareReg(registers.SP, state.SP);
        CompareReg(registers.SI, state.SI);
        CompareReg(registers.DI, state.DI);

        CompareReg(registers.CS, state.CS);
        CompareReg(registers.DS, state.DS);
        CompareReg(registers.ES, state.ES);
        CompareReg(registers.SS, state.SS);
        CompareReg(registers.FS, state.FS);
        CompareReg(registers.GS, state.GS);

        CompareReg(registers.IP, state.IP);
        CompareReg(registers.Flags, state.Flags.FlagRegister16);
    }
    
    private void CompareMemoryWithExpected(CpuState cpuState, Memory memory) {
        uint[][] ram = cpuState.Ram;
        foreach (uint[] ramLine in ram) {
            uint physicalAddress = ramLine[0];
            uint value = ramLine[1];
            if (value > 0xFF) {
                throw new ArgumentOutOfRangeException("ram", $"Value {value} is not a byte for address {physicalAddress}");
            }
            
            Assert.Equal(value, memory.UInt8[physicalAddress]);
        }
    }

    private void CompareReg(uint? expected, ushort actual) {
        if (expected == null) {
            return;
        }

        ushort expectedValue = (ushort)expected;
        Assert.Equal(expectedValue, actual);
    }

    private ushort ConvertReg(uint? registerInJson) {
        if (registerInJson == null) {
            return 0;
        }
        return (ushort)registerInJson;
    }

    private void RunCpuTest(CpuTest cpuTest, int index, string fileName, string cpuModel) {
        string name = cpuTest.Name;
        try {
            Memory memory = InitializeMemoryFromTest(cpuTest.Initial);
            State state = InitializeRegistersFromTest(cpuTest.Initial);
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            PauseHandler pauseHandler = new PauseHandler(loggerService);
            EmulatorBreakpointsManager emulatorBreakpointsManager = new(pauseHandler, state);
            IOPortDispatcher ioPortDispatcher =
                new(emulatorBreakpointsManager.IoReadWriteBreakpoints, state, loggerService, false);
            CallbackHandler callbackHandler = new(state, loggerService);
            DualPic dualPic = new DualPic(state, ioPortDispatcher, false, true, loggerService);
            FunctionCatalogue functionCatalogue = new();
            CfgCpu cfgCpu = new CfgCpu(memory, state, ioPortDispatcher, callbackHandler, dualPic,
                emulatorBreakpointsManager, functionCatalogue, false, loggerService);
            cfgCpu.SignalEntry();
            cfgCpu.ExecuteNext();
            CompareRegistersWithExpected(cpuTest.Final, state);
            CompareMemoryWithExpected(cpuTest.Final, memory);
        } catch (Exception e) {
            throw new Exception($"An error occurred while running test {name} in {fileName} (index {index}) for {cpuModel}", e);
        }
    }
}