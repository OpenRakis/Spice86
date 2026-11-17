namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using System.Buffers.Binary;
using System.Linq;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// A direct service call is emitted only when the whole program lets it: every <c>int</c> site on
/// the vector observed only the installed stub, the recorded vector still points at it, the
/// selector value at the site is a known constant whose entry binds an implementation, and no
/// <c>int</c> site anywhere has a vector the analysis cannot evaluate. A constant vector different
/// from the service's is unrelated, declared or not. Any other shape keeps the generic
/// <c>InterruptCall</c>: a program handler, a hook, an uninstalled vector, a missing target or an
/// unknown selector can all make the direct call wrong, so one bad site on the vector disqualifies
/// every site of it. These tests lift real two-site CFGs and run the pipeline through
/// <see cref="IrPass.InterruptServiceCalls"/> with the production
/// <see cref="HardwareInterruptEffectBuilder.HasOnlyBuiltInTargets"/> predicate, then assert the
/// lowered call nodes themselves.
/// </summary>
public sealed class IrInterruptServiceCallsTest {
    private static readonly SegmentedAddress DosStub = new(0xF000, 0x9000);

    private static IReadOnlyDictionary<int, InterruptServiceEntry> DosContracts() =>
        InterruptServiceAttributes.Of(typeof(DosInt21Handler), ServiceSelector.Ah)
            .Services.ToDictionary(pair => pair.Entry.SelectorValue, pair => pair.Entry);

    private static InterruptServiceVector DosVector() => new(0x21, DosStub,
        new HandlerServices.SelectedServices(ServiceSelector.Ah, DosContracts()),
        typeof(DosInt21Handler));

    private static byte[] MemoryWithVectors(params (byte Vector, SegmentedAddress Target)[] entries) {
        byte[] memory = new byte[0x40000];
        foreach ((byte vector, SegmentedAddress target) in entries) {
            BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(vector * 4), target.Offset);
            BinaryPrimitives.WriteUInt16LittleEndian(memory.AsSpan(vector * 4 + 2), target.Segment);
        }
        return memory;
    }

    private static byte[] DosMemory() => MemoryWithVectors((0x21, DosStub));

    /// <summary>Two <c>int 21h</c> sites with AH=30h and distinct continuations.</summary>
    private static (ushort, byte[])[] DosVersionProgram => [
        (0x0000, [0xB4, 0x30]),       // mov ah, 0x30
        (0x0002, [0xCD, 0x21]),       // int 0x21
        (0x0004, [0xB4, 0x30]),       // mov ah, 0x30
        (0x0006, [0xCD, 0x21]),       // int 0x21
        (0x0008, [0xC3])              // ret
    ];

    /// <summary>A small program interrupt handler partition: it reads SI into BX, writes DI and
    /// returns. Its first instruction is the target custom sites observe.</summary>
    private static (CfgCodePartition Partition, CfgInstruction[] Instructions) CustomHandlerPartition(
        TestInstructionHelper helper) {
        CfgInstruction[] parsed = [
            Parse(helper, 0x2000, 0x89, 0xF3),        // mov bx, si
            Parse(helper, 0x2002, 0xBF, 0xEF, 0xBE),  // mov di, 0xBEEF
            Parse(helper, 0x2005, 0xCF)               // iret
        ];
        Chain(parsed);
        return (Partition(2, Block(90, parsed)), parsed);
    }

    /// <summary>A custom handler whose tail far-jumps to the installed stub instead of
    /// returning: the same shape a chaining hook leaves in the CFG.</summary>
    private static (CfgCodePartition Partition, CfgInstruction[] Instructions) ChainedHandlerPartition(
        TestInstructionHelper helper) {
        CfgInstruction[] parsed = [
            Parse(helper, 0x2000, 0x89, 0xF3),                    // mov bx, si
            Parse(helper, 0x2002, 0xEA, 0x00, 0x90, 0x00, 0xF0)   // jmp far F000:9000 (the stub)
        ];
        Chain(parsed);
        return (Partition(2, Block(90, parsed)), parsed);
    }

    private static (CfgPartitionedProgram Program, CfgInstruction[] Parsed, CfgInstruction Stub)
        BuildProgram(TestInstructionHelper helper,
            (ushort Offset, byte[] Bytes)[] instructions,
            Func<CfgInstruction, CfgInstruction[], IEnumerable<ICfgNode>>[] siteTargets,
            byte[] memory,
            Func<CfgCodePartition, CfgInstruction, CfgCodePartitionTransfer[]> transfersOf,
            params CfgCodePartition[] extraPartitions) {
        CfgInstruction[] parsed = instructions
            .Select(instruction => Parse(helper, instruction.Offset, instruction.Bytes))
            .ToArray();
        Chain(parsed);
        CfgInstruction stub = Parse(helper, DosStub.Offset, [0xCF]);
        int site = 0;
        List<int> splitAfter = [];
        for (int i = 0; i < parsed.Length; i++) {
            if (instructions[i].Bytes[0] != 0xCD) {
                continue;
            }
            parsed[i].SuccessorsPerType[InstructionSuccessorType.Normal] =
                new HashSet<ICfgNode>(siteTargets[site++](stub, parsed));
            if (i + 1 < parsed.Length) {
                parsed[i].SuccessorsPerType[InstructionSuccessorType.CallToReturn] =
                    new HashSet<ICfgNode> { parsed[i + 1] };
                splitAfter.Add(i + 1);
            }
        }
        List<CfgBlock> blocks = [];
        int start = 0;
        int id = 1;
        foreach (int cut in splitAfter) {
            blocks.Add(Block(id++, [.. parsed[start..cut]]));
            start = cut;
        }
        if (start < parsed.Length) {
            blocks.Add(Block(id++, [.. parsed[start..]]));
        }
        blocks.Add(Block(id, [stub]));
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "test",
            Blocks = [.. blocks],
            Entries = SingleEntry(blocks[0])
        };
        CfgPartitionedProgram program = new() {
            Partitions = [partition, .. extraPartitions],
            Transfers = transfersOf(partition, stub),
            Inputs = GeneratorInputs.Empty with {
                HardwareInterrupts = GeneratorInputs.Empty.HardwareInterrupts with {
                    InstalledInterruptHandlerAddresses = [DosStub],
                    ServiceVectors = new Dictionary<byte, InterruptServiceVector> {
                        [0x21] = DosVector()
                    }
                },
                ProgramEntryPoint = partition.Entries[0].Address,
                MemorySnapshot = memory
            }
        };
        return (program, parsed, stub);
    }

    /// <summary>Lifts the program and runs the IR pipeline through
    /// <see cref="IrPass.InterruptServiceCalls"/> with the production whole-vector predicate, so the
    /// statements hold the final generic or direct call nodes.</summary>
    private static IReadOnlyList<IrMethod> ServiceCallMethods(CfgPartitionedProgram program) {
        AstInstructionRenderer renderer = new(AsmRenderingConfig.CreateSpice86Style());
        GeneratorAnalysis analysis = GeneratorAnalysis.Build(program,
            new InstructionMnemonic(renderer));
        CfgGeneratorContext context = analysis.Context;
        GenerationPlan plan = GenerationPlanBuilder.Build(context);
        TransferEmitter transfer = new(context);
        HardwareInterruptEffectBuilder effects = new(context, transfer, renderer, plan);
        InstructionLowerer lowerer = new(context, transfer, effects, effects.Compute());
        IrLifter lifter = new(context, lowerer, renderer);
        List<IrMethod> methods = new(plan.Methods.Count);
        foreach (IrMethod method in plan.Methods.Select(lifter.Lift)) {
            IrPassPipeline.Run(method, IrPass.InterruptServiceCalls, verify: true, buildWebs: false,
                program.Inputs.IoPorts, effects.HasOnlyBuiltInTargets, new GenerationStepTimer());
            methods.Add(method);
        }
        return methods;
    }

    private static List<IVisitableAstNode> CallNodes(IReadOnlyList<IrMethod> methods) =>
        methods.SelectMany(method => method.Blocks)
            .SelectMany(block => block.Statements)
            .Select(statement => statement.Node)
            .Where(node => node is InterruptServiceCallNode or InterruptCallHelperNode
                or ProgramInterruptCallHelperNode)
            .ToList();

    private static void AssertNoDirectCalls(IReadOnlyList<IrMethod> methods) =>
        CallNodes(methods).OfType<InterruptServiceCallNode>().Should().BeEmpty(
            "a disqualifying site keeps every site of the vector generic");

    /// <summary>Both sites observed only the installed stub and the recorded vector still
    /// points at it: the calls become direct invocations of the bound
    /// <see cref="DosInt21Handler.ReadDosVersion"/>.</summary>
    [Fact]
    public void BuiltInStubSitesCallTheImplementationDirectly() {
        // Arrange / Act
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (stub, _) => [stub]], DosMemory(), (_, _) => []);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        List<InterruptServiceCallNode> calls = [.. CallNodes(methods).OfType<InterruptServiceCallNode>()];
        calls.Should().HaveCount(2, "both stub-only sites bind the implementation");
        calls.Should().OnlyContain(call => call.Service.Implementation != null
            && call.Service.Implementation.Name == nameof(DosInt21Handler.ReadDosVersion));

        // The same graph still generates compiling source.
        GeneratedCSharpProgram generated =
            new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(program);
        generated.SourceText.Should().Contain("Dos.ReadDosVersion()");
        AssertCompiles(generated);
    }

    /// <summary>A second site on the same vector targeting a program handler makes the whole
    /// vector untyped: the first, all-stub site cannot go direct either. The custom call keeps the
    /// lowered program-handler invocation, whose effect is the handler partition's.</summary>
    [Fact]
    public void ACustomTargetAnywhereOnTheVectorKeepsEverySiteGeneric() {
        // Arrange
        TestInstructionHelper helper = new();
        (CfgCodePartition handler, CfgInstruction[] handlerCode) = CustomHandlerPartition(helper);

        // Act
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (_, _) => [handlerCode[0]]], DosMemory(), (_, _) => [], handler);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
        CallNodes(methods).OfType<InterruptCallHelperNode>().Should()
            .OnlyContain(node => node.Kind == InterruptSiteKind.Hooked,
                "the stub-only site stays a generic hooked call");
        ProgramInterruptCallHelperNode custom = CallNodes(methods)
            .OfType<ProgramInterruptCallHelperNode>().Should().ContainSingle(
                "the custom target keeps a program-handler call").Subject;
        (custom.Handler.Reads & CpuValues.Si).Should().Be(CpuValues.Si,
            "the handler's mov bx, si reads SI at its entry");
        (custom.Handler.Writes & CpuValues.Di).Should().Be(CpuValues.Di,
            "the handler's mov di, 0xBEEF writes DI before its iret");
    }

    /// <summary>A handler that far-jumps to the installed stub instead of returning is still a
    /// custom target of its site: observing the stub in the chain does not clean the vector.</summary>
    [Fact]
    public void AChainedHandlerStillKeepsEverySiteGeneric() {
        // Arrange
        TestInstructionHelper helper = new();
        (CfgCodePartition handler, CfgInstruction[] handlerCode) = ChainedHandlerPartition(helper);

        // Act
        (CfgPartitionedProgram program, _, CfgInstruction stub) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (_, _) => [handlerCode[0]]], DosMemory(),
            (mainPartition, stubTarget) => [
                new CfgCodePartitionTransfer {
                    FromPartition = handler,
                    ToPartition = mainPartition,
                    FromNode = handlerCode[^1],
                    TargetNode = stubTarget,
                    Kind = CfgCodePartitionTransferKind.CrossPartitionFlow
                }
            ], handler);
        handlerCode[^1].SuccessorsPerType[InstructionSuccessorType.Normal] =
            new HashSet<ICfgNode> { stub };
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
    }

    /// <summary>A site of the vector without any observed target disqualifies it: discovery
    /// never saw the handler the call would deliver.</summary>
    [Fact]
    public void ASiteWithoutObservedTargetKeepsEverySiteGeneric() {
        // Arrange / Act
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (_, _) => []], DosMemory(), (_, _) => []);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
    }

    /// <summary>A speculative <c>int</c> site - a failure path discovery never executed, marked by
    /// <c>SetSpeculative</c> - is not a recorded site: it has no observed target to qualify or
    /// disqualify anything, so the recorded stub-only site still goes direct.</summary>
    [Fact]
    public void ASpeculativeSiteWithoutTargetDoesNotDisqualifyTheVector() {
        // Arrange / Act
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, CfgInstruction[] parsed, _) = BuildProgram(helper,
            DosVersionProgram, [(stub, _) => [stub], (_, _) => []], DosMemory(), (_, _) => []);
        parsed[3].SetSpeculative(true);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        CallNodes(methods).OfType<InterruptServiceCallNode>().Should().ContainSingle(
            "only the recorded stub-only site binds the implementation")
            .Which.Service.Implementation?.Name.Should().Be(nameof(DosInt21Handler.ReadDosVersion));
        CallNodes(methods).OfType<InterruptCallHelperNode>().Should().ContainSingle(
            "the speculative site itself stays generic")
            .Which.Kind.Should().Be(InterruptSiteKind.NotObserved);
    }

    /// <summary>A site that observed both the stub and a custom target keeps the whole vector
    /// generic: the chaining handler differs from the built-in service.</summary>
    [Fact]
    public void ASiteTargetingTheStubAndACustomKeepsEverySiteGeneric() {
        // Arrange
        TestInstructionHelper helper = new();
        (CfgCodePartition handler, CfgInstruction[] handlerCode) = CustomHandlerPartition(helper);

        // Act
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (stub, _) => [stub, handlerCode[0]]], DosMemory(), (_, _) => [], handler);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
        CallNodes(methods).OfType<InterruptCallHelperNode>().Should()
            .OnlyContain(node => node.Kind == InterruptSiteKind.Hooked);
    }

    /// <summary>The recorded vector table entry pointing at a custom handler fails the check even
    /// when every site on the vector observed only the stub.</summary>
    [Fact]
    public void ARecordedCustomVectorKeepsEverySiteGeneric() {
        // Arrange / Act
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (stub, _) => [stub]],
            MemoryWithVectors((0x21, new SegmentedAddress(0x1234, 0x5678))), (_, _) => []);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
    }

    /// <summary>A constant vector that is not the service's is unrelated to the decision even with
    /// no service metadata at all: an <c>int 60h</c> to a program handler does not disqualify the
    /// DOS sites.</summary>
    [Fact]
    public void AnUnrelatedConstantVectorKeepsTheDirectCalls() {
        // Arrange
        TestInstructionHelper helper = new();
        (CfgCodePartition handler, CfgInstruction[] handlerCode) = CustomHandlerPartition(helper);
        (ushort, byte[])[] instructions = [
            .. DosVersionProgram[..^1],
            (0x0008, [0xCD, 0x60]),     // int 0x60
            (0x000A, [0xC3])            // ret
        ];

        // Act
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, instructions,
            [(stub, _) => [stub], (stub, _) => [stub], (_, _) => [handlerCode[0]]],
            DosMemory(), (_, _) => [], handler);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        CallNodes(methods).OfType<InterruptServiceCallNode>().Should().HaveCount(2,
            "an int on another vector is ignored, declared or not");
    }

    /// <summary>An <c>int</c> site whose vector the analysis cannot evaluate cannot be proven
    /// unrelated, so it disqualifies the typed calls of the whole program.</summary>
    [Fact]
    public void AnUnresolvableVectorKeepsEverySiteGeneric() {
        // Arrange
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, CfgInstruction[] parsed, _) = BuildProgram(helper, DosVersionProgram,
            [(stub, _) => [stub], (stub, _) => [stub]], DosMemory(), (_, _) => []);
        CfgInstruction indirect = parsed[3];
        indirect.AttachAsts(indirect.DisplayAst,
            new InterruptCallNode(indirect, new RegisterNode(DataType.UINT8, 0)));

        // Act
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
    }

    /// <summary>Without a known selector value the site keeps the generic call even when the
    /// vector itself is clean.</summary>
    [Fact]
    public void AnUnknownSelectorKeepsTheGenericCall() {
        // Arrange / Act
        (ushort, byte[])[] instructions = [
            (0x0000, [0xCD, 0x21]),     // int 0x21
            (0x0002, [0xC3])            // ret
        ];
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, instructions,
            [(stub, _) => [stub]], DosMemory(), (_, _) => []);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
        CallNodes(methods).OfType<InterruptCallHelperNode>().Should()
            .OnlyContain(node => node.Kind == InterruptSiteKind.SelectorUnknown);
    }

    /// <summary>A selector value whose entry has no implementation binding keeps the generic
    /// call, but with real dispatch and the precise contract's normal-return effects: the
    /// contract's reads stay live across the call and DS is not clobbered.</summary>
    [Fact]
    public void AServiceWithoutImplementationKeepsTheGenericCall() {
        // Arrange / Act
        (ushort, byte[])[] instructions = [
            (0x0000, [0xB4, 0x09]),     // mov ah, 0x09
            (0x0002, [0xCD, 0x21]),     // int 0x21
            (0x0004, [0xC3])            // ret
        ];
        TestInstructionHelper helper = new();
        (CfgPartitionedProgram program, _, _) = BuildProgram(helper, instructions,
            [(stub, _) => [stub]], DosMemory(), (_, _) => []);
        IReadOnlyList<IrMethod> methods = ServiceCallMethods(program);

        // Assert
        AssertNoDirectCalls(methods);
        InterruptCallHelperNode call = CallNodes(methods).OfType<InterruptCallHelperNode>()
            .Should().ContainSingle().Subject;
        call.Kind.Should().Be(InterruptSiteKind.BuiltInDispatch);
        IrEffects effects = IrEffectsVisitor.Of(call, false);
        effects.Reads.HasFlag(CpuValues.Ds).Should().BeTrue(
            "the print-string contract reads DS:DX, so DS stays live across the dispatch");
        (effects.MayWrite & CpuValues.Ds).Should().Be(CpuValues.None,
            "the contract writes nothing, so a known DS survives the dispatch");
    }

    /// <summary>A built-in dispatch followed by a bound service: the unbound print-string call
    /// keeps the contract's writes (DS survives) while the write-file call is direct. A custom
    /// target on the vector drops both to the unknown-effects generic call instead.</summary>
    [Fact]
    public void BuiltInDispatchKeepsDsKnownOnlyWithoutACustomTarget() {
        // Arrange
        (ushort, byte[])[] instructions = [
            (0x0000, [0xB4, 0x09]),     // mov ah, 0x09
            (0x0002, [0xCD, 0x21]),     // int 0x21
            (0x0004, [0xB4, 0x40]),     // mov ah, 0x40
            (0x0006, [0xCD, 0x21]),     // int 0x21
            (0x0008, [0xC3])            // ret
        ];
        TestInstructionHelper helper = new();

        // Act: no custom target anywhere.
        (CfgPartitionedProgram clean, _, _) = BuildProgram(helper, instructions,
            [(stub, _) => [stub], (stub, _) => [stub]], DosMemory(), (_, _) => []);
        IReadOnlyList<IrMethod> cleanMethods = ServiceCallMethods(clean);

        // Assert
        InterruptCallHelperNode dispatch = CallNodes(cleanMethods)
            .OfType<InterruptCallHelperNode>().Should().ContainSingle(
                "the unbound print service keeps real dispatch").Subject;
        dispatch.Kind.Should().Be(InterruptSiteKind.BuiltInDispatch);
        (IrEffectsVisitor.Of(dispatch, false).MayWrite & CpuValues.Ds).Should().Be(CpuValues.None);
        CallNodes(cleanMethods).OfType<InterruptServiceCallNode>().Should().ContainSingle(
            "the write service binds its implementation");

        // Act: a custom target on the same vector.
        (CfgCodePartition handler, CfgInstruction[] handlerCode) = CustomHandlerPartition(helper);
        (CfgPartitionedProgram hooked, _, _) = BuildProgram(helper, instructions,
            [(stub, _) => [stub], (_, _) => [handlerCode[0]]], DosMemory(), (_, _) => [], handler);
        IReadOnlyList<IrMethod> hookedMethods = ServiceCallMethods(hooked);

        // Assert
        AssertNoDirectCalls(hookedMethods);
        InterruptCallHelperNode generic = CallNodes(hookedMethods)
            .OfType<InterruptCallHelperNode>().First(node => node.Kind == InterruptSiteKind.Hooked);
        IrEffectsVisitor.Of(generic, false).MayWrite.HasFlag(CpuValues.Ds).Should().BeTrue(
            "a hooked call can deliver any handler, so a known DS does not survive it");
    }

    /// <summary>A direct call must-writes only its result targets, reads only its bound input
    /// arguments (added by the statement effects, not by the contract effects) and may-write
    /// the contract's writes - no frame, no selector, no preserved outputs.</summary>
    [Fact]
    public void DirectEffectsWriteOnlyTheResultTargets() {
        // Arrange: int 17h WriteCharacter, declared Writes = Ah|Carry.
        InterruptServiceEntry entry = InterruptServiceAttributes
            .Of(typeof(SystemBiosInt17Handler), ServiceSelector.Ah)
            .Services.Single(pair => pair.Entry.SelectorValue == 0x00).Entry;
        InterruptServiceCallNode call = new(0x17, ServiceSelector.Ah, entry, "Printer",
            [(RegisterNode)IrValueSets.NodeOf(CpuValues.Al),
                (RegisterNode)IrValueSets.NodeOf(CpuValues.Dx)]) {
            Results = [new RegisterNode(DataType.UINT8, 4), new CpuFlagNode(Flags.Carry)]
        };

        // Act
        IrEffects effects = ServiceContractEffects.Of(call);

        // Assert
        effects.MustWrite.Should().Be(CpuValues.Ah | CpuValues.Carry,
            "only the result targets are must-written");
        effects.Reads.Should().Be(CpuValues.None,
            "the bound inputs arrive as arguments; the contract effects read nothing else");
        effects.MayWrite.Should().Be(CpuValues.Ah | CpuValues.Carry,
            "may-write is the contract's writes");
        effects.TokenReads.Should().Be(IrTokens.Memory,
            "a direct call still touches memory conservatively");
        effects.TokenWrites.Should().Be(IrTokens.Memory);
        effects.MayFault.Should().BeFalse();
        effects.DeliversInterrupts.Should().BeFalse();
    }
}
