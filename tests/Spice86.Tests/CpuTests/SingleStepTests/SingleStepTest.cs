namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU;
using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;

using Xunit;

/// <summary>
/// Executes tests from https://github.com/SingleStepTests/
/// </summary>
public class SingleStepTest {
    private SingleStepTestMinimalMachine _singleStepTestMinimalMachine = new(CpuModel.INTEL_80386);
    private readonly RevocationListHelper _revocationListHelper;
    private readonly CpuTestRunner _testRunner;

    /// <summary>
    /// Set the SPICE86_GENERATE_REVOCATION_LIST environment variable to "1" to generate a
    /// revocation list file instead of failing tests.
    /// The file will contain hashes of failing tests and statistics.
    /// </summary>
    private static readonly bool GenerateRevocationList =
        Environment.GetEnvironmentVariable("SPICE86_GENERATE_REVOCATION_LIST") == "1";

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
        string writeBasePath = Path.Join(Environment.CurrentDirectory, "tests", "Spice86.Tests", "Resources", "cpuTests", "singleStepTests");
        _revocationListHelper = new RevocationListHelper(readBasePath: "Resources/cpuTests/singleStepTests", writeBasePath: writeBasePath);

        CpuTestAsserter testAsserter = new();
        _testRunner = new CpuTestRunner(testAsserter);
    }

    [Theory(Skip = "Not ready yet to be run in CI")]
    [InlineData(CpuModel.INTEL_80386, "00-65", 2)]
    [InlineData(CpuModel.INTEL_80386, "66-66", 2)]
    [InlineData(CpuModel.INTEL_80386, "67.00-67.7F", 2)]
    [InlineData(CpuModel.INTEL_80386, "67.80-67.FF", 2)]
    [InlineData(CpuModel.INTEL_80386, "68-FF", 2)]
    public void TestCpu(CpuModel cpuModel, string range, int maxCycles) {
        _singleStepTestMinimalMachine = new(cpuModel);
        string cpuModelString = FromCpuModel(cpuModel);
        string fileName = $"{cpuModelString}.{range}.zip";
        string path = Path.Join(AppContext.BaseDirectory, fileName);

        using Stream stream = File.OpenRead(path);
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
                // When generating revocation lists, run all tests (including previously revoked ones)
                // to capture current state. In normal/fix mode, skip tests in the revocation list.
                bool shouldSkipRevokedTest = !GenerateRevocationList && OpcodeToFix == null && revocationList.Contains(cpuTest.Hash);

                if (shouldSkipRevokedTest) {
                    stats.RecordFailingTest(opcode, cpuTest.Hash);
                    continue;
                }

                bool testPassed = true;
                try {
                    _testRunner.RunTest(cpuTest, index++, entry.Name, _singleStepTestMinimalMachine, maxCycles);
                } catch (InvalidOperationException) {
                    testPassed = false;
                    if (!GenerateRevocationList) {
                        throw;
                    }
                } catch (Xunit.Sdk.XunitException) {
                    testPassed = false;
                    if (!GenerateRevocationList) {
                        throw;
                    }
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

        // Deserialize directly to list of CpuTest - logic is encapsulated
        List<JsonElement>? tests = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent);
        if (tests == null) {
            return null;
        }

        try {
            return tests.Select(element => CpuTest.FromJson(element.GetRawText())).ToList();
        } catch(InvalidTestException) {
            return null;
        }
    }
}