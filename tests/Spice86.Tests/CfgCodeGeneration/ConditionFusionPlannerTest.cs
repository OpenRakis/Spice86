namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

public class ConditionFusionPlannerTest {
    private static readonly SequentialIdAllocator ParserIds = new(2_000_000);
    private static readonly SegmentedAddress SetterAddress = new(0x1000, 0x0010);
    private static readonly SegmentedAddress JumpAddress = new(0x1000, 0x0012);
    private static readonly SegmentedAddress SecondAddress = new(0x1000, 0x0012);
    private static readonly SegmentedAddress ThirdAddress = new(0x1000, 0x0014);

    [Fact]
    public void FusesAnAdjacentCompareAndJumpWhenNoFlagIsLiveAfter() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);
        IfElseNode originalJump = (IfElseNode)jumpIfZero.ExecutionAst;

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, Plans(compare, jumpIfZero), StatusFlags.None);

        // Assert
        IfElseNode fusedJump = (IfElseNode)fusion.ExecutionAstOf(jumpIfZero);
        Print(fusedJump.Condition).Should().Be("AX == BX");
        fusedJump.TrueCase.Should().BeSameAs(originalJump.TrueCase);
        fusedJump.FalseCase.Should().BeSameAs(originalJump.FalseCase);
        BlockNode fusedSetter = (BlockNode)fusion.ExecutionAstOf(compare);
        fusedSetter.Statements.Should().ContainSingle().Which.Should().BeOfType<MoveIpNextNode>();
    }

    [Fact]
    public void KeepsThePairWhenAFlagIsLiveAfterTheJump() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, Plans(compare, jumpIfZero), StatusFlags.Carry);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsThePairWhenTheJumpIsAMethodEntry() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = ConditionFusionPlanner.Plan(Plans(compare, jumpIfZero),
            new HashSet<ICfgNode> { jumpIfZero }, _ => StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsThePairWhenAnEventCheckSeparatesThem() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);
        CfgBlock block = Block(compare, jumpIfZero);
        NodeEmissionPlan[] checkBeforeJump = [
            new(compare, block, true, false, false), new(jumpIfZero, block, false, true, false)
        ];
        NodeEmissionPlan[] checkAfterSetter = [
            new(compare, block, true, false, true), new(jumpIfZero, block, false, false, false)
        ];

        // Act
        ConditionFusion beforeJump = Plan(jumpIfZero, checkBeforeJump, StatusFlags.None);
        ConditionFusion afterSetter = Plan(jumpIfZero, checkAfterSetter, StatusFlags.None);

        // Assert
        AssertUnchanged(beforeJump, compare, jumpIfZero);
        AssertUnchanged(afterSetter, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsThePairAcrossBlocks() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);
        NodeEmissionPlan[] plans = [
            new(compare, new CfgBlock(1, compare), true, false, false),
            new(jumpIfZero, new CfgBlock(2, jumpIfZero), true, false, false)
        ];

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, plans, StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsThePairWhenTheSetterFaulted() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);
        compare.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> { CfgTestHelpers.CreateInstruction(new SegmentedAddress(0xF000, 0x0000)) };

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, Plans(compare, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsJumpsWithoutAnExactRelationAndNonSetters() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfOverflow = Parse(JumpAddress, 0x70, 0x05);
        CfgInstruction move = Parse(SetterAddress, 0x89, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);

        // Act
        ConditionFusion overflow = Plan(jumpIfOverflow, Plans(compare, jumpIfOverflow), StatusFlags.None);
        ConditionFusion afterMove = Plan(jumpIfZero, Plans(move, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(overflow, compare, jumpIfOverflow);
        AssertUnchanged(afterMove, move, jumpIfZero);
    }

    [Fact]
    public void FusesASignTestAfterACompare() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction jumpIfSign = Parse(JumpAddress, 0x78, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfSign, Plans(compare, jumpIfSign), StatusFlags.None);

        // Assert
        Print(((IfElseNode)fusion.ExecutionAstOf(jumpIfSign)).Condition).Should().Be("(short)(AX - BX) < 0");
        ((BlockNode)fusion.ExecutionAstOf(compare)).Statements.Should().ContainSingle().Which.Should().BeOfType<MoveIpNextNode>();
    }

    [Fact]
    public void TestsTheAssignedResultAndKeepsTheSetterEvenWhenFlagsAreLiveAfter() {
        // Arrange
        CfgInstruction subtract = Parse(SetterAddress, 0x29, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, Plans(subtract, jumpIfZero), StatusFlags.All);

        // Assert
        Print(((IfElseNode)fusion.ExecutionAstOf(jumpIfZero)).Condition).Should().Be("AX == 0");
        fusion.ExecutionAstOf(subtract).Should().BeSameAs(subtract.ExecutionAst);
    }

    [Fact]
    public void KeepsTheResultTestOutWhenTheJumpIsAMethodEntry() {
        // Arrange
        CfgInstruction subtract = Parse(SetterAddress, 0x29, 0xD8);
        CfgInstruction jumpIfZero = Parse(JumpAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = ConditionFusionPlanner.Plan(Plans(subtract, jumpIfZero),
            new HashSet<ICfgNode> { jumpIfZero }, _ => StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, subtract, jumpIfZero);
    }

    [Fact]
    public void FusesACompareWhoseOperandsSurviveTheInstructionsInBetween() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction move = Parse(SecondAddress, 0x89, 0xD1);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, move, jumpIfZero), StatusFlags.None);

        // Assert
        Print(((IfElseNode)fusion.ExecutionAstOf(jumpIfZero)).Condition).Should().Be("AX == BX");
        ((BlockNode)fusion.ExecutionAstOf(compare)).Statements.Should().ContainSingle().Which.Should().BeOfType<MoveIpNextNode>();
        fusion.ExecutionAstOf(move).Should().BeSameAs(move.ExecutionAst);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0xD0 })]
    [InlineData(new byte[] { 0xB0, 0x01 })]
    [InlineData(new byte[] { 0xB4, 0x01 })]
    public void KeepsACompareWhenAnInstructionInBetweenWritesAnOperand(byte[] betweenBytes) {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction between = Parse(SecondAddress, betweenBytes);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, between, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsACompareWhenAnInstructionInBetweenReadsFlags() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD9);
        CfgInstruction loadFlags = Parse(SecondAddress, 0x9F);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, loadFlags, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsACompareWithAMemoryOperandWhenAnInstructionSeparatesIt() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x3B, 0x07);
        CfgInstruction move = Parse(SecondAddress, 0x89, 0xD1);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, move, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsACompareOnTheStackPointerWhenAPushSeparatesIt() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xDC);
        CfgInstruction push = Parse(SecondAddress, 0x50);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, push, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsACompareWhenAnInstructionInBetweenIsAMethodEntry() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction move = Parse(SecondAddress, 0x89, 0xD1);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = ConditionFusionPlanner.Plan(PlansInOneBlock(compare, move, jumpIfZero),
            new HashSet<ICfgNode> { move }, _ => StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void KeepsACompareWhenAnEventCheckFollowsAnInstructionInBetween() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction move = Parse(SecondAddress, 0x89, 0xD1);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);
        NodeEmissionPlan[] plans = PlansInOneBlock(compare, move, jumpIfZero);
        plans[1] = plans[1] with { EmitsExternalEventCheckAfter = true };

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, plans, StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, compare, jumpIfZero);
    }

    [Fact]
    public void TheNearestFlagWriterIsTheSetter() {
        // Arrange
        CfgInstruction compare = Parse(SetterAddress, 0x39, 0xD8);
        CfgInstruction increment = Parse(SecondAddress, 0x41);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(compare, increment, jumpIfZero), StatusFlags.None);

        // Assert
        Print(((IfElseNode)fusion.ExecutionAstOf(jumpIfZero)).Condition).Should().Be("CX == 0");
        fusion.ExecutionAstOf(compare).Should().BeSameAs(compare.ExecutionAst);
        fusion.ExecutionAstOf(increment).Should().BeSameAs(increment.ExecutionAst);
    }

    [Fact]
    public void TestsAResultThatSurvivesTheInstructionsInBetween() {
        // Arrange
        CfgInstruction subtract = Parse(SetterAddress, 0x29, 0xD8);
        CfgInstruction pushFlags = Parse(SecondAddress, 0x9C);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(subtract, pushFlags, jumpIfZero), StatusFlags.All);

        // Assert
        Print(((IfElseNode)fusion.ExecutionAstOf(jumpIfZero)).Condition).Should().Be("AX == 0");
        fusion.ExecutionAstOf(subtract).Should().BeSameAs(subtract.ExecutionAst);
    }

    [Fact]
    public void KeepsAResultTestWhenTheDestinationIsWrittenInBetween() {
        // Arrange
        CfgInstruction subtract = Parse(SetterAddress, 0x29, 0xD8);
        CfgInstruction move = Parse(SecondAddress, 0xB0, 0x01);
        CfgInstruction jumpIfZero = Parse(ThirdAddress, 0x74, 0x05);

        // Act
        ConditionFusion fusion = Plan(jumpIfZero, PlansInOneBlock(subtract, move, jumpIfZero), StatusFlags.None);

        // Assert
        AssertUnchanged(fusion, subtract, jumpIfZero);
    }

    private static ConditionFusion Plan(CfgInstruction jump, IReadOnlyList<NodeEmissionPlan> plans, StatusFlags liveAfterJump) =>
        ConditionFusionPlanner.Plan(plans, new HashSet<ICfgNode>(), node => node.Equals(jump) ? liveAfterJump : StatusFlags.All);

    private static NodeEmissionPlan[] Plans(CfgInstruction setter, CfgInstruction jump) {
        CfgBlock block = Block(setter, jump);
        return [new(setter, block, true, false, false), new(jump, block, false, false, false)];
    }

    private static NodeEmissionPlan[] PlansInOneBlock(params CfgInstruction[] nodes) {
        CfgBlock block = new(1, nodes[0]);
        foreach (CfgInstruction node in nodes.Skip(1)) {
            block.Append(node);
        }
        return nodes.Select((node, index) => new NodeEmissionPlan(node, block, index == 0, false, false)).ToArray();
    }

    private static CfgBlock Block(CfgInstruction setter, CfgInstruction jump) {
        CfgBlock block = new(1, setter);
        block.Append(jump);
        return block;
    }

    private static void AssertUnchanged(ConditionFusion fusion, CfgInstruction setter, CfgInstruction jump) {
        fusion.ExecutionAstOf(setter).Should().BeSameAs(setter.ExecutionAst);
        fusion.ExecutionAstOf(jump).Should().BeSameAs(jump.ExecutionAst);
    }

    private static string Print(ValueNode node) =>
        new CSharpExpressionPrinter(new Dictionary<ushort, string>()).Print(node).Text;

    private static CfgInstruction Parse(SegmentedAddress address, params byte[] bytes) {
        Memory memory = new(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate(), new RealModeMmu8086(), false);
        State state = new(CpuModel.INTEL_80386);
        InstructionParser parser = new(memory, state, ParserIds);
        for (int index = 0; index < bytes.Length; index++) {
            memory.UInt8[address.Linear + (uint)index] = bytes[index];
        }
        return parser.ParseInstructionAt(address);
    }
}
