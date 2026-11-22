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
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;
using Spice86.Tests.CfgCpu.Blocks;

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
            _ => throw new InvalidOperationException("no label expected"));
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
    public void SiteRecordsTheInstructionPrintedBeforeIt() {
        // Arrange
        BranchProbeSites probes = new();
        SegmentedAddress address = new(0x1000, 0x0010);
        ICfgNode instruction = CfgTestHelpers.CreateInstruction(address);
        BlockNode node = new(
            new IfElseNode(Zero, new BlockNode(Call("Work")), new BlockNode()),
            new InstructionBoundaryNode(instruction, "", new BlockNode(
                new IfElseNode(Carry, new BlockNode(Call("Work")), new BlockNode()))));

        // Act
        Print(node, probes);

        // Assert
        probes.Sites[0].Address.Should().BeNull();
        probes.Sites[1].Address.Should().Be(address);
    }

    [Fact]
    public void SitesInKeepsOnlyTheProbesPresentInTheSource() {
        // Arrange
        BranchProbeSites probes = new();
        string first = probes.Branch(BranchProbeKind.If, "ZeroFlag");
        probes.Branch(BranchProbeKind.If, "CarryFlag");
        string third = probes.Selector("AX");

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
        json.Should().Contain("\"kind\": \"Switch\"");
        read.Methods.Should().ContainSingle();
        GeneratedMethodManifest method = read.Methods[0];
        method.Name.Should().Be("Sample");
        method.Entries.Should().Equal(manifest.Methods[0].Entries);
        method.Parameters.Should().Equal(manifest.Methods[0].Parameters);
        method.Outputs.Should().Equal(manifest.Methods[0].Outputs);
        method.Sites.Should().Equal(manifest.Methods[0].Sites);
        read.Sites.Select(site => site.Id).Should().Equal(0, 1);
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
    public void DefaultBuildHasNoProbeAndNoManifest(string fixtureName) {
        // Arrange
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;

        // Act
        GeneratedCSharpProgram defaultBuild = Generate(program, GeneratorOptions.Default);

        // Assert
        ProbeIdsIn(defaultBuild.SourceText).Should().BeEmpty();
        defaultBuild.Manifest.Should().BeNull();
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
        checkBuild.Manifest.Should().NotBeNull();
        GeneratedCodeManifest manifest = checkBuild.Manifest!;
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
