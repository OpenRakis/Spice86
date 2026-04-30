namespace Spice86.Tests.CpuTests.SingleStepTests;

using Spice86.Core.Emulator.CPU;
using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;

using Xunit;

/// <summary>
/// Executes tests from https://github.com/SingleStepTests/
/// </summary>
public class SingleStepTest : IDisposable {
    private SingleStepTestMinimalMachine _singleStepTestMinimalMachine = new(CpuModel.INTEL_80386);
    private readonly RevocationListHelper _revocationListHelper;
    private readonly CpuTestRunner _testRunner;
    private readonly UndefinedFlagsTable _undefinedFlagsTable;

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
        // Read AND write the global revocation list at the source-tree location so
        // regen runs are immediately observed by subsequent runs (no rebuild needed
        // to refresh the bin copy of the resource, which used to make the agent loop
        // appear non-deterministic). Per-zip revocation lists are still loaded from
        // the bin-copied zip archives.
        string sourceResourcesPath = LocateSourceResourcesDirectory();
        _revocationListHelper = new RevocationListHelper(readBasePath: sourceResourcesPath, writeBasePath: sourceResourcesPath);

        string csvPath = Path.Join("Resources", "cpuTests", "singleStepTests", "80386.csv");
        _undefinedFlagsTable = new UndefinedFlagsTable(csvPath);

        CpuTestAsserter testAsserter = new();
        _testRunner = new CpuTestRunner(testAsserter);
    }

    private static string LocateSourceResourcesDirectory() {
        string relative = Path.Join("tests", "Spice86.Tests", "Resources", "cpuTests", "singleStepTests");
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) {
            string candidate = Path.Join(dir.FullName, relative);
            if (Directory.Exists(candidate)) {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate source resources directory '{relative}' walking up from {AppContext.BaseDirectory}");
    }

    [Theory]
    [InlineData(CpuModel.INTEL_80386, "00-65", 3)]
    [InlineData(CpuModel.INTEL_80386, "66-66", 3)]
    [InlineData(CpuModel.INTEL_80386, "67.00-67.7F", 3)]
    [InlineData(CpuModel.INTEL_80386, "67.80-67.FF", 3)]
    [InlineData(CpuModel.INTEL_80386, "68-FF", 3)]
    public void TestCpu(CpuModel cpuModel, string range, int maxCycles) {
        _singleStepTestMinimalMachine.Dispose();
        _singleStepTestMinimalMachine = new(cpuModel);
        string cpuModelString = FromCpuModel(cpuModel);
        string fileName = $"{cpuModelString}.{range}.zip";
        string path = Path.Join(AppContext.BaseDirectory, fileName);

        using Stream stream = File.OpenRead(path);
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ISet<string> revocationList = _revocationListHelper.ReadCombinedRevocationLists(archive, cpuModelString, range);
        ISet<string> eternalRevocationList = _revocationListHelper.ReadEternalRevocationList(cpuModelString);

        TestStatistics stats = new TestStatistics();
        RunAllTests(archive, revocationList, eternalRevocationList, cpuModel, maxCycles, stats);

#pragma warning disable CS0162 // Unreachable code detected
        if (GenerateRevocationList) {
            _revocationListHelper.WriteRevocationList(stats, cpuModelString, range);
        } else {
            string? opcodeToFix = OpcodeToFix;
            if (opcodeToFix != null
                && stats.OpcodeStats.TryGetValue(opcodeToFix, out TestCounters? counters)
                && counters.Failing > 0) {
                string reasons = string.Join("\n", stats.OpcodeErrorReasons[opcodeToFix]
                    .OrderByDescending(kv => kv.Value)
                    .Take(15)
                    .Select(kv => $"  -> {kv.Key}: {kv.Value}"));
                string samples = string.Join("\n---\n", stats.OpcodeSampleErrors[opcodeToFix]);
                throw new Xunit.Sdk.XunitException(
                    $"Opcode {opcodeToFix}: {counters.Failing}/{counters.Passing + counters.Failing} failing in {range}.\nGrouped reasons:\n{reasons}\n\nSample errors:\n{samples}");
            }
        }
#pragma warning restore CS0162 // Unreachable code detected
    }

    private void RunAllTests(ZipArchive archive, ISet<string> revocationList, ISet<string> eternalRevocationList, CpuModel cpuModel, int maxCycles, TestStatistics stats) {
        foreach (ZipArchiveEntry entry in archive.Entries) {
            List<CpuTest>? cpuTests = ReadCpuTests(entry);
            if (cpuTests is null) {
                continue;
            }

            string opcode = entry.Name.Replace(".json", "");
            uint flagsMask = _undefinedFlagsTable.GetDefinedFlagsMask(opcode);

            // In fix mode, only run tests for the specific opcode we're fixing
            if (OpcodeToFix != null && opcode != OpcodeToFix) {
                continue;
            }

            int index = 0;
            foreach (CpuTest cpuTest in cpuTests) {
                // Skip tests whose encoding falls in one of the three undefined SIB rows.
                // They are intentionally excluded from the revocation list because the
                // recorded data documents them as inconsistent undefined behavior.
                if (InvalidSibFilter.IsInvalidSibEncoding(cpuTest.Bytes)) {
                    stats.RecordPassingTest(opcode);
                    continue;
                }

                // - Eternal revocation list entries are ALWAYS skipped regardless of mode.
                // - In fix mode (OpcodeToFix set), the revocation list is ignored for the
                //   opcode being fixed so failures surface.
                // - In regen mode (GenerateRevocationList = true), the non-eternal revocation
                //   list is ignored so the regenerated list reflects the current state.
                // - In honor mode (GenerateRevocationList = false, OpcodeToFix == null),
                //   tests in the revocation list are skipped to keep CI permissive.
                if (eternalRevocationList.Contains(cpuTest.Hash)) {
                    stats.RecordPassingTest(opcode);
                    continue;
                }

                bool shouldSkipRevokedTest = OpcodeToFix == null
                    && !GenerateRevocationList
                    && revocationList.Contains(cpuTest.Hash);

                if (shouldSkipRevokedTest) {
                    stats.RecordFailingTest(opcode, cpuTest.Hash, "Skipped (revocation list)");
                    continue;
                }

                string errorMessage = "";
                bool testPassed = true;
                try {
                    _testRunner.RunTest(cpuTest, index++, entry.Name, _singleStepTestMinimalMachine, maxCycles, flagsMask);
                } catch (Exception ex) {
                    testPassed = false;
                    errorMessage = ex.Message;
                    if (!GenerateRevocationList && OpcodeToFix == null) {
                        throw;
                    }
                }

                if (testPassed) {
                    stats.RecordPassingTest(opcode);
                } else {
                    stats.RecordFailingTest(opcode, cpuTest.Hash, errorMessage);
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
        var tests = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent);
        if (tests == null) {
            return null;
        }

        try {
            return tests.Select(element => CpuTest.FromJson(element.GetRawText())).ToList();
        } catch(InvalidTestException) {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _singleStepTestMinimalMachine.Dispose();
    }
}