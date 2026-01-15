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
    private readonly RevocationListHelper _revocationListHelper;

    /// <summary>
    /// Set to true to generate a revocation list file instead of failing tests.
    /// The file will contain hashes of failing tests and statistics.
    /// </summary>
    private const bool GenerateRevocationList = false;

    /// <summary>
    /// Fix mode: When set to a specific opcode (e.g., "00", "66", "67.00"), the revocation list
    /// will be respected for all opcodes EXCEPT the one specified here. This allows fixing
    /// a specific opcode without running all other tests. Set to null to respect the full
    /// revocation list or when GenerateRevocationList is true.
    /// </summary>
    private const string? OpcodeToFix = null;

    public SingleStepTest() {
        // Read from Resources/cpuTests/singleStepTests
        // Write to tests/Spice86.Tests/Resources/cpuTests/singleStepTests/
        string writeBasePath = Path.Combine(Environment.CurrentDirectory, "tests", "Spice86.Tests", "Resources", "cpuTests", "singleStepTests");
        _revocationListHelper = new RevocationListHelper(readBasePath: "Resources/cpuTests/singleStepTests", writeBasePath: writeBasePath);
    }

    [Theory]
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
        ISet<string> revocationList = _revocationListHelper.ReadCombinedRevocationLists(archive, cpuModelString, range);

        TestStatistics stats = new TestStatistics();
        RunAllTests(archive, revocationList, cpuModel, maxCycles, stats);

        if (GenerateRevocationList) {
            _revocationListHelper.WriteRevocationList(stats, cpuModelString, range);
        }
    }

    private void RunAllTests(ZipArchive archive, ISet<string> revocationList, CpuModel cpuModel, int maxCycles, TestStatistics stats) {
        foreach (ZipArchiveEntry entry in archive.Entries) {
            List<CpuTest>? cpuTests = ReadCpuTests(entry);
            if (cpuTests is null) {
                continue;
            }

            string opcode = entry.Name.Replace(".json", "");

            // In fix mode, only run tests for the specific opcode we're fixing
            if (OpcodeToFix != null && opcode != OpcodeToFix) {
                continue;
            }

            int index = 0;
            foreach (CpuTest cpuTest in cpuTests) {
                // In fix mode, ignore the revocation list for the opcode being fixed
                // In normal mode, skip tests in the revocation list
                bool shouldSkipRevokedTest = OpcodeToFix == null && revocationList.Contains(cpuTest.Hash);

                if (shouldSkipRevokedTest) {
                    stats.RecordFailingTest(opcode, cpuTest.Hash);
                    continue;
                }

                bool testPassed = true;
                try {
                    RunCpuTest(cpuTest, index++, entry.Name, cpuModel, maxCycles);
                } catch (Exception ex) {
                    testPassed = false;
                    if (!GenerateRevocationList) {
                        throw;
                    }
                    // Exception is intentionally swallowed when generating revocation list
                    _ = ex;
                }

                if (testPassed) {
                    stats.RecordPassingTest(opcode);
                } else {
                    stats.RecordFailingTest(opcode, cpuTest.Hash);
                }
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
        state.EAX = ConvertReg(registers.EAX);
        state.EBX = ConvertReg(registers.EBX);
        state.ECX = ConvertReg(registers.ECX);
        state.EDX = ConvertReg(registers.EDX);
        state.EBP = ConvertReg(registers.EBP);
        state.ESP = ConvertReg(registers.ESP);
        state.ESI = ConvertReg(registers.ESI);
        state.EDI = ConvertReg(registers.EDI);

        state.CS = ConvertSReg(registers.CS);
        state.DS = ConvertSReg(registers.DS);
        state.ES = ConvertSReg(registers.ES);
        state.SS = ConvertSReg(registers.SS);
        state.FS = ConvertSReg(registers.FS);
        state.GS = ConvertSReg(registers.GS);

        state.IP = ConvertSReg(registers.EIP);
        state.Flags.FlagRegister = ConvertReg(registers.EFlags);
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
        CompareReg(nameof(state.EAX), registers.EAX, state.EAX);
        CompareReg(nameof(state.EBX), registers.EBX, state.EBX);
        CompareReg(nameof(state.ECX), registers.ECX, state.ECX);
        CompareReg(nameof(state.EDX), registers.EDX, state.EDX);
        CompareReg(nameof(state.EBP), registers.EBP, state.EBP);
        CompareReg(nameof(state.ESP), registers.ESP, state.ESP);
        CompareReg(nameof(state.ESI), registers.ESI, state.ESI);
        CompareReg(nameof(state.EDI), registers.EDI, state.EDI);

        CompareReg(nameof(state.CS), registers.CS, state.CS);
        CompareReg(nameof(state.DS), registers.DS, state.DS);
        CompareReg(nameof(state.ES), registers.ES, state.ES);
        CompareReg(nameof(state.SS), registers.SS, state.SS);
        CompareReg(nameof(state.FS), registers.FS, state.FS);
        CompareReg(nameof(state.GS), registers.GS, state.GS);

        CompareReg(nameof(state.IP), registers.EIP, state.IP);
        CompareReg(nameof(state.Flags), registers.EFlags, state.Flags.FlagRegister, true);
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

    private uint ConvertReg(uint? registerInJson) {
        if (registerInJson == null) {
            return 0;
        }
        return registerInJson.Value;
    }

    private ushort ConvertSReg(uint? registerInJson) {
        if (registerInJson == null) {
            return 0;
        }

        if (registerInJson > ushort.MaxValue) {
            throw new ArgumentOutOfRangeException("registerInJson", $"Value {registerInJson} is not a 16bit value");
        }

        return (ushort)registerInJson;
    }

    private void RunCpuTest(CpuTest cpuTest, int index, string fileName, CpuModel cpuModel, int maxCycles) {
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
        } catch (Exception e) {
            string instructionBytes = FormatBytesAsHex(cpuTest.Bytes);

            // Create State objects only when there's an error for debugging
            State initialStateSnapshot = new State(cpuModel);
            InitializeRegistersFromTest(cpuTest.Initial, initialStateSnapshot);
            string initialState = initialStateSnapshot.ToString();
            string finalState = _singleStepTestMinimalMachine.State.ToString();
            string debugInfo = $"\n\nInitial State:\n{initialState}\n\nFinal State:\n{finalState}";

            throw new Exception($"An error occurred while running test \"{cpuTest.Name}\" ({cpuTest.Hash}) in {fileName} (index {index}) for {cpuModel} (Instruction bytes are {instructionBytes}): {e.Message}{debugInfo}", e);
        } finally {
            _singleStepTestMinimalMachine.RestoreMemoryAfterTest();
            _singleStepTestMinimalMachine.Cpu.Clear();
        }
    }
    
    private static string FormatBytesAsHex(uint[] byteValues) {
        byte[] bytes = byteValues.Select(i => (byte)i).ToArray();
        return Convert.ToHexString(bytes);
    }
}