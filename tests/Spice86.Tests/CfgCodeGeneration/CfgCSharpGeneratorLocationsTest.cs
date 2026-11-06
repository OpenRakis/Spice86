namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The globals by location: a register proven equal to a lower class-order register prints
/// through that register's class, a register changed after the copy keeps its own class, a
/// location property exports a Ghidra label, and a shared pointer under conflicting proofs
/// demotes to the own register class.
/// </summary>
public class CfgCSharpGeneratorLocationsTest {
    private static CfgCodePartition PartitionOf(CfgBlock block) => new() {
        Id = 1,
        Kind = CfgCodePartitionKind.Observed,
        Name = "test",
        Blocks = [block],
        Entries = SingleEntry(block)
    };

    private static GeneratedCSharpProgram Generate(params (ushort Offset, byte[] Bytes)[] instructions) {
        CfgCodePartition partition = PartitionOf(Linear(new TestInstructionHelper(), instructions));
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramEntryPoint = partition.Entries[0].Address
            }
        };
        return new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(program);
    }

    [Fact]
    public void RegisterProvenEqualToALowerOrderRegisterPrintsThroughItsClass() {
        // Arrange: ES is pushed from an unknown DS; the ES access merges into the DS class.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x1E]),                 // push ds
            (0x0001, [0x07]),                 // pop es
            (0x0002, [0x26, 0xA1, 0x30, 0x00]), // mov ax, es:[0x0030]
            (0x0006, [0xC3]));                // ret

        // Assert
        program.SourceText.Should().Contain("AX = DsGlobals.Word0030;");
        program.Files.Select(file => file.Name).Should().NotContain("EsGlobals.cs");
        AssertCompiles(program);
    }

    [Fact]
    public void RegisterChangedAfterTheCopyKeepsItsOwnClass() {
        // Arrange: ES is pushed from DS, then DS is reloaded: the ES access stays on EsGlobals.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0x1E]),                 // push ds
            (0x0001, [0x07]),                 // pop es
            (0x0002, [0xB8, 0x01, 0x20]),     // mov ax, 0x2001
            (0x0005, [0x50]),                 // push ax
            (0x0006, [0x1F]),                 // pop ds
            (0x0007, [0x26, 0xA1, 0x30, 0x00]), // mov ax, es:[0x0030]
            (0x000B, [0xC3]));                // ret

        // Assert
        program.SourceText.Should().Contain("AX = EsGlobals.Word0030;");
        AssertCompiles(program);
    }

    [Fact]
    public void ValueInsideTheImagePrintsAnImageRelativeLocationClass() {
        // Arrange: entry 0x170, a DOS image [0x160, 0x190), DS proven 0x180 = entry + 0x0010.
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0xB8, 0x80, 0x01]),     // mov ax, 0x0180
            (0x0003, [0x50]),               // push ax
            (0x0004, [0x1F]),               // pop ds
            (0x0005, [0xA1, 0x30, 0x00]),   // mov ax, [0x0030]
            (0x0008, [0xC3]));              // ret
        CfgPartitionedProgram partitioned = new() {
            Partitions = [PartitionOf(block)],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramImages = ProgramImages.Create(0x170,
                    [new LoadedImage(LoadedImageKind.DosProgram, "program.com", 0x160, 0x1700, 0x200)])
            }
        };

        // Act
        GeneratedCSharpProgram program = new CfgCSharpGenerator(collectCensuses: false, verify: false)
            .Generate(partitioned);

        // Assert
        program.SourceText.Should().Contain("AX = Image0010Globals.Word0030;");
        string location = program.Files.Single(file => file.Name == "Image0010Globals.cs").Text;
        location.Should().Contain("Segment = (ushort)(programStartSegment + 0x0010);");
        location.Should().Contain("base(memory, (uint)((programStartSegment + 0x0010) * 16))");
        AssertCompiles(program);
    }

    [Fact]
    public void ImmediateSetValueInsideTheImagePrintsAnAbsoluteLocationClass() {
        // Arrange: entry 0x170, a DOS image [0x160, 0x190), DS set from the immediate 0x180
        // (inside the image): the generated DS = 0x0180 is a literal, so the class must be
        // absolute to agree with it at any load segment.
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0xB8, 0x80, 0x01]),     // mov ax, 0x0180
            (0x0003, [0x8E, 0xD8]),           // mov ds, ax
            (0x0005, [0xA1, 0x30, 0x00]),     // mov ax, [0x0030]
            (0x0008, [0xC3]));                // ret
        CfgPartitionedProgram partitioned = new() {
            Partitions = [PartitionOf(block)],
            Transfers = [],
            Inputs = GeneratorInputs.Empty with {
                ProgramImages = ProgramImages.Create(0x170,
                    [new LoadedImage(LoadedImageKind.DosProgram, "program.com", 0x160, 0x1700, 0x200)])
            }
        };

        // Act
        GeneratedCSharpProgram program = new CfgCSharpGenerator(collectCensuses: false, verify: false)
            .Generate(partitioned);

        // Assert
        program.SourceText.Should().Contain("AX = Segment0180Globals.Word0030;");
        program.Files.Select(file => file.Name).Should().NotContain("Image0010Globals.cs");
        AssertCompiles(program);
    }

    [Fact]
    public void LocationPropertyExportsAGhidraLabelAtItsAddress() {
        // Arrange: the one location access of the program.
        GeneratedCSharpProgram program = Generate(
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x50]),             // push ax
            (0x0004, [0x1F]),             // pop ds
            (0x0005, [0xA1, 0x30, 0x00]), // mov ax, [0x0030]
            (0x0008, [0xC3]));            // ret

        // Assert: one label at 2000:0030.
        program.DataSymbolLabels.Should().Contain(label =>
            label.Name == "Word0030" && label.Address.Equals(new SegmentedAddress(0x2000, 0x0030)));
    }

    [Fact]
    public void SharedPointerInstanceUnderDifferentProofs_DemotesToTheOwnRegisterClass() {
        // Arrange: two word reads under DS=0x2000 then DS=0x2001; the second statement's pointer
        // node is replaced by the first statement's instance, as a shared subtree after a pass.
        IrMethod method = Lift(Linear(new TestInstructionHelper(),
            (0x0000, [0xB8, 0x00, 0x20]),   // mov ax, 0x2000
            (0x0003, [0x8E, 0xD8]),       // mov ds, ax
            (0x0005, [0xA1, 0x20, 0x00]), // mov ax, [0x0020]
            (0x0008, [0xB8, 0x01, 0x20]), // mov ax, 0x2001
            (0x000B, [0x8E, 0xD8]),       // mov ds, ax
            (0x000D, [0xA1, 0x20, 0x00]), // mov ax, [0x0020]
            (0x0010, [0xC3])));           // ret
        IrSsa ssa = IrPassPipeline.Run(method, IrPass.Constants, verify: true).Ssa
            ?? throw new InvalidOperationException("The pipeline built no SSA.");
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(ssa));
        List<(IrBlock Block, int Index, SegmentedPointerNode Pointer)> reads = method.Blocks
            .SelectMany(block => block.Statements.SelectMany((statement, index) =>
                IrAstChildren.Descendants(statement.Node).OfType<SegmentedPointerNode>()
                    .Take(1).Select(pointer => (block, index, pointer))))
            .ToList();
        reads.Count.Should().Be(2);
        SegmentedPointerNode shared = reads[0].Pointer;
        IrStatement second = reads[1].Block.Statements[reads[1].Index];
        IVisitableAstNode Rewrite(IVisitableAstNode node) =>
            ReferenceEquals(node, reads[1].Pointer) ? shared : IrAstChildren.MapChildren(node, Rewrite);
        reads[1].Block.Statements[reads[1].Index] = IrStatement.Create(Rewrite(second.Node), second.Origin);
        IrSegmentValues values = new(method, ssa, slots, IrSegmentFacts.None, new HashSet<int>());

        // Act
        GlobalAccessClassification classification = GlobalAccessClassification.Build(
            [(method, values, ssa)], GeneratorInputs.Empty.ProgramImages);

        // Assert: the conflicting second proof demotes the shared node to its own register's class.
        classification.ConflictDemotions.Should().Be(1);
        bool found = classification.TryTarget(shared, out GlobalAccessClassification.Target? target);
        found.Should().BeTrue();
        GlobalAccessClassification.Target resolved = target
            ?? throw new InvalidOperationException("The shared node kept no target.");
        resolved.Class.Name.Should().Be("DsGlobals");
        resolved.Class.Properties.Should().ContainKey((0x0020, 2));
    }

    [Fact]
    public void CallResultTargetDoesNotResolveToTheSelectorConstant() {
        // Arrange: an AX-selected service (selector 0) whose result target is AX itself.
        // Checking the selector before the result overlap would resolve the value AX holds
        // after the call to the selector constant 0.
        IrMethod method = Lift(Linear(new TestInstructionHelper(),
            (0x0000, [0xCD, 0x33]),       // int 0x33
            (0x0002, [0x8E, 0xD8]),       // mov ds, ax
            (0x0004, [0xA1, 0x20, 0x00]), // mov ax, [0x0020]
            (0x0007, [0xC3])));           // ret
        InterruptServiceEntry entry = new(0x00, "ResetMouseDriver",
            new ServiceContract(
                new CallEffect(CpuValues.Ax | CpuValues.Ss | CpuValues.Sp, CpuValues.Ax),
                ServiceMemoryEffect.None, false),
            ServiceStringArgument.None,
            typeof(BiosEquipmentDeterminationInt11Handler)
                .GetMethod(nameof(BiosEquipmentDeterminationInt11Handler.GetEquipmentList))
                ?? throw new InvalidOperationException("no GetEquipmentList"),
            []);
        IrBlock block = method.Blocks.Single(b =>
            b.Statements.Any(statement => statement.Node is InterruptCallHelperNode));
        int callIndex = block.Statements.ToList()
            .FindIndex(statement => statement.Node is InterruptCallHelperNode);
        callIndex.Should().BeGreaterThanOrEqualTo(0);
        InterruptServiceCallNode callNode = new(0x33, ServiceSelector.Ax, entry, "Mouse") {
            Results = [new RegisterNode(DataType.UINT16, 0)]
        };
        block.Statements[callIndex] = IrStatement.Create(callNode,
            block.Statements[callIndex].Origin);
        IrSsa ssa = IrSsa.Build(method);
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(ssa));
        IrSegmentValues values = new(method, ssa, slots, IrSegmentFacts.None, new HashSet<int>());

        // Act: DS after `mov ds, ax` follows the call-defined AX version.
        IrStatement read = method.Blocks.SelectMany(b => b.Statements).First(statement =>
            IrAstChildren.Descendants(statement.Node).OfType<SegmentedPointerNode>().Any());
        SegmentValue resolved = values.ValueAt(read, IrSsaValue.Ds);

        // Assert: the result version is Unknown, not the selector constant.
        resolved.Should().BeOfType<SegmentValue.Unknown>(
            "the AX the call returns is not the selector 0 the helper stored");
    }
}
