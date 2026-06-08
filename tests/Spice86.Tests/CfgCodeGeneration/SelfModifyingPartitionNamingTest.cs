namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

using Xunit;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Self-modifying code can split several partitions onto the same entry address (a selector node and its
/// instruction variants live at one address but become distinct partitions). This locks in that the
/// generator (1) gives each such partition a unique C# method name so the source compiles instead of
/// emitting duplicate methods (CS0111), and (2) emits a single <c>DefineFunction</c> per address - the
/// selector one - so installing the overrides does not throw on a duplicate address registration.
/// </summary>
public class SelfModifyingPartitionNamingTest {
    private static readonly SegmentedAddress SharedAddress = new(0x3409, 0x0025);

    /// <summary>
    /// Minimal CFG node standing in for a self-modifying instruction variant. Naming and plan building read
    /// only its address and id; the body-lowering ASTs are never reached by those stages.
    /// </summary>
    private sealed class VariantNode(int id, SegmentedAddress address) : CfgNode(id, address, null) {
        public override bool IsLive => true;
        public override void UpdateSuccessorCache() { }
        public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) => null;
        public override IVisitableAstNode DisplayAst => throw new NotSupportedException();
        public override IVisitableAstNode ExecutionAst => throw new NotSupportedException();
    }

    private static CfgCodePartition PartitionWith(int id, ICfgNode entryNode) {
        CfgBlock block = new(id * 10, entryNode);
        CfgCodePartitionEntry entry = new() { Node = entryNode, Kind = CfgCodePartitionEntryKind.FunctionEntry };
        return new CfgCodePartition {
            Id = id,
            Kind = CfgCodePartitionKind.Observed,
            Name = "unknown",
            Blocks = [block],
            Entries = [entry]
        };
    }

    [Fact]
    public void PartitionsSharingEntryAddressGetUniqueMethodNames() {
        CfgCodePartition selectorPartition = PartitionWith(1, new CfgSelectorNode(1001, SharedAddress));
        CfgCodePartition variantA = PartitionWith(2, new VariantNode(1002, SharedAddress));
        CfgCodePartition variantB = PartitionWith(3, new VariantNode(1003, SharedAddress));
        CfgPartitionedProgram program = new() {
            Partitions = [selectorPartition, variantA, variantB],
            Transfers = []
        };

        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program);

        List<string> names = [
            analysis.Context.GetMethodName(selectorPartition),
            analysis.Context.GetMethodName(variantA),
            analysis.Context.GetMethodName(variantB)
        ];
        names.Distinct().Should().HaveCount(3, "each partition at the same address must compile to its own method");
        // Disambiguated names keep the address as the trailing three underscore tokens so the symbol still
        // round-trips through the Ghidra symbol parser (segment_offset_linear).
        names.Should().AllSatisfy(name => name.Should().EndWith("3409_0025_340B5"));
    }

    [Fact]
    public void OnlySelectorPartitionIsRegisteredAtSharedAddress() {
        CfgCodePartition variantA = PartitionWith(2, new VariantNode(1002, SharedAddress));
        CfgCodePartition selectorPartition = PartitionWith(5, new CfgSelectorNode(1005, SharedAddress));
        CfgCodePartition variantB = PartitionWith(7, new VariantNode(1007, SharedAddress));
        CfgPartitionedProgram program = new() {
            Partitions = [variantA, selectorPartition, variantB],
            Transfers = []
        };

        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program);
        GenerationPlan plan = GenerationPlanBuilder.Build(analysis.Context);

        List<OverrideRegistration> atSharedAddress = plan.OverrideRegistrations
            .Where(registration => registration.Offset == SharedAddress.Offset)
            .ToList();
        atSharedAddress.Should().HaveCount(1, "the address-keyed override catalogue holds one override per address");
        atSharedAddress[0].MethodName.Should().Be(analysis.Context.GetMethodName(selectorPartition),
            "the selector partition dispatches to the right variant at runtime, so it owns the address registration");
    }
}
