namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using Spice86.Tests.AsmFixtures;
using Spice86.Tests.CfgCodeGeneration.Ir;

using System.Runtime.CompilerServices;

using Xunit;

internal sealed class GeneratedCodeMachineTestRunner {
    /// <summary>
    /// Generates C# for one fixture, compiles it, re-runs the fixture as an override run and compares the
    /// generated source with its golden.
    /// </summary>
    /// <param name="fixture">Fixture used for discovery and for the override run.</param>
    /// <returns>The generated program.</returns>
    public GeneratedCSharpProgram TestGeneratedCode(AsmFixture fixture) {
        return TestGeneratedCode(fixture, [fixture]);
    }

    /// <summary>
    /// Generates C# from one discovery fixture, compiles it, then runs every execution fixture with the
    /// compiled override installed and checks that execution's oracle. The generated source is compared
    /// with its golden at the end.
    /// </summary>
    /// <param name="discovery">Fixture whose run is recorded for code generation.</param>
    /// <param name="executions">Fixtures run with the compiled override, in order, one fresh emulator each.</param>
    /// <returns>The generated program.</returns>
    /// <exception cref="ArgumentException">Discovery and execution do not share the same golden key.</exception>
    public GeneratedCSharpProgram TestGeneratedCode(AsmFixture discovery, IReadOnlyList<AsmFixture> executions) {
        using AsmRunScope discoveryScope = new(discovery);
        AsmRunSettings discoverySettings = discoveryScope.Fixture.Settings;
        List<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates = new();
        foreach (AsmFixture execution in executions.Where(execution =>
                !IsEquivalentScenarioVariant(discoverySettings, execution.Settings))) {
            using AsmRunScope scope = new(execution);
            using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);
            interpretedStates.Add((execution.Settings, AsmFinalState.Capture(run, execution.Settings)));
        }
        using OwnedRunScopes executionScopes = new();
        foreach (AsmFixture execution in executions) {
            executionScopes.Add(new AsmRunScope(execution));
        }
        return TestGeneratedCodeCore(discoveryScope,
            executionScopes.Scopes.Select<AsmRunScope, (Func<AsmRunScope> Prepare, string? GateSkipReason)>(
                scope => (() => scope, null)).ToList(), interpretedStates);
    }

    /// <summary>
    /// Runs one scenario through the whole generated-code pipeline: a fresh preparation for discovery,
    /// whose oracle is checked first and whose finished machine is then compared with the listing and
    /// CFG goldens, generation, compilation with the metrics gate, a second fresh preparation for the
    /// override run whose oracle is the same one, and the generated-source golden last.
    /// </summary>
    /// <param name="scenario">Scenario to discover, generate and re-run.</param>
    /// <returns>The generated program.</returns>
    public GeneratedCSharpProgram TestGeneratedCode(AsmScenario scenario) {
        return TestGeneratedCode(scenario, [scenario]);
    }

    /// <summary>
    /// Discovers one scenario, generates and compiles its source, then runs every execution scenario
    /// with the compiled override installed, each from a fresh preparation whose oracle is checked.
    /// Every scope is disposed whether the pipeline succeeds or fails. Before generation, every
    /// execution that is not the discovery's own variant also gets its interpreted run checked against
    /// the oracle, the engine assertions and the machine goldens, and when
    /// <see cref="TestJitSettings.CompiledRun"/> is set every execution additionally runs through the
    /// JIT compiler the same way.
    /// </summary>
    /// <param name="discovery">Scenario whose run is recorded for code generation.</param>
    /// <param name="executions">Scenarios executed with the compiled override, in order.</param>
    /// <returns>The generated program.</returns>
    /// <exception cref="ArgumentException">Discovery and executions do not share the same golden key.</exception>
    public GeneratedCSharpProgram TestGeneratedCode(AsmScenario discovery, IReadOnlyList<AsmScenario> executions) {
        using AsmRunScope discoveryScope = discovery.Prepare();
        AsmRunSettings discoverySettings = discoveryScope.Fixture.Settings;
        List<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates = new();
        List<(Func<AsmRunScope> Prepare, string? GateSkipReason)> executionEntries = new();
        foreach (AsmScenario execution in executions) {
            string baselineKey = discoveryScope.RunKey;
            if (!IsSameVariantAsDiscovery(execution, discoverySettings)) {
                (AsmRunSettings settings, AsmFinalState state, string runKey) =
                    RunEmulatedAndCheckMachineGoldens(execution, JitMode.InterpretedOnly);
                interpretedStates.Add((settings, state));
                baselineKey = runKey;
            }
            if (TestJitSettings.CompiledRun) {
                RunEmulatedAndCheckMachineGoldens(execution, JitMode.CompiledOnly);
            }
            executionEntries.Add((() => execution.Prepare(baselineKey),
                execution.GeneratedFinalStateGateSkipReason));
        }
        return TestGeneratedCodeCore(discoveryScope, executionEntries, interpretedStates);
    }

    /// <summary>Whether an execution scenario is another run of the same scenario variant as the
    /// discovery, which the discovery run already covers interpreted.</summary>
    private static bool IsSameVariantAsDiscovery(AsmScenario execution, AsmRunSettings discoverySettings) {
        using AsmRunScope scope = execution.Prepare();
        return IsEquivalentScenarioVariant(discoverySettings, scope.Fixture.Settings);
    }

    /// <summary>Runs one scenario emulated and checks its oracle, engine assertions and machine goldens:
    /// the interpreted run of every non-discovery variant, and the compiled-JIT run of every variant
    /// when <see cref="TestJitSettings.CompiledRun"/> opts in.</summary>
    /// <returns>The settings and interpreted final state of the run, the strict gate's baseline.</returns>
    private static (AsmRunSettings Settings, AsmFinalState State, string RunKey) RunEmulatedAndCheckMachineGoldens(
        AsmScenario scenario, JitMode jitMode) {
        using AsmRunScope scope = scenario.Prepare();
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, jitMode);
        string goldenKey = MachineGoldens.GoldenKey(scope.Fixture);
        MachineGoldens.CompareListingWithExpected(goldenKey, run.Machine);
        MachineGoldens.CompareCfgBlocksJsonWithExpected(goldenKey, run.Machine);
        return (scope.Fixture.Settings, AsmFinalState.Capture(run, scope.Fixture.Settings), scope.RunKey);
    }

    /// <summary>
    /// Generates C# from one prepared discovery run, compiles it, then runs every prepared execution run
    /// with the compiled override installed and checks its oracle. The generated source is compared with
    /// its golden at the end.
    /// </summary>
    /// <param name="discovery">Prepared discovery run whose execution is recorded for code generation.</param>
    /// <param name="executions">Prepared runs executed with the compiled override, in order.</param>
    /// <returns>The generated program.</returns>
    /// <exception cref="ArgumentException">Discovery and execution do not share the same golden key.</exception>
    public GeneratedCSharpProgram TestGeneratedCode(AsmRunScope discovery, IReadOnlyList<AsmRunScope> executions) {
        AsmRunSettings discoverySettings = discovery.Fixture.Settings;
        List<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates = new();
        foreach (AsmRunScope execution in executions.Where(execution =>
                !IsEquivalentScenarioVariant(discoverySettings, execution.Fixture.Settings))) {
            using AsmRunScope scope = new(execution.Fixture);
            using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);
            interpretedStates.Add((execution.Fixture.Settings,
                AsmFinalState.Capture(run, execution.Fixture.Settings)));
        }
        return TestGeneratedCodeCore(discovery,
            executions.Select<AsmRunScope, (Func<AsmRunScope> Prepare, string? GateSkipReason)>(
                scope => (() => scope, null)).ToList(), interpretedStates);
    }

    private GeneratedCSharpProgram TestGeneratedCodeCore(AsmRunScope discovery,
        IReadOnlyList<(Func<AsmRunScope> Prepare, string? GateSkipReason)> executions,
        IReadOnlyList<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates) {
        string goldenKey = GoldenKey(discovery.Fixture.Settings);
        GeneratedCSharpProgram generatedProgram = GenerateCompileAndRun(discovery, executions, interpretedStates);
        // Compared last: a behavioural regression (memory dump or caller assertion) is the more
        // important failure and must be the one xunit reports when both a behavioural and a golden
        // mismatch occur on the same run.
        CompareGeneratedSourceWithExpected(goldenKey, generatedProgram);

        return generatedProgram;
    }

    private GeneratedCSharpProgram GenerateCompileAndRun(AsmRunScope discovery,
        IReadOnlyList<(Func<AsmRunScope> Prepare, string? GateSkipReason)> executions,
        IReadOnlyList<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates) {
        AsmRunSettings discoverySettings = discovery.Fixture.Settings;
        string goldenKey = GoldenKey(discoverySettings);

        (GeneratedCSharpProgram generatedProgram, AsmFinalState discoveryFinalState) =
            GenerateSourceAndFinalState(discovery);
        using CompiledGeneratedOverride compiledOverride = CompileGeneratedProgram(discoverySettings, generatedProgram);
        string? probeReportPath = GeneratedCodeProbes.ReportPath;
        if (probeReportPath is null) {
            RunExecutions(executions, compiledOverride.Supplier, discoverySettings, goldenKey,
                discoveryFinalState, interpretedStates,
                new HashSet<SegmentedAddress>(generatedProgram.FramelessServiceCallSites),
                generatedProgram.StackSpaceCallSites,
                generatedProgram.DroppedStackSlotSites);
            return generatedProgram;
        }

        InstrumentedGeneratedSource instrumented = GeneratedCodeProbes.Instrument(generatedProgram);
        using CompiledGeneratedOverride probedOverride = new GeneratedOverrideCompiler().CompileSupplier(instrumented.Sources);
        RunExecutions(executions, probedOverride.Supplier, discoverySettings, goldenKey,
            discoveryFinalState, interpretedStates,
            new HashSet<SegmentedAddress>(generatedProgram.FramelessServiceCallSites),
            generatedProgram.StackSpaceCallSites,
            generatedProgram.DroppedStackSlotSites);
        GeneratedCodeProbes.Append(probeReportPath, goldenKey, instrumented,
            GeneratedCodeProbes.ReadHits(probedOverride.Supplier.GetType().Assembly));
        return generatedProgram;
    }

    /// <summary>Runs every execution with <paramref name="supplier"/> - oracle, engine assertions
    /// and generated-execution assertions - and gates its final state on the interpreted final
    /// state of its own scenario variant: the discovery variant uses the discovery run's state,
    /// every other variant the state its interpreted run captured. A scenario that skips the
    /// final-state gate still runs; it only records its skip reason in the report.</summary>
    private static void RunExecutions(
        IReadOnlyList<(Func<AsmRunScope> Prepare, string? GateSkipReason)> executions,
        IOverrideSupplier supplier, AsmRunSettings discoverySettings, string goldenKey,
        AsmFinalState discoveryFinalState,
        IReadOnlyList<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates,
        IReadOnlySet<SegmentedAddress> framelessServiceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int ArgumentBytes)> stackSpaceCallSites,
        IReadOnlyList<(SegmentedAddress Address, int Bytes)> droppedStackSlotSites) {
        foreach ((Func<AsmRunScope> prepare, string? gateSkipReason) in executions) {
            using AsmRunScope execution = prepare();
            string executionKey = GoldenKey(execution.Fixture.Settings);
            if (executionKey != goldenKey) {
                throw new ArgumentException(
                    $"Discovery golden key '{goldenKey}' differs from execution golden key '{executionKey}'.");
            }
            using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(execution, supplier);
            if (gateSkipReason is not null) {
                GeneratedCodeDifferentialReport.AppendGateSkipped(goldenKey, gateSkipReason);
                continue;
            }
            AsmFinalState baseline = InterpretedBaseline(execution.Fixture.Settings, discoverySettings,
                discoveryFinalState, interpretedStates);
            GeneratedCodeDifferentialReport.Check(goldenKey,
                execution.Fixture.Settings.GenerationPolicy, baseline,
                AsmFinalState.Capture(run, execution.Fixture.Settings), framelessServiceCallSites,
                stackSpaceCallSites, droppedStackSlotSites);
        }
    }

    /// <summary>The interpreted final state of <paramref name="execution"/>'s own scenario variant.</summary>
    private static AsmFinalState InterpretedBaseline(AsmRunSettings execution,
        AsmRunSettings discoverySettings, AsmFinalState discoveryFinalState,
        IReadOnlyList<(AsmRunSettings Settings, AsmFinalState State)> interpretedStates) {
        if (IsEquivalentScenarioVariant(discoverySettings, execution)) {
            return discoveryFinalState;
        }
        foreach ((AsmRunSettings settings, AsmFinalState state) in interpretedStates) {
            if (IsEquivalentScenarioVariant(settings, execution)) {
                return state;
            }
        }
        throw new InvalidOperationException(
            $"No interpreted final state for execution variant " +
            $"{execution.ScenarioIdentity}/{execution.VariantName}.");
    }

    /// <summary>
    /// Whether an execution run is another run of the same scenario variant as the discovery run: same
    /// stable identity and same logical variant. Compared by identity, not by delegate or host-path
    /// equality, so independently prepared but equivalent runs are still reported as a differential.
    /// </summary>
    private static bool IsEquivalentScenarioVariant(AsmRunSettings discovery, AsmRunSettings execution) {
        return discovery.ScenarioIdentity == execution.ScenarioIdentity
            && discovery.VariantName == execution.VariantName;
    }

    /// <summary>
    /// Compiles an existing generated program and records its metrics.
    /// </summary>
    /// <param name="settings">The settings used for discovery and the golden key.</param>
    /// <param name="generatedProgram">The source produced by that discovery run.</param>
    /// <returns>The compiled override, which the caller must dispose.</returns>
    public CompiledGeneratedOverride CompileGeneratedProgram(AsmRunSettings settings, GeneratedCSharpProgram generatedProgram) {
        GeneratedOverrideCompiler compiler = new();
        GeneratedCompilation compilation = compiler.Compile(generatedProgram);
        GeneratedCodeMetricsCollector.GeneratedCodePartMetrics parts = GeneratedCodeMetricsCollector.CollectParts(
            GoldenKey(settings), compilation);
        IReadOnlyDictionary<string, GeneratedCodeMetrics> methods =
            GeneratedCodeMetricsCollector.CollectMethods(GoldenKey(settings), compilation);
        WriteMetrics(parts, methods);
        WriteGotoCensus(GotoCensusLine.Format(GoldenKey(settings), generatedProgram.GotoCensus));
        generatedProgram.GotoCensus.Total.Should().Be(parts.Program.GotoStatements + parts.Support.GotoStatements,
            $"every printed goto of {GoldenKey(settings)} has exactly one census reason");
        WriteFlagReadCensus(FlagReadCensusLine.Format(GoldenKey(settings), generatedProgram.FlagReadCensus));
        generatedProgram.FlagReadCensus.Total.Should().Be(parts.Program.StatusFlagReads + parts.Support.StatusFlagReads,
            $"every printed flag read of {GoldenKey(settings)} has exactly one census line entry");
        WriteWebCensus(WebCensusLine.Format(GoldenKey(settings), generatedProgram.WebCensus));
        WriteUntypedCensus(UntypedCensusLine.Format(GoldenKey(settings), generatedProgram.UntypedMethods));
        // GeneratedCodeMetricsAssertions.WriteTightenedBounds(parts);
        GeneratedCodeMetricsAssertions.AssertInvariants(parts, generatedProgram.SourceText, methods);
        return compiler.CompileSupplier(compilation);
    }

    /// <summary>
    /// Builds the golden-file key for a run: the stable scenario identity, followed by one fixed suffix
    /// per enabled configuration of the <see cref="AsmRunSettings"/>, in a fixed order (PIT, A20, interrupt
    /// vectors, fail-on-port, XMS, EMS, non-default UMB, Sound Blaster model, OPL mode, speculation,
    /// non-default HLT, relocated handler segment). Runs of the same
    /// bin with different options therefore get distinct goldens instead of overwriting each other, and
    /// two scenarios that execute the same-named program (two START.BAT scenarios) get distinct goldens
    /// through their distinct identities. <c>MaxCycles</c>, <c>CDrive</c>, <c>VariantName</c> and
    /// <c>ConfigureMachine</c> are deliberately not part of the key. Every option is enumerated explicitly
    /// so adding one to the settings is a compile-visible decision here rather than a silent omission
    /// that lets two runs share a golden. Relocated installed callbacks append their segment because
    /// their addresses appear in generated source, and a run with DOS upper memory blocks disabled
    /// appends <c>.noumb</c> because it changes the machine the generated source encodes.
    /// </summary>
    /// <param name="settings">Settings to build the key from.</param>
    /// <returns>The golden-file key.</returns>
    public static string GoldenKey(AsmRunSettings settings) {
        string key = settings.ScenarioIdentity;
        if (settings.EnablePit) {
            key += ".pit";
        }
        if (settings.EnableA20Gate) {
            key += ".a20";
        }
        if (settings.InstallInterruptVectors) {
            key += ".ivt";
        }
        if (settings.FailOnUnhandledPort) {
            key += ".failport";
        }
        if (settings.EnableXms) {
            key += ".xms";
        }
        if (settings.EnableEms) {
            key += ".ems";
        }
        if (!settings.EnableUmb) {
            key += ".noumb";
        }
        if (settings.SbType != SbType.None) {
            key += "." + settings.SbType.ToString().ToLowerInvariant();
        }
        if (settings.OplMode != OplMode.None) {
            key += "." + settings.OplMode.ToString().ToLowerInvariant();
        }
        if (settings.EnableSpeculativeCfgExploration) {
            key += ".spec";
        }
        if (!settings.HltStopsEmulation) {
            key += ".hltwait";
        }
        key += settings.GenerationPolicy.GoldenSuffix;
        return key + settings.HandlerSegmentGoldenSuffix;
    }

    public GeneratedCSharpProgram GenerateProgramAndSource(AsmRunScope scope) {
        (GeneratedCSharpProgram generatedProgram, _) = GenerateSourceAndFinalState(scope);
        return generatedProgram;
    }

    /// <summary>Compares the generated source to its golden.</summary>
    public void CompareGeneratedSourceWithExpected(string goldenKey, GeneratedCSharpProgram generatedProgram) {
        List<string> actualLines = NormalizeLines(generatedProgram.SourceText);
        // WriteExpectedGeneratedSource(goldenKey, generatedProgram.SourceText);
        string resPath = GetGoldenPath(goldenKey + ".cs.txt");
        File.Exists(resPath).Should().BeTrue(
            $"golden '{goldenKey}.cs.txt' is missing; regenerate it by uncommenting the WriteExpectedGeneratedSource call in GeneratedCodeMachineTestRunner.CompareGeneratedSourceWithExpected, running the generated entry points once, then re-commenting it");
        List<string> expectedLines = NormalizeLines(File.ReadAllText(resPath));
        Assert.Equal(expectedLines, actualLines);
    }

    private static void WriteExpectedGeneratedSource(string goldenKey, string sourceText) {
        // Write directly to the source tree so the golden file is committed alongside the code. The
        // comparison reads the same path, so a golden written during a regeneration pass is readable
        // again in that same pass.
        File.WriteAllText(GetGoldenPath(goldenKey + ".cs.txt"), sourceText);
    }

    /// <summary>Path of a committed generated-source golden in the source tree, the file both the writer
    /// and the comparison use.</summary>
    /// <param name="fileName">File name of the golden.</param>
    /// <returns>Absolute path of that golden.</returns>
    private static string GetGoldenPath(string fileName) {
        string sourceDir = GetDirectoryName(GetSourceFilePath());
        return Path.Join(sourceDir, "Resources", "cpuTests", "res", "GeneratedCode", fileName);
    }

    private static List<string> NormalizeLines(string text) {
        return text.Replace("\r\n", "\n").Split('\n').ToList();
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private static string GetDirectoryName(string path) {
        return Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"No directory for path: {path}");
    }

    private static readonly object GeneratedSourceFileLock = new();

    private static void WriteGeneratedSource(string fileKey, GeneratedCSharpProgram generatedProgram) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string fileName = fileKey;
        foreach (char invalidChar in Path.GetInvalidFileNameChars()) {
            fileName = fileName.Replace(invalidChar, '_');
        }
        lock (GeneratedSourceFileLock) {
            File.WriteAllText(Path.Join(outputDirectory, fileName + ".generated.cs"), generatedProgram.SourceText);
        }
    }

    private static readonly object MetricsFileLock = new();
    private static readonly SortedDictionary<string, (string Summary, string[] Methods)> MetricsRows = new(StringComparer.Ordinal);

    private static void WriteMetrics(GeneratedCodeMetricsCollector.GeneratedCodePartMetrics parts,
        IReadOnlyDictionary<string, GeneratedCodeMetrics> methods) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string[] methodLines = methods.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"method={pair.Key} {pair.Value.SourceLocation} {pair.Value.ToLine()}").ToArray();
        lock (MetricsFileLock) {
            MetricsRows[parts.Program.Fixture] = ("part=program " + parts.Program.ToLine()
                + " | part=support " + parts.Support.ToLine(), methodLines);
            File.WriteAllLines(Path.Join(outputDirectory, "metrics.txt"), MetricsRows.Values.Select(row => row.Summary));
            File.WriteAllLines(Path.Join(outputDirectory, "metrics-methods.txt"), MetricsRows.Values.SelectMany(row => row.Methods));
        }
    }

    private static readonly object CensusFileLock = new();
    private static bool _censusFileInitialized;

    private static void WriteGotoCensus(string line) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string censusPath = Path.Join(outputDirectory, "goto-census.txt");
        lock (CensusFileLock) {
            if (!_censusFileInitialized) {
                File.Delete(censusPath);
                _censusFileInitialized = true;
            }
            File.AppendAllText(censusPath, line + Environment.NewLine);
        }
    }

    private static readonly object FlagReadCensusFileLock = new();
    private static bool _flagReadCensusFileInitialized;

    private static void WriteFlagReadCensus(string line) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string censusPath = Path.Join(outputDirectory, "flag-read-census.txt");
        lock (FlagReadCensusFileLock) {
            if (!_flagReadCensusFileInitialized) {
                File.Delete(censusPath);
                _flagReadCensusFileInitialized = true;
            }
            File.AppendAllText(censusPath, line + Environment.NewLine);
        }
    }

    private static readonly object WebCensusFileLock = new();
    private static bool _webCensusFileInitialized;

    private static void WriteWebCensus(string line) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string censusPath = Path.Join(outputDirectory, "web-census.txt");
        lock (WebCensusFileLock) {
            if (!_webCensusFileInitialized) {
                File.Delete(censusPath);
                _webCensusFileInitialized = true;
            }
            File.AppendAllText(censusPath, line + Environment.NewLine);
        }
    }

    private static readonly object UntypedCensusFileLock = new();
    private static bool _untypedCensusFileInitialized;

    private static void WriteUntypedCensus(string line) {
        string outputDirectory = Path.Join(AppContext.BaseDirectory, "generated-code");
        Directory.CreateDirectory(outputDirectory);
        string censusPath = Path.Join(outputDirectory, "untyped-census.txt");
        lock (UntypedCensusFileLock) {
            if (!_untypedCensusFileInitialized) {
                File.Delete(censusPath);
                _untypedCensusFileInitialized = true;
            }
            File.AppendAllText(censusPath, line + Environment.NewLine);
        }
    }

    private (GeneratedCSharpProgram GeneratedProgram, AsmFinalState FinalState) GenerateSourceAndFinalState(AsmRunScope scope) {
        (CfgPartitionedProgram program, AsmFinalState finalState) = GenerateProgram(scope);
        GeneratedCSharpProgram generatedProgram =
            new CfgCSharpGenerator(collectCensuses: true, verify: true, printAsmComments: false,
                scope.Fixture.Settings.GenerationPolicy).Generate(program);
        AssertAddressMapCoversEveryInstruction(program, generatedProgram);
        IrLiftCheck.Check(program, GoldenKey(scope.Fixture.Settings));
        WriteGeneratedSource(GoldenKey(scope.Fixture.Settings), generatedProgram);
        return (generatedProgram, finalState);
    }

    /// <summary>The address map covers every instruction of every printed method: each instruction
    /// address of the partitioned program has an entry, and every entry points to a line range
    /// inside its own file, inside its own method.</summary>
    private static void AssertAddressMapCoversEveryInstruction(CfgPartitionedProgram program,
        GeneratedCSharpProgram generated) {
        HashSet<string> expected = program.Partitions
            .SelectMany(partition => partition.Blocks)
            .SelectMany(block => block.Instructions.OfType<CfgInstruction>())
            .Select(instruction => instruction.Address.ToString())
            .ToHashSet();
        HashSet<string> mapped = generated.AddressMap.Select(entry => entry.Address).ToHashSet();
        mapped.Should().BeSubsetOf(expected,
            "every mapped instruction is an instruction of the partitioned program; coverage of "
            + "printed instructions is structural - the printer records one entry per "
            + "InstructionStartNode");
        Dictionary<string, string[]> fileLines = generated.Files
            .ToDictionary(file => file.Name, file => NormalizeLines(file.Text).ToArray());
        foreach (GeneratedAddressMapEntry entry in generated.AddressMap) {
            fileLines.Should().ContainKey(entry.File, $"entry {entry.Address} names an existing file");
            string[] lines = fileLines[entry.File];
            entry.FirstLine.Should().BeGreaterThanOrEqualTo(1);
            entry.FirstLine.Should().BeLessThanOrEqualTo(lines.Length,
                $"entry {entry.Address} starts inside {entry.File}");
            entry.LastLine.Should().BeLessThanOrEqualTo(lines.Length,
                $"entry {entry.Address} ends inside {entry.File}");
            int decl = Array.FindIndex(lines,
                line => line.Contains("public virtual") && line.Contains($"{entry.Method}("));
            decl.Should().BeGreaterThanOrEqualTo(0, $"entry {entry.Address}'s method {entry.Method} is declared in {entry.File}");
            int methodEnd = Array.FindIndex(lines, decl + 1,
                line => line.Equals("    }", StringComparison.Ordinal));
            methodEnd.Should().BeGreaterThanOrEqualTo(0, $"{entry.Method}'s block closes");
            (entry.FirstLine - 1).Should().BeGreaterThanOrEqualTo(decl,
                $"entry {entry.Address} starts inside {entry.Method}");
            (entry.LastLine - 1).Should().BeLessThanOrEqualTo(methodEnd,
                $"entry {entry.Address} ends inside {entry.Method}");
        }
    }

    /// <summary>The recorded program and finished-machine state of <paramref name="scope"/>'s
    /// discovery run: interpreter oracle and machine goldens checked, no generated C# written.</summary>
    internal static (CfgPartitionedProgram Program, AsmFinalState FinalState) GenerateProgram(AsmRunScope scope) {
        // The discovery run checks the scenario's oracle before anything is generated: a wrong expected
        // value fails discovery instead of surfacing later as a generated-code difference. The finished
        // machine is then compared with the listing and CFG goldens before the graph is exported.
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);

        Machine machine = run.Machine;
        string machineGoldenKey = MachineGoldens.GoldenKey(scope.Fixture);
        MachineGoldens.CompareListingWithExpected(machineGoldenKey, machine);
        MachineGoldens.CompareCfgBlocksJsonWithExpected(machineGoldenKey, machine);

        CfgBlockGraph graph = new CfgBlockGraphExporter().ExportFromExecutionContext(machine.CfgCpu.ExecutionContextManager, null).Graph;
        graph.Truncated.Should().BeFalse();

        GeneratorInputs inputs = run.DependencyInjection.GeneratorInputsCollector
            .Capture(run.Machine.CfgCpu.ExecutionContextManager);
        CfgPartitionedProgram program = new CfgFunctionPartitioner().Partition(graph,
            machine.CfgCpu.ExecutionContextManager, new FunctionCatalogue(), inputs);
        AsmFinalState finalState = AsmFinalState.Capture(run, scope.Fixture.Settings);
        return (program, finalState);
    }

    /// <summary>Owns the execution run scopes of one pipeline run: they are disposed when the run ends,
    /// before the discovery scope a <c>using</c> declaration disposes afterwards.</summary>
    private sealed class OwnedRunScopes : IDisposable {
        private readonly List<AsmRunScope> _scopes = new();

        public IReadOnlyList<AsmRunScope> Scopes => _scopes;

        public void Add(AsmRunScope scope) {
            _scopes.Add(scope);
        }

        public void Dispose() {
            foreach (AsmRunScope scope in _scopes) {
                scope.Dispose();
            }
        }
    }
}
