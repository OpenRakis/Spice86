namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;

using System.Text.RegularExpressions;

using Xunit;

/// <summary>
/// Checks the branch probe, device operation and manifest options of the generator: what the printers write
/// with probes on, that the default output has none, and that a check build runs like the default build while
/// reporting its branches.
/// </summary>
public sealed class BranchProbesTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private static readonly CpuFlagNode Carry = new(Flags.Carry);
    private static readonly Regex ProbeCall = new(@"(?<![A-Za-z0-9_.])(?:Branch|Selector)\((\d+), ");

    private sealed class Recorder : IBranchProbeObserver {
        public List<(int Site, bool Taken)> Branches { get; } = [];

        public List<(int Site, uint Value)> Selectors { get; } = [];

        public void OnBranch(int site, bool taken) => Branches.Add((site, taken));

        public void OnSelector(int site, uint value) => Selectors.Add((site, value));
    }

    private static BinaryOperationNode And(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.LOGICAL_AND, right);

    private static BinaryOperationNode Or(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.LOGICAL_OR, right);

    private static MethodCallNode Call(string name) => new(null, name);

    private static string Print(IVisitableAstNode node, BranchProbeSites? probes) {
        CSharpSourceWriter writer = new();
        CSharpExpressionPrinter expressions = new(new Dictionary<ushort, string>()) { BranchProbes = probes };
        CSharpStatementPrinter printer = new(writer, expressions,
            _ => throw new InvalidOperationException("no label expected"), printAsmComments: false);
        printer.Print(node);
        return writer.ToString();
    }

    [Fact]
    public void ConditionPrintsUnchangedWithoutProbes() {
        // Arrange
        IfElseNode node = new(And(Zero, Carry), new BlockNode(Call("Work")), new BlockNode());

        // Act
        string printed = Print(node, null);

        // Assert
        printed.Should().Contain("if (ZeroFlag && CarryFlag) {");
        printed.Should().NotContain("Branch(");
    }

    [Fact]
    public void EachOperandOfACombinedConditionGetsItsOwnProbe() {
        // Arrange
        BranchProbeSites probes = new() { Method = "Sample" };
        IfElseNode node = new(And(Zero, Carry), new BlockNode(Call("Work")), new BlockNode());

        // Act
        string printed = Print(node, probes);

        // Assert
        printed.Should().Contain("if (Branch(0, ZeroFlag) && Branch(1, CarryFlag)) {");
        probes.Sites.Select(site => (site.Id, site.Kind, site.Method, site.Text)).Should().Equal(
            (0, BranchProbeKind.If, "Sample", "ZeroFlag"), (1, BranchProbeKind.If, "Sample", "CarryFlag"));
    }

    [Fact]
    public void NestedCombinedConditionKeepsItsGrouping() {
        // Arrange
        BranchProbeSites probes = new();
        IfElseNode node = new(Or(And(Zero, Carry), Zero), new BlockNode(Call("Work")), new BlockNode());

        // Act
        string printed = Print(node, probes);

        // Assert
        printed.Should().Contain(
            "if ((Branch(0, ZeroFlag) && Branch(1, CarryFlag)) || Branch(2, ZeroFlag)) {");
    }

    [Fact]
    public void LoopsAndElseIfArmsGetProbesOfTheirKind() {
        // Arrange
        BranchProbeSites probes = new();
        BlockNode node = new(
            new WhileNode(Zero, new BlockNode(Call("Work"))),
            new DoWhileNode(new BlockNode(Call("Work")), Carry),
            new IfElseNode(Zero, new BlockNode(Call("Work")),
                new BlockNode(new IfElseNode(Carry, new BlockNode(Call("Other")), new BlockNode()))));

        // Act
        string printed = Print(node, probes);

        // Assert
        printed.Should().Contain("while (Branch(0, ZeroFlag)) {");
        printed.Should().Contain("} while (Branch(1, CarryFlag));");
        printed.Should().Contain("if (Branch(2, ZeroFlag)) {");
        printed.Should().Contain("else if (Branch(3, CarryFlag)) {");
        probes.Sites.Select(site => site.Kind).Should().Equal(BranchProbeKind.While, BranchProbeKind.DoWhile,
            BranchProbeKind.If, BranchProbeKind.If);
    }

    [Fact]
    public void SwitchValueGetsASelectorProbe() {
        // Arrange
        BranchProbeSites probes = new();
        SwitchNode node = new(new LoadOffsetParameterNode(),
            [new SwitchSection(1, new BlockNode(Call("Work"), new BreakNode()))],
            new BlockNode(new BreakNode()));
        string plain = Print(node, null);

        // Act
        string printed = Print(node, probes);

        // Assert
        printed.Should().Contain("switch (Selector(0, ");
        printed.Should().NotBe(plain);
        probes.Sites.Should().ContainSingle().Which.Kind.Should().Be(BranchProbeKind.Switch);
        plain.Should().Contain($"switch ({probes.Sites[0].Text})");
    }

    [Fact]
    public void ConditionalExpressionGetsAProbe() {
        // Arrange
        BranchProbeSites probes = new();
        ConditionalValueNode node = new(DataType.BOOL, Zero, Carry, Zero);
        CSharpExpressionPrinter expressions = new(new Dictionary<ushort, string>()) { BranchProbes = probes };

        // Act
        string printed = expressions.Print(node).Text;

        // Assert
        printed.Should().StartWith("Branch(0, ZeroFlag) ? ");
        probes.Sites.Should().ContainSingle().Which.Kind.Should().Be(BranchProbeKind.ConditionalValue);
    }

    [Fact]
    public void ConditionalExpressionProbesEachOperandBeforeItsArms() {
        // Arrange
        BranchProbeSites probes = new();
        ConditionalValueNode node = new(DataType.BOOL, And(Zero, Carry),
            new ConditionalValueNode(DataType.BOOL, Carry, Zero, Carry), Zero);
        CSharpExpressionPrinter expressions = new(new Dictionary<ushort, string>()) { BranchProbes = probes };

        // Act
        string printed = expressions.Print(node).Text;

        // Assert: the outer condition is numbered first, as it comes first in the source
        printed.Should().StartWith("(Branch(0, ZeroFlag) && Branch(1, CarryFlag)) ? Branch(2, CarryFlag) ? ");
        probes.Sites.Select(site => (site.Id, site.Kind, site.Text)).Should().Equal(
            (0, BranchProbeKind.ConditionalValue, "ZeroFlag"), (1, BranchProbeKind.ConditionalValue, "CarryFlag"),
            (2, BranchProbeKind.ConditionalValue, "CarryFlag"));
    }

    [Fact]
    public void ConstantConditionGetsNoProbe() {
        // Arrange
        BranchProbeSites probes = new();
        ConstantNode alwaysTrue = new(DataType.BOOL, 1);
        BlockNode node = new(
            new DoWhileNode(new BlockNode(Call("Work")), alwaysTrue),
            new IfElseNode(And(alwaysTrue, Carry), new BlockNode(Call("Work")), new BlockNode()));

        // Act
        string printed = Print(node, probes);

        // Assert: only the operand that can take both outcomes is probed
        printed.Should().Contain("} while (true);");
        printed.Should().Contain("if (true && Branch(0, CarryFlag)) {");
        probes.Sites.Should().ContainSingle().Which.Text.Should().Be("CarryFlag");
    }

    [Fact]
    public void ProbeRecordsTheOriginOfItsOperandOrOfTheConditionAroundIt() {
        // Arrange: the test of one instruction, or-ed with the test of another jump and with a condition of
        // no known origin
        BranchProbeSites probes = new();
        SegmentedAddress loop = new(0x1000, 0x0010);
        SegmentedAddress jump = new(0x1000, 0x0020);
        BinaryOperationNode ownTest = And(new CpuFlagNode(Flags.Zero), new CpuFlagNode(Flags.Carry));
        ownTest.BranchOrigin = new BranchOrigin(loop);
        CpuFlagNode otherJump = new(Flags.Sign) { BranchOrigin = new BranchOrigin(jump) };
        CpuFlagNode unknown = new(Flags.Overflow);
        IfElseNode node = new(Or(Or(ownTest, otherJump), unknown), new BlockNode(Call("Work")),
            new BlockNode());

        // Act
        Print(node, probes);

        // Assert: the operands of the instruction's own test share its address, the unknown one has none
        probes.Sites.Select(site => site.Address).Should().Equal(loop, loop, jump, null);
    }

    [Fact]
    public void SitesInKeepsOnlyTheProbesPresentInTheSource() {
        // Arrange
        BranchProbeSites probes = new();
        string first = probes.Branch(BranchProbeKind.If, "ZeroFlag", null);
        probes.Branch(BranchProbeKind.If, "CarryFlag", null);
        string third = probes.Selector("AX", null);

        // Act
        IReadOnlyList<BranchProbeSite> printed = probes.SitesIn([$"if ({first}) {{", $"switch ({third}) {{"]);

        // Assert
        printed.Select(site => site.Id).Should().Equal(0, 2);
    }

    [Fact]
    public void BranchAndSelectorReportAndReturnTheirArgument() {
        // Arrange
        using Spice86Creator creator = new(binName: "jump2");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        CSharpOverrideHelper helper = new(new Dictionary<SegmentedAddress, FunctionInformation>(), machine,
            Substitute.For<ILogger>(), new Configuration { HttpApiPort = 0 });

        // Act: without an observer the helpers only return their argument
        bool unobserved = helper.Branch(1, true);
        Recorder recorder = new();
        machine.BranchProbeObserver = recorder;
        bool taken = helper.Branch(7, true);
        bool notTaken = helper.Branch(8, false);
        ushort selected = helper.Selector(9, (ushort)0x1234);

        // Assert
        unobserved.Should().BeTrue();
        taken.Should().BeTrue();
        notTaken.Should().BeFalse();
        selected.Should().Be(0x1234);
        recorder.Branches.Should().Equal((7, true), (8, false));
        recorder.Selectors.Should().Equal((9, 0x1234u));
    }

    [Fact]
    public void ManifestSurvivesAJsonRoundTrip() {
        // Arrange
        GeneratedCodeManifest manifest = new([
            new GeneratedMethodManifest("Sample",
                [new MethodEntryManifest(0x1000, 0x0010), new MethodEntryManifest(0x1000, 0x0020)],
                [new MethodValueManifest("ax", "ushort", "AX")],
                [new MethodValueManifest("carryFlag", "bool", "CarryFlag")],
                [new ProbeSiteManifest(0, BranchProbeKind.If, 0x1000, 0x0012, "ax == 0"),
                    new ProbeSiteManifest(1, BranchProbeKind.Switch, null, null, "BX")])
        ]);

        // Act
        string json = manifest.ToJson();
        GeneratedCodeManifest read = GeneratedCodeManifest.FromJson(json);

        // Assert
        json.Should().Contain("\"kind\":\"Switch\"");
        json.Should().Contain("\"synthetic\":true");
        read.Sites.Select(site => site.Synthetic).Should().Equal(false, true);
        read.Methods.Should().ContainSingle();
        GeneratedMethodManifest method = read.Methods[0];
        method.Name.Should().Be("Sample");
        method.Entries.Should().Equal(manifest.Methods[0].Entries);
        method.Parameters.Should().Equal(manifest.Methods[0].Parameters);
        method.Outputs.Should().Equal(manifest.Methods[0].Outputs);
        method.Sites.Should().Equal(manifest.Methods[0].Sites);
        read.Sites.Select(site => site.Id).Should().Equal(0, 1);
    }

    [Fact]
    public void ManifestAddressMapSurvivesAJsonRoundTrip() {
        // Arrange: two methods, each in its file with instructions; the second instruction of the
        // first method prints nothing, so its range is empty (LastLine = FirstLine - 1).
        GeneratedCodeManifest manifest = new([
                new GeneratedMethodManifest("First",
                    [new MethodEntryManifest(0xF000, 0x0010)],
                    [], [], []),
                new GeneratedMethodManifest("Second",
                    [new MethodEntryManifest(0xF000, 0x0040)],
                    [new MethodValueManifest("ax", "ushort", "AX")],
                    [new MethodValueManifest("result", "ushort", "BX")],
                    [])
            ]) {
            AddressMap = [
                new AddressMapEntryManifest("F000:0010", "CfgGeneratedOverrides.F000.cs", "First", 12, 14),
                new AddressMapEntryManifest("F000:0013", "CfgGeneratedOverrides.F000.cs", "First", 15, 14),
                new AddressMapEntryManifest("F000:0040", "CfgGeneratedOverrides.F000.cs", "Second", 20, 23)
            ]
        };

        // Act
        GeneratedCodeManifest read = GeneratedCodeManifest.FromJson(manifest.ToJson());

        // Assert
        read.AddressMap.Should().Equal(manifest.AddressMap);
        read.Methods.Select(method => method.Name).Should().Equal("First", "Second");
        read.Methods[1].Parameters.Should().Equal(manifest.Methods[1].Parameters);
        read.Methods[1].Outputs.Should().Equal(manifest.Methods[1].Outputs);
    }

    private static GeneratedCSharpProgram Generate(CfgPartitionedProgram program, GeneratorOptions options) {
        return new CfgCSharpGenerator(options with { CollectCensuses = true, Verify = true }).Generate(program);
    }

    private static HashSet<int> ProbeIdsIn(string source) {
        return ProbeCall.Matches(source).Select(match => int.Parse(match.Groups[1].Value)).ToHashSet();
    }

    [Theory]
    [InlineData("if_conversion")]
    [InlineData("partition_jump_table")]
    public void DefaultBuildHasNoProbeAndItsManifestNoSite(string fixtureName) {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;

        // Act
        GeneratedCSharpProgram defaultBuild = Generate(program, GeneratorOptions.Default);

        // Assert: the manifest is written at every dump, but probe sites stay conditional on the probes
        ProbeIdsIn(defaultBuild.SourceText).Should().BeEmpty();
        defaultBuild.Manifest.Sites.Should().BeEmpty();
        defaultBuild.Manifest.Methods.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("if_conversion")]
    [InlineData("partition_jump_table")]
    public void CheckBuildListsEveryPrintedProbeInTheManifest(string fixtureName) {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;

        // Act
        GeneratedCSharpProgram checkBuild = Generate(program, GeneratorOptions.CheckBuild);

        // Assert: the ids in the source and in the manifest are the same set, each listed once
        GeneratedCodeManifest manifest = checkBuild.Manifest;
        HashSet<int> printed = ProbeIdsIn(checkBuild.SourceText);
        printed.Should().NotBeEmpty();
        manifest.Sites.Select(site => site.Id).Should().OnlyHaveUniqueItems();
        manifest.Sites.Select(site => site.Id).Should().BeEquivalentTo(printed);

        // Assert: every method is listed with an entry, and a site belongs to the method that prints it
        manifest.Methods.Should().NotBeEmpty();
        manifest.Methods.Should().OnlyContain(method => method.Entries.Count > 0);
        foreach (ProbeSiteManifest site in manifest.Sites) {
            string call = site.Kind == BranchProbeKind.Switch ? "Selector" : "Branch";
            checkBuild.SourceText.Should().Contain($"{call}({site.Id}, {site.Text})");
        }
    }

    [Theory]
    [InlineData("if_conversion")]
    [InlineData("partition_jump_table")]
    public void CheckBuildPassesTheOracleAndReportsItsBranches(string fixtureName) {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        AsmFixture fixture = discovery.Fixture;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCSharpProgram checkBuild = Generate(program, GeneratorOptions.CheckBuild);
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(checkBuild);
        Recorder recorder = new();
        AsmFixture observed = new(
            fixture.Settings with { ConfigureMachine = machine => machine.BranchProbeObserver = recorder },
            fixture.ExpectedMemory, fixture.AssertResult);

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(observed, compiled.Supplier);

        // Assert: every reported site is in the manifest, with the kind its helper stands for
        Dictionary<int, ProbeSiteManifest> sites = checkBuild.Manifest!.Sites.ToDictionary(site => site.Id);
        (recorder.Branches.Count + recorder.Selectors.Count).Should().BeGreaterThan(0);
        foreach ((int site, bool _) in recorder.Branches) {
            sites.Should().ContainKey(site).WhoseValue.Kind.Should().NotBe(BranchProbeKind.Switch);
        }
        foreach ((int site, uint _) in recorder.Selectors) {
            sites.Should().ContainKey(site).WhoseValue.Kind.Should().Be(BranchProbeKind.Switch);
        }
    }

    [Fact]
    public void JumpTableProgramReportsItsSwitchValue() {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition("partition_jump_table"));
        AsmFixture fixture = discovery.Fixture;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCSharpProgram checkBuild = Generate(program, GeneratorOptions.CheckBuild);
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(checkBuild);
        Recorder recorder = new();
        AsmFixture observed = new(
            fixture.Settings with { ConfigureMachine = machine => machine.BranchProbeObserver = recorder },
            fixture.ExpectedMemory, fixture.AssertResult);

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(observed, compiled.Supplier);

        // Assert
        checkBuild.Manifest!.Sites.Should().Contain(site => site.Kind == BranchProbeKind.Switch);
        recorder.Selectors.Should().NotBeEmpty();
    }

    /// <summary>The manifest of the check build of a fixture, and the instructions of the program it was
    /// generated from, one per address.</summary>
    private static (GeneratedCodeManifest Manifest, Dictionary<SegmentedAddress, CfgInstruction> Instructions)
        CheckBuildOf(string fixtureName) {
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCodeManifest manifest = Generate(program, GeneratorOptions.CheckBuild).Manifest
            ?? throw new InvalidOperationException($"The check build of {fixtureName} has no manifest.");
        Dictionary<SegmentedAddress, CfgInstruction> instructions = program.Partitions
            .SelectMany(partition => partition.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<CfgInstruction>()
            .DistinctBy(instruction => instruction.Address)
            .ToDictionary(instruction => instruction.Address);
        return (manifest, instructions);
    }

    private static SegmentedAddress? AddressOf(ProbeSiteManifest site) =>
        site is { Segment: { } segment, Offset: { } offset } ? new SegmentedAddress(segment, offset) : null;

    private static CfgInstruction? InstructionOf(ProbeSiteManifest site,
        Dictionary<SegmentedAddress, CfgInstruction> instructions) =>
        AddressOf(site) is { } address ? instructions.GetValueOrDefault(address) : null;

    /// <summary>A jump with a taken and a fall-through successor: a Jcc, JCXZ or LOOP.</summary>
    private static bool IsConditionalJump(CfgInstruction? instruction) =>
        instruction is { IsJump: true, StaticSuccessorAddresses.Count: 2 };

    /// <summary>A jump whose target is read at run time.</summary>
    private static bool IsIndirectJump(CfgInstruction? instruction) =>
        instruction is { IsJump: true, StaticSuccessorAddresses.Count: 0 };

    [Theory]
    [InlineData("if_conversion")]
    [InlineData("partition_jump_table")]
    [InlineData("dataflow_rewrite_traps")]
    public void EachSiteRecordsTheInstructionThatDecidesIt(string fixtureName) {
        // Arrange
        (GeneratedCodeManifest manifest, Dictionary<SegmentedAddress, CfgInstruction> instructions) =
            CheckBuildOf(fixtureName);

        // Act
        List<(ProbeSiteManifest Site, CfgInstruction? Instruction)> sites = manifest.Sites
            .Select(site => (Site: site, Instruction: InstructionOf(site, instructions)))
            .ToList();

        // Assert: every site has the address of an instruction of the program
        sites.Should().NotBeEmpty();
        sites.Should().OnlyContain(pair => !pair.Site.Synthetic && pair.Instruction != null);
        // Assert: a switch dispatches at an indirect jump, and no branch site is at an unconditional jump
        sites.Where(pair => pair.Site.Kind == BranchProbeKind.Switch && !IsIndirectJump(pair.Instruction))
            .Should().BeEmpty();
        sites.Where(pair => pair.Site.Kind != BranchProbeKind.Switch && pair.Instruction is { IsJump: true }
                && !IsConditionalJump(pair.Instruction))
            .Should().BeEmpty();
        // Assert: each conditional jump has its own site, also when the structurer joins the tests of several
        // jumps into one condition with && and ||
        foreach (CfgInstruction jump in instructions.Values.Where(IsConditionalJump)) {
            sites.Count(pair => ReferenceEquals(pair.Instruction, jump))
                .Should().Be(1, $"the jump at {jump.Address} decides one printed condition");
        }
    }

    [Theory]
    [InlineData("partition_jump_table")]
    [InlineData("dataflow_rewrite_traps")]
    public void BranchSitesOfAProgramOfJumpsAreItsConditionalJumps(string fixtureName) {
        // Arrange
        (GeneratedCodeManifest manifest, Dictionary<SegmentedAddress, CfgInstruction> instructions) =
            CheckBuildOf(fixtureName);

        // Act
        List<CfgInstruction?> deciding = manifest.Sites
            .Where(site => site.Kind != BranchProbeKind.Switch)
            .Select(site => InstructionOf(site, instructions))
            .ToList();

        // Assert: never the compare before the jump, or the last instruction printed before the condition
        deciding.Should().NotBeEmpty();
        deciding.Should().OnlyContain(instruction => IsConditionalJump(instruction));
    }

    [Fact]
    public void ConvertedValueChoiceRecordsTheInstructionItCameFrom() {
        // Arrange
        (GeneratedCodeManifest manifest, Dictionary<SegmentedAddress, CfgInstruction> instructions) =
            CheckBuildOf("if_conversion");

        // Act
        List<InstructionOperation?> operations = manifest.Sites
            .Where(site => site.Kind == BranchProbeKind.ConditionalValue)
            .Select(site => InstructionOf(site, instructions)?.DisplayAst.Operation)
            .ToList();

        // Assert: each conditional value comes from the setcc or salc whose internal branch was converted
        operations.Should().NotBeEmpty();
        operations.Should().OnlyContain(operation => operation == InstructionOperation.SALC
            || (operation != null && operation.Value.ToString().StartsWith("SET", StringComparison.Ordinal)));
    }

    [Fact]
    public void ComparisonPackedFromSeveralJumpsIsSynthetic() {
        // Arrange
        (GeneratedCodeManifest manifest, Dictionary<SegmentedAddress, CfgInstruction> instructions) =
            CheckBuildOf("dataflow_wide_conditions");

        // Act
        ILookup<bool, ProbeSiteManifest> bySynthetic = manifest.Sites.ToLookup(site => site.Synthetic);

        // Assert: a 32-bit comparison packed from the high and low word tests has no address
        bySynthetic[true].Should().NotBeEmpty();
        bySynthetic[true].Should().OnlyContain(site => site.Segment == null && site.Offset == null
            && site.Text.Contains("<< 16", StringComparison.Ordinal));
        // Assert: every other site has the address of its jump, each operand of a merged range test included
        bySynthetic[false].Should().NotBeEmpty();
        bySynthetic[false].Should().NotContain(site => site.Text.Contains("<< 16", StringComparison.Ordinal));
        bySynthetic[false].Should().OnlyContain(site => IsConditionalJump(InstructionOf(site, instructions)));
    }

    [Fact]
    public void DeviceOperationsOffPrintsEveryPortAccessOnItsOwn() {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.DeviceOperations());
        AsmFixture fixture = discovery.Fixture;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;

        // Act
        GeneratedCSharpProgram defaultBuild = Generate(program, GeneratorOptions.Default);
        GeneratedCSharpProgram single =
            Generate(program, GeneratorOptions.Default with { DeviceOperations = false });
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(single);
        using AsmFixtureRun run = AsmFixtureRunner.RunWithOverride(fixture, compiled.Supplier);

        // Assert: the default build combines the PIT and CMOS sequences, the other build has no such call
        defaultBuild.SourceText.Should().Contain("Pit.ProgramChannel(");
        defaultBuild.SourceText.Should().Contain("Cmos.WriteRegister(");
        single.SourceText.Should().NotContain("Pit.ProgramChannel(");
        single.SourceText.Should().NotContain("Cmos.WriteRegister(");
        single.SourceText.Should().NotContain("Cmos.ReadRegister(");
    }
}
