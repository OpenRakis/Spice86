namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

using Xunit;

/// <summary>
/// Executes tests from https://github.com/SingleStepTests/
/// </summary>
public class SingleStepTest {
    private SingleStepTestMinimalMachine _singleStepTestMinimalMachine = new(CpuModel.INTEL_80386);
    [Theory(Skip = "Not ready yet to be run in CI")]
    [InlineData(CpuModel.INTEL_80386, "00-65", 2)]
    [InlineData(CpuModel.INTEL_80386, "66-66", 2)]
    [InlineData(CpuModel.INTEL_80386, "67.00-67.7F", 2)]
    [InlineData(CpuModel.INTEL_80386, "67.80-67.FF", 2)]
    [InlineData(CpuModel.INTEL_80386, "68-FF", 2)]
    public void TestCpu(CpuModel cpuModel, string range, int maxCycles) {
        _singleStepTestMinimalMachine = new(cpuModel);
        string cpuModelString = FromCpuModel(cpuModel);
        string resource = $"{cpuModelString}.{range}.zip";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream == null) {
            Assert.Fail($"Couldn't find test resource {resource} for {cpuModel} for range {range}");
        }
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ISet<string> revocationList = ReadRevocationList(archive);
        foreach (ZipArchiveEntry entry in archive.Entries) {
            List<CpuTest>? cpuTests = ReadCpuTests(entry);
            if (cpuTests is null) {
                continue;
            }

            int index = 0;
            foreach (CpuTest cpuTest in cpuTests) {
                if (revocationList.Contains(cpuTest.Hash)) {
                    continue;
                }
                RunCpuTest(cpuTest, index++, entry.Name, cpuModel, maxCycles);
            }
        }
    }

    private string FromCpuModel(CpuModel cpuModel) {
        return cpuModel switch {
            CpuModel.INTEL_8086 => "8086",
            CpuModel.INTEL_80286 => "80286",
            CpuModel.INTEL_80386 => "80386",
            _ => throw new InvalidEnumArgumentException()
        };
    }

    private ISet<string> ReadRevocationList(ZipArchive archive) {
        ZipArchiveEntry? revocationEntry = archive.GetEntry("revocation_list.txt");
        if (revocationEntry is null) {
            return new HashSet<string>();
        }
        using Stream entryStream = revocationEntry.Open();
        using StreamReader reader = new StreamReader(entryStream);
        HashSet<string> revocationList = [];
        string? line;
        while ((line = reader.ReadLine()) != null) {
            if (!string.IsNullOrWhiteSpace(line)) {
                revocationList.Add(line.Trim());
            }
        }
        return revocationList;
    }

    private List<CpuTest>? ReadCpuTests(ZipArchiveEntry entry) {
        if (!entry.Name.EndsWith(".json")) {
            return null;
        }
        using Stream entryStream = entry.Open();
        using StreamReader reader = new StreamReader(entryStream);
        string jsonContent = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<CpuTest>>(jsonContent);
    }

    private void InitializeRegistersFromTest(CpuState cpuState, State state) {
        Registers registers = cpuState.Registers;
        state.EAX = ConvertReg(registers.AX);
        state.EBX = ConvertReg(registers.BX);
        state.ECX = ConvertReg(registers.CX);
        state.EDX = ConvertReg(registers.DX);
        state.EBP = ConvertReg(registers.BP);
        state.ESP = ConvertReg(registers.SP);
        state.ESI = ConvertReg(registers.SI);
        state.EDI = ConvertReg(registers.DI);
        
        state.CS = ConvertReg(registers.CS);
        state.DS = ConvertReg(registers.DS);
        state.ES = ConvertReg(registers.ES);
        state.SS = ConvertReg(registers.SS);
        state.FS = ConvertReg(registers.FS);
        state.GS = ConvertReg(registers.GS);

        state.IP = ConvertReg(registers.IP);
        state.Flags.FlagRegister = ConvertReg(registers.Flags);
    }

    private void InitializeMemoryFromTest(CpuState cpuState, Memory memory) {
        uint[][] ram = cpuState.Ram;
        foreach (uint[] ramLine in ram) {
            uint physicalAddress = ramLine[0];
            uint value = ramLine[1];
            if (value > 0xFF) {
                throw new ArgumentOutOfRangeException("ram", $"Value {value} is not a byte for address {physicalAddress}");
            }
            
            memory.UInt8[physicalAddress] = (byte)value;
        }
    }
    
    private void CompareRegistersWithExpected(CpuState cpuState, State state) {
        Registers registers = cpuState.Registers;
        CompareReg(nameof(state.EAX), registers.AX, state.EAX);
        CompareReg(nameof(state.EBX), registers.BX, state.EBX);
        CompareReg(nameof(state.ECX), registers.CX, state.ECX);
        CompareReg(nameof(state.EDX), registers.DX, state.EDX);
        CompareReg(nameof(state.EBP), registers.BP, state.EBP);
        CompareReg(nameof(state.ESP), registers.SP, state.ESP);
        CompareReg(nameof(state.ESI), registers.SI, state.ESI);
        CompareReg(nameof(state.EDI), registers.DI, state.EDI);

        CompareReg(nameof(state.CS), registers.CS, state.CS);
        CompareReg(nameof(state.DS), registers.DS, state.DS);
        CompareReg(nameof(state.ES), registers.ES, state.ES);
        CompareReg(nameof(state.SS), registers.SS, state.SS);
        CompareReg(nameof(state.FS), registers.FS, state.FS);
        CompareReg(nameof(state.GS), registers.GS, state.GS);

        CompareReg(nameof(state.IP), registers.IP, state.IP);
        CompareReg(nameof(state.Flags), registers.Flags, state.Flags.FlagRegister16, true);
    }
    
    private void CompareMemoryWithExpected(CpuState cpuState, Memory memory) {
        uint[][] ram = cpuState.Ram;
        foreach (uint[] ramLine in ram) {
            uint physicalAddress = ramLine[0];
            uint expected = ramLine[1];
            if (expected > 0xFF) {
                throw new ArgumentOutOfRangeException("ram", $"Value {expected} is not a byte for address {physicalAddress}");
            }

            byte actual = memory.UInt8[physicalAddress];
            if (expected == actual) {
                continue;
            }
            string expectedHex = ConvertUtils.ToHex8((byte)expected);
            string actualHex = ConvertUtils.ToHex8(actual);
            string address = ConvertUtils.ToHex32(physicalAddress);
            Assert.Fail($"Byte at address {address} differs. Expected {expectedHex} Actual {actualHex}");
        }
    }

    private void CompareReg(string register, uint? expected, uint actual, bool isFlags=false) {
        if (expected == null) {
            return;
        }

        if (expected == actual) {
            return;
        }

        string expectedStr;
        string actualStr;
        string additionalInfo = "";
        if (isFlags) {
            expectedStr = ConvertUtils.ToBin32(expected.Value);
            actualStr = ConvertUtils.ToBin32(actual);
            IList<string?> flagsDiffering = [
                CompareFlag(nameof(Flags.Carry), Flags.Carry, expected.Value, actual),
                CompareFlag(nameof(Flags.Parity), Flags.Parity, expected.Value, actual),
                CompareFlag(nameof(Flags.Auxiliary), Flags.Auxiliary, expected.Value, actual),
                CompareFlag(nameof(Flags.Zero), Flags.Zero, expected.Value, actual),
                CompareFlag(nameof(Flags.Sign), Flags.Sign, expected.Value, actual),
                CompareFlag(nameof(Flags.Trap), Flags.Trap, expected.Value, actual),
                CompareFlag(nameof(Flags.Interrupt), Flags.Interrupt, expected.Value, actual),
                CompareFlag(nameof(Flags.Direction), Flags.Direction, expected.Value, actual),
                CompareFlag(nameof(Flags.Overflow), Flags.Overflow, expected.Value, actual),
            ];
            additionalInfo = ". " + string.Join(",", flagsDiffering.Where(x => x is not null));
        } else {
            expectedStr = ConvertUtils.ToHex32(expected.Value);
            actualStr = ConvertUtils.ToHex32(actual);
        }
        Assert.Fail($"Expected and actual are not the same for register {register}. Expected: {expectedStr} Actual: {actualStr}{additionalInfo}");
    }

    private string? CompareFlag(string flagname, uint mask, uint expected, uint actual) {
        if((expected & mask) == (actual & mask)) {
            return null;
        }

        return $"{flagname} differs";
    }

    private ushort ConvertReg(uint? registerInJson) {
        if (registerInJson == null) {
            return 0;
        }
        return (ushort)registerInJson;
    }

    private void RunCpuTest(CpuTest cpuTest, int index, string fileName, CpuModel cpuModel, int maxCycles) {
        string name = cpuTest.Name;
        try {
            InitializeMemoryFromTest(cpuTest.Initial, _singleStepTestMinimalMachine.Memory);
            InitializeRegistersFromTest(cpuTest.Initial, _singleStepTestMinimalMachine.State);
            CfgCpu cfgCpu = _singleStepTestMinimalMachine.Cpu;
            cfgCpu.SignalEntry();
            for(int i=0;i<maxCycles;i++) {
                // some tests have 2 instructions
                cfgCpu.ExecuteNext();
            }

            CompareRegistersWithExpected(cpuTest.Final, _singleStepTestMinimalMachine.State);
            CompareMemoryWithExpected(cpuTest.Final, _singleStepTestMinimalMachine.Memory);
            _singleStepTestMinimalMachine.RestoreMemoryAfterTest();
        } catch (Exception e) {
            string instructionBytes = FormatBytesAsHex(cpuTest.Bytes);
            throw new Exception($"An error occurred while running test {name} in {fileName} (index {index}) for {cpuModel} (Instruction bytes are {instructionBytes}): {e.Message}", e);
        }
    }
    
    private static string FormatBytesAsHex(uint[] byteValues) {
        byte[] bytes = byteValues.Select(i => (byte)i).ToArray();
        return Convert.ToHexString(bytes);
    }
}