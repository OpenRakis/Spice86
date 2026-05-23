namespace Spice86.Tests.UI.CfgCpu;

using AvaloniaGraphControl;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.StateSerialization.ControlFlow;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Enums;
using Spice86.ViewModels.Services;

using Xunit;
using Spice86.ViewModels.DataModels;

/// <summary>
/// UI tests for <see cref="CfgCpuViewModel"/> validating block-centric rendering, indicators,
/// search, edge styling, and table behaviour.
/// </summary>
/// <remarks>
/// <para>
/// These tests build synthetic <see cref="CfgBlock"/> graphs through the production
/// <see cref="NodeLinker"/> using parsed <see cref="CfgInstruction"/>s. The view model is
/// exercised at the model layer (no AXAML rendering): its behaviour is observed through the
/// <see cref="CfgCpuViewModel.Graph"/>, <see cref="CfgCpuViewModel.NumberOfNodes"/>,
/// <see cref="CfgCpuViewModel.NodeEntries"/> and <see cref="CfgCpuViewModel.TableNodes"/>
/// observables. The dashed-outline + ellipsis styling for in-discovery blocks is
/// rendered by the AXAML view; here we assert the model-level contract surfaced on
/// <see cref="CfgGraphNode.IsDiscoveryComplete"/> and the trailing "…" token in the node
/// listing.
/// </para>
/// <para>
/// Each test builds its own minimal CFG, wires a real <see cref="ExecutionContextManager"/>
/// via the same construction pattern as the existing CfgNodeFeederTest, and constructs a
/// fresh <see cref="CfgCpuViewModel"/> that uses the synthesised CFG as its data source.
/// The view model is then driven through its public
/// <see cref="CfgCpuViewModel.UpdateGraphCommand"/> so the same code path the UI uses is
/// exercised end-to-end.
/// </para>
/// </remarks>
public class CfgCpuViewModelTest : IDisposable {
    private static readonly AsmRenderingConfig AsmConfig = AsmRenderingConfig.CreateSpice86Style();

    /// <summary>
    /// Test harness wiring a minimal real CFG infrastructure: <see cref="Memory"/>,
    /// <see cref="State"/>, <see cref="EmulatorBreakpointsManager"/>, <see cref="NodeLinker"/>,
    /// and a real <see cref="ExecutionContextManager"/> exposing a writable
    /// <see cref="Spice86.Core.Emulator.CPU.CfgCpu.Linker.ExecutionContext"/>.
    /// </summary>
    private sealed class Harness : IDisposable {
        private readonly CfgNodeExecutionCompiler _compiler;
        private readonly CfgNodeExecutionCompilerMonitor _monitor;
        private readonly PauseHandler _pauseHandler;
        private readonly ILoggerService _loggerService;

        public Harness() {
            _loggerService = Substitute.For<ILoggerService>();
            AddressReadWriteBreakpoints memoryBreakpoints = new();
            AddressReadWriteBreakpoints ioBreakpoints = new();
            Memory = new Memory(memoryBreakpoints, new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
            State = new State(CpuModel.INTEL_80286);
            _pauseHandler = new PauseHandler(_loggerService);
            EmulatorBreakpointsManager breakpointsManager = new(_pauseHandler, State, Memory, memoryBreakpoints, ioBreakpoints);
            InstructionReplacerRegistry replacerRegistry = new();
            _monitor = new CfgNodeExecutionCompilerMonitor(_loggerService);
            _compiler = new CfgNodeExecutionCompiler(_monitor, _loggerService, JitMode.InterpretedOnly);
            CfgNodeFeeder cfgNodeFeeder = new(Memory, State, breakpointsManager, replacerRegistry, _compiler);
            Linker = new NodeLinker(replacerRegistry, _compiler, new CfgNodeIdAllocator());
            ContextManager = new ExecutionContextManager(
                Memory, State, cfgNodeFeeder, replacerRegistry,
                new Spice86.Core.Emulator.Function.FunctionCatalogue(),
                useCodeOverride: false,
                loggerService: _loggerService,
                cpuHeavyLogger: null);
            // Reuse the InstructionParser pattern from TestInstructionHelper but bind it to
            // OUR memory/state so parsed instructions land at the addresses we wire through
            // the linker.
            InstructionHelper = new TestInstructionHelperBoundTo(Memory, State);
        }

        public Memory Memory { get; }

        public State State { get; }

        public NodeLinker Linker { get; }

        public ExecutionContextManager ContextManager { get; }

        public TestInstructionHelperBoundTo InstructionHelper { get; }

        public ExecutionContext Context => ContextManager.CurrentExecutionContext;

        public ICfgNode Link(ICfgNode from, ICfgNode to,
            InstructionSuccessorType linkType = InstructionSuccessorType.Normal) =>
            Linker.Link(linkType, from, to);

        public void Dispose() {
            _compiler.Dispose();
            _monitor.Dispose();
            _pauseHandler.Dispose();
        }
    }

    /// <summary>
    /// Minimal helper bound to a specific <see cref="Memory"/> + <see cref="State"/> pair (so
    /// the parsed <see cref="CfgInstruction"/>s are wired against the same memory the
    /// harness uses). Mirrors the production parser plumbing without depending on a fresh
    /// memory/state pair.
    /// </summary>
    private sealed class TestInstructionHelperBoundTo {
        private readonly Memory _memory;
        private readonly Spice86.Core.Emulator.CPU.CfgCpu.Parser.InstructionParser _parser;

        public TestInstructionHelperBoundTo(Memory memory, State state) {
            _memory = memory;
            _parser = new Spice86.Core.Emulator.CPU.CfgCpu.Parser.InstructionParser(memory, state, new CfgNodeIdAllocator());
        }

        public CfgInstruction WriteAndParse(SegmentedAddress address,
            Action<Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter.MemoryAsmWriter> write) {
            Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter.MemoryAsmWriter writer =
                new(_memory, address);
            write(writer);
            return _parser.ParseInstructionAt(address);
        }
    }

    private readonly Harness _harness;

    public CfgCpuViewModelTest() {
        _harness = new Harness();
    }

    public void Dispose() {
        _harness.Dispose();
    }

    /// <summary>
    /// Creates a fresh <see cref="CfgCpuViewModel"/> wired against the harness's real
    /// <see cref="ExecutionContextManager"/>, with a real <see cref="NodeToString"/> and a
    /// synchronous <see cref="IUIDispatcher"/> stand-in so command continuations execute
    /// inline.
    /// </summary>
    private CfgCpuViewModel CreateViewModel() {
        IUIDispatcher uiDispatcher = new InlineUIDispatcher();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        NodeToString nodeToString = new(AsmConfig);
        CfgBlockGraphExporter graphExporter = new();
        return new CfgCpuViewModel(uiDispatcher, _harness.ContextManager, pauseHandler,
            nodeToString, AsmConfig, graphExporter);
    }

    /// <summary>
    /// Builds a tiny three-block linear chain. Returns the entry instructions of each block
    /// in order along with the explicit terminators so tests can assert on per-block layout.
    /// </summary>
    /// <remarks>
    /// Layout:
    /// <code>
    /// blk0:  mov ax, 0x1234   ; @ 0x1000:0x0000
    ///        mov bx, 0x5678   ; @ 0x1000:0x0003
    ///        jmp short blk1   ; @ 0x1000:0x0006 (block terminator)
    /// blk1:  mov cx, 0xCAFE   ; @ 0x1000:0x0008 (block entry)
    ///        ret              ; @ 0x1000:0x000B (block terminator)
    /// blk2:  mov dx, 0xBEEF   ; @ 0x1000:0x000C (block entry)
    ///        nop              ; @ 0x1000:0x000F
    ///        retf             ; @ 0x1000:0x0010 (block terminator)
    /// </code>
    /// </remarks>
    private (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov,
        CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf)
        BuildLinearThreeBlockChain() {
        SegmentedAddress addrBlk0Mov1 = new(0x1000, 0x0000);
        // mov ax, 0x1234 -> B8 34 12 (3 bytes)
        CfgInstruction blk0Mov1 = _harness.InstructionHelper.WriteAndParse(addrBlk0Mov1, w => {
            w.WriteUInt8(0xB8);
            w.WriteUInt8(0x34);
            w.WriteUInt8(0x12);
        });

        SegmentedAddress addrBlk0Mov2 = new(0x1000, 0x0003);
        // mov bx, 0x5678 -> BB 78 56 (3 bytes)
        CfgInstruction blk0Mov2 = _harness.InstructionHelper.WriteAndParse(addrBlk0Mov2, w => {
            w.WriteUInt8(0xBB);
            w.WriteUInt8(0x78);
            w.WriteUInt8(0x56);
        });

        SegmentedAddress addrBlk0Jmp = new(0x1000, 0x0006);
        // jmp short +0 -> EB 00 (2 bytes), targets 0x0008
        CfgInstruction blk0Jmp = _harness.InstructionHelper.WriteAndParse(addrBlk0Jmp, w => {
            w.WriteUInt8(0xEB);
            w.WriteUInt8(0x00);
        });

        SegmentedAddress addrBlk1Mov = new(0x1000, 0x0008);
        // mov cx, 0xCAFE -> B9 FE CA (3 bytes)
        CfgInstruction blk1Mov = _harness.InstructionHelper.WriteAndParse(addrBlk1Mov, w => {
            w.WriteUInt8(0xB9);
            w.WriteUInt8(0xFE);
            w.WriteUInt8(0xCA);
        });

        SegmentedAddress addrBlk1Ret = new(0x1000, 0x000B);
        // ret near -> C3 (1 byte)
        CfgInstruction blk1Ret = _harness.InstructionHelper.WriteAndParse(addrBlk1Ret,
            w => w.WriteUInt8(0xC3));

        SegmentedAddress addrBlk2Mov = new(0x1000, 0x000C);
        // mov dx, 0xBEEF -> BA EF BE (3 bytes)
        CfgInstruction blk2Mov = _harness.InstructionHelper.WriteAndParse(addrBlk2Mov, w => {
            w.WriteUInt8(0xBA);
            w.WriteUInt8(0xEF);
            w.WriteUInt8(0xBE);
        });

        SegmentedAddress addrBlk2Nop = new(0x1000, 0x000F);
        CfgInstruction blk2Nop = _harness.InstructionHelper.WriteAndParse(addrBlk2Nop,
            w => w.WriteUInt8(0x90));

        SegmentedAddress addrBlk2Retf = new(0x1000, 0x0010);
        // retf -> CB (1 byte)
        CfgInstruction blk2Retf = _harness.InstructionHelper.WriteAndParse(addrBlk2Retf,
            w => w.WriteUInt8(0xCB));

        // Wire edges through the linker: this builds CfgBlocks with proper back-pointers and
        // proper IsDiscoveryComplete transitions on each boundary.
        _harness.Link(blk0Mov1, blk0Mov2);
        _harness.Link(blk0Mov2, blk0Jmp);
        _harness.Link(blk0Jmp, blk1Mov);
        _harness.Link(blk1Mov, blk1Ret);
        _harness.Link(blk1Ret, blk2Mov);
        _harness.Link(blk2Mov, blk2Nop);
        _harness.Link(blk2Nop, blk2Retf);

        return (blk0Mov1, blk0Jmp, blk1Mov, blk1Ret, blk2Mov, blk2Retf);
    }

    private static Task UpdateGraphAndWait(CfgCpuViewModel viewModel) =>
        viewModel.UpdateGraphCommand.ExecuteAsync(null);

    private static Task SearchAndWait(CfgCpuViewModel viewModel) =>
        viewModel.SearchNodeCommand.ExecuteAsync(null);

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// One graph node is rendered per <see cref="CfgBlock"/>, and each block's
    /// rendered listing contains its contained instructions in order.
    /// </summary>
    [Fact]
    public async Task Renders_OneNodePerCfgBlock_WithListing() {
        // Arrange
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        _harness.ContextManager.ExecutingNode = nodes.blk0Mov;
        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert — view model produces exactly one CfgGraphNode per CfgBlock visited.
        viewModel.Graph.Should().NotBeNull("UpdateGraph must produce a non-null Graph");
        viewModel.NumberOfNodes.Should().BeGreaterThan(0,
            "the view model must visit and render at least one block");

        // The CfgBlocks reachable from the seed: blk0, blk1, blk2 (three blocks).
        viewModel.NumberOfNodes.Should().Be(3,
            "exactly one CfgGraphNode is rendered per CfgBlock; the three-block chain " +
            "should produce three graph nodes");

        // Each block's listing concatenates its instructions in order. We verify the listing
        // text of the entry block (which is what the user sees first in the graph view).
        CfgBlock blk0Block = nodes.blk0Mov.ContainingBlock ?? throw new InvalidOperationException("blk0Mov.ContainingBlock should not be null");
        CfgGraphNode entryBlockGraphNode = FindGraphNodeForBlock(viewModel, blk0Block);
        string text = entryBlockGraphNode.ToString();
        text.Should().Contain("mov", "the listing must include the rendered MOV instructions");
        text.Should().Contain("jmp", "the listing must include the rendered JMP terminator");
        // Order: the first MOV is rendered before the second MOV which is rendered before JMP.
        int idxMov1 = text.IndexOf("0x1234", StringComparison.OrdinalIgnoreCase);
        int idxMov2 = text.IndexOf("0x5678", StringComparison.OrdinalIgnoreCase);
        int idxJmp = text.IndexOf("jmp", StringComparison.OrdinalIgnoreCase);
        idxMov1.Should().BeGreaterThanOrEqualTo(0);
        idxMov2.Should().BeGreaterThan(idxMov1,
            "the listing must preserve instruction order — first MOV (0x1234) appears before second MOV (0x5678)");
        idxJmp.Should().BeGreaterThan(idxMov2,
            "the listing must preserve instruction order — second MOV appears before the JMP terminator");
    }

    /// <summary>
    /// Block-to-block edges are styled by deriving the <see cref="CfgEdgeType"/> from
    /// the source block's terminator's <see cref="CfgInstruction.Kind"/>.
    /// </summary>
    [Fact]
    public async Task Renders_BlockEdgesWithStyling() {
        // Arrange
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        _harness.ContextManager.ExecutingNode = nodes.blk0Mov;
        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert
        viewModel.Graph.Should().NotBeNull();
        CfgBlock blk0 = nodes.blk0Mov.ContainingBlock ?? throw new InvalidOperationException("blk0Mov.ContainingBlock should not be null");
        CfgBlock blk1 = nodes.blk1Mov.ContainingBlock ?? throw new InvalidOperationException("blk1Mov.ContainingBlock should not be null");
        CfgBlock blk2 = nodes.blk2Mov.ContainingBlock ?? throw new InvalidOperationException("blk2Mov.ContainingBlock should not be null");

        // The blk0 → blk1 edge derives from blk0's terminator (a JMP) → CfgEdgeType.Jump.
        CfgGraphEdgeLabel jmpEdge = FindEdgeLabelBetween(viewModel, blk0.Id, blk1.Id);
        jmpEdge.EdgeType.Should().Be(CfgEdgeType.Jump,
            "an edge whose source block ends in a JMP must be styled CfgEdgeType.Jump");

        // The blk1 → blk2 edge derives from blk1's terminator (a near RET) → CfgEdgeType.Return.
        CfgGraphEdgeLabel retEdge = FindEdgeLabelBetween(viewModel, blk1.Id, blk2.Id);
        retEdge.EdgeType.Should().Be(CfgEdgeType.Return,
            "an edge whose source block ends in a RET must be styled CfgEdgeType.Return");
    }

    /// <summary>
    /// A block whose <see cref="CfgBlock.IsLive"/> is <c>false</c> renders with a
    /// distinct "stale" indicator surfaced as <see cref="CfgGraphNode.IsLive"/>.
    /// </summary>
    [Fact]
    public async Task IsLiveFalse_RendersStaleIndicator() {
        // Arrange
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        _harness.ContextManager.ExecutingNode = nodes.blk0Mov;

        // Mark every instruction in blk1 as non-live so blk1.IsLive observably becomes false.
        nodes.blk1Mov.SetLive(false);
        nodes.blk1Ret.SetLive(false);
        CfgBlock blk1PreCheck = nodes.blk1Mov.ContainingBlock ?? throw new InvalidOperationException("blk1Mov.ContainingBlock should not be null");
        blk1PreCheck.IsLive.Should().BeFalse("the test pre-condition requires blk1 to be non-live");

        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert — the view model surfaces the IsLive flag on the rendered graph node so the
        // AXAML view can apply stale styling.
        CfgBlock blk0 = nodes.blk0Mov.ContainingBlock ?? throw new InvalidOperationException("blk0Mov.ContainingBlock should not be null");
        CfgBlock blk1 = nodes.blk1Mov.ContainingBlock ?? throw new InvalidOperationException("blk1Mov.ContainingBlock should not be null");
        CfgGraphNode liveBlk0 = FindGraphNodeForBlock(viewModel, blk0);
        CfgGraphNode staleBlk1 = FindGraphNodeForBlock(viewModel, blk1);
        liveBlk0.IsLive.Should().BeTrue(
            "a block whose contained instructions are all live must render with IsLive=true");
        staleBlk1.IsLive.Should().BeFalse(
            "a block with at least one non-live instruction must render with IsLive=false so " +
            "the AXAML view can apply the stale visual indicator");
    }

    /// <summary>
    /// A block whose <see cref="CfgBlock.IsDiscoveryComplete"/> is <c>false</c>
    /// renders with a trailing ellipsis on its listing and a discovery-complete flag exposed
    /// to the view (the dashed outline is rendered by AXAML based on this flag).
    /// </summary>
    [Fact]
    public async Task IsDiscoveryCompleteFalse_RendersDashedOutlineAndEllipsis() {
        // Arrange — build a two-block chain where the second block is in-discovery: the
        // linker creates the second block when the predecessor's terminator (a JMP) crosses
        // a boundary, but the second block's tail is never closed because we never observe
        // a boundary or another successor on it.
        SegmentedAddress addrJmp = new(0x2000, 0x0000);
        // jmp short +0 -> EB 00 (2 bytes), targets 0x0002
        CfgInstruction jmp = _harness.InstructionHelper.WriteAndParse(addrJmp, w => {
            w.WriteUInt8(0xEB);
            w.WriteUInt8(0x00);
        });

        SegmentedAddress addrNop1 = new(0x2000, 0x0002);
        CfgInstruction nop1 = _harness.InstructionHelper.WriteAndParse(addrNop1,
            w => w.WriteUInt8(0x90));

        SegmentedAddress addrNop2 = new(0x2000, 0x0003);
        CfgInstruction nop2 = _harness.InstructionHelper.WriteAndParse(addrNop2,
            w => w.WriteUInt8(0x90));

        // Linking jmp → nop1 finalises the JMP block (the JMP is itself a terminator) and
        // creates a new block starting at nop1.
        _harness.Link(jmp, nop1);
        // Linking nop1 → nop2 extends the second block; it remains in-discovery because no
        // boundary is observed for nop2's successor yet.
        _harness.Link(nop1, nop2);

        // Verify pre-condition.
        CfgBlock jmpBlock = jmp.ContainingBlock ?? throw new InvalidOperationException("jmp.ContainingBlock should not be null");
        CfgBlock nopBlock = nop1.ContainingBlock ?? throw new InvalidOperationException("nop1.ContainingBlock should not be null");
        nopBlock.IsDiscoveryComplete.Should().BeFalse(
            "the pre-condition requires the second block to be in-discovery: no " +
            "boundary has been observed for its successor yet");

        _harness.ContextManager.ExecutingNode = jmp;
        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert
        CfgGraphNode rendered = FindGraphNodeForBlock(viewModel, nopBlock);
        rendered.IsDiscoveryComplete.Should().BeFalse(
            "a CfgBlock with IsDiscoveryComplete==false must surface that flag on its " +
            "CfgGraphNode so the AXAML view can apply the dashed-outline indicator");

        string text = rendered.ToString();
        text.Should().EndWith("\u2026",
            "an in-discovery block's listing must end with the trailing '…' marker");

        // The JMP block (which IS discovery-complete because the JMP is itself a terminator)
        // must NOT carry the trailing ellipsis nor the in-discovery flag.
        CfgGraphNode jmpRendered = FindGraphNodeForBlock(viewModel, jmpBlock);
        jmpRendered.IsDiscoveryComplete.Should().BeTrue(
            "a discovery-complete block must surface IsDiscoveryComplete=true");
        jmpRendered.ToString().Should().NotEndWith("\u2026",
            "a discovery-complete block must NOT carry the trailing '…' marker");
    }

    /// <summary>
    /// When the executing <see cref="CfgInstruction"/> is contained in a block,
    /// that block gets a red border (via <see cref="CfgGraphNode.IsExecuting"/>) and the
    /// per-instruction line carries a single 🔴 marker.
    /// </summary>
    [Fact]
    public async Task Executing_BlockHasRedBorderAndInstructionHasMarker() {
        // Arrange
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        // Executing node is the second instruction inside blk0 (interior to the block, not the
        // terminator) so the per-instruction marker landing on a non-entry, non-terminator
        // instruction is verified.
        CfgBlock blk0 = nodes.blk0Mov.ContainingBlock ?? throw new InvalidOperationException("blk0Mov.ContainingBlock should not be null");
        ICfgNode blk0Mov2 = blk0.Instructions[1];
        _harness.ContextManager.ExecutingNode = blk0Mov2;

        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert — the containing block is flagged as executing (gets red border in the view).
        CfgGraphNode renderedBlk0 = FindGraphNodeForBlock(viewModel, blk0);
        renderedBlk0.IsExecuting.Should().BeTrue(
            "the block containing the executing node must surface IsExecuting=true on its " +
            "CfgGraphNode so the AXAML view can apply the red border");

        // Exactly one 🔴 marker: on the per-instruction line only. The block title carries
        // no dot — the red border is the block-level indicator instead.
        string text = renderedBlk0.ToString();
        int markerCount = CountSubstringOccurrences(text, "\U0001f534");
        markerCount.Should().Be(1,
            "exactly one 🔴 marker must appear: on the executing instruction line; " +
            $"the block title must not carry a dot (red border is used instead); observed {markerCount} markers");

        // The other blocks must not be marked.
        CfgBlock blk1 = nodes.blk1Mov.ContainingBlock ?? throw new InvalidOperationException("blk1Mov.ContainingBlock should not be null");
        CfgGraphNode renderedBlk1 = FindGraphNodeForBlock(viewModel, blk1);
        renderedBlk1.IsExecuting.Should().BeFalse(
            "blocks not containing the executing node must surface IsExecuting=false");
    }

    /// <summary>
    /// Search navigates to the block containing an instruction matching the search
    /// term. The search rebuilds the graph rooted on that block.
    /// </summary>
    [Fact]
    public async Task Search_NavigatesToContainingBlock() {
        // Arrange
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        _harness.ContextManager.ExecutingNode = nodes.blk0Mov;
        CfgCpuViewModel viewModel = CreateViewModel();
        // Set MaxNodesToDisplay to 1 so the initial BFS rooted at blk0 only renders blk0;
        // searching for blk2's content should then re-root the BFS at blk2 and render blk2
        // (rather than the initial blk0). This makes the navigation observable in the
        // rendered Graph rather than just in transient StatusMessage state.
        viewModel.MaxNodesToDisplay = 1;

        // Build the initial graph so the searchable index is populated.
        await UpdateGraphAndWait(viewModel);

        // The 0xBEEF immediate is unique to blk2's first MOV; searching for it must navigate
        // to blk2's containing block.
        viewModel.SearchText = "0xBEEF";

        // Act
        await SearchAndWait(viewModel);

        // Assert — after navigation, the rendered graph is re-rooted on blk2 and renders it.
        // (With MaxNodesToDisplay=1, only the seed block is rendered, so we can directly
        // verify the seed is now blk2, not blk0.)
        viewModel.Graph.Should().NotBeNull();
        viewModel.NumberOfNodes.Should().Be(1,
            "MaxNodesToDisplay caps the BFS to one block; searching must re-root the BFS on " +
            "the matched block");

        // The single rendered block must be blk2 (the search target), not the original seed
        // blk0. We verify this by checking that the searchable text from blk2's instructions
        // is part of the rendered NodeEntries.
        viewModel.NodeEntries.Should().Contain(entry =>
            entry.Contains("0xBEEF", StringComparison.OrdinalIgnoreCase),
            "after search, the rendered NodeEntries must come from blk2 — the block " +
            "containing the matched instruction");
        viewModel.NodeEntries.Should().NotContain(entry =>
            entry.Contains("0x1234", StringComparison.OrdinalIgnoreCase),
            "after search, the rendered NodeEntries must NOT come from blk0 (since the BFS " +
            "is re-rooted on blk2 and MaxNodesToDisplay=1)");
    }

    /// <summary>
    /// <see cref="CfgCpuViewModel.MaxNodesToDisplay"/> caps the number of CfgBlocks
    /// rendered. The other controls (AutoFollow, search, table view, table filter) remain
    /// available with their existing semantics.
    /// </summary>
    [Fact]
    public async Task MaxNodesToDisplay_CapsBlockCount() {
        // Arrange — three-block chain. Setting MaxNodesToDisplay to 2 must cap the BFS at
        // two blocks.
        (CfgInstruction blk0Mov, CfgInstruction blk0Jmp, CfgInstruction blk1Mov, CfgInstruction blk1Ret, CfgInstruction blk2Mov, CfgInstruction blk2Retf) nodes = BuildLinearThreeBlockChain();
        _harness.ContextManager.ExecutingNode = nodes.blk0Mov;
        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 2;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert — at most two blocks rendered.
        viewModel.NumberOfNodes.Should().Be(2,
            "MaxNodesToDisplay must cap the number of CfgBlocks visited by the BFS");

        // Table filter is preserved: setting a filter must reduce the rendered table rows
        // (the filter matches Address/Assembly/Type substrings).
        int unfilteredRowCount = viewModel.TableNodes.Count;
        unfilteredRowCount.Should().BeGreaterThan(0,
            "the table view must initially contain rows derived from the rendered blocks");
        viewModel.TableFilter = "ZZZNoMatchAtAll";
        viewModel.TableNodes.Count.Should().Be(0,
            "TableFilter must filter rendered rows by substring match; setting an " +
            "impossible filter must produce zero rows");

        viewModel.TableFilter = string.Empty;
        viewModel.TableNodes.Count.Should().Be(unfilteredRowCount,
            "clearing TableFilter must restore the previously-rendered rows");

        // AutoFollow control is still settable.
        viewModel.AutoFollow = false;
        viewModel.AutoFollow.Should().BeFalse("AutoFollow control must remain available");
    }

    /// <summary>
    /// A block whose terminator is a <see cref="SelectorNode"/> renders block-level
    /// edges with <see cref="CfgEdgeType.Selector"/> styling.
    /// </summary>
    [Fact]
    public async Task SelectorTerminatedBlock_UsesSelectorEdgeStyling() {
        // Arrange — build a graph where two CfgInstructions land at the same address, which
        // forces the linker to inject a SelectorNode between the predecessor and the two
        // variants. The predecessor's CfgBlock terminates at the SelectorNode.
        SegmentedAddress addrPred = new(0x3000, 0x0000);
        // Use a NOP as the predecessor: a non-control-flow single-byte instruction.
        CfgInstruction predecessor = _harness.InstructionHelper.WriteAndParse(addrPred,
            w => w.WriteUInt8(0x90));

        // First variant at 0x3000:0x0001 (NOP), parsed from current memory.
        SegmentedAddress addrVariants = new(0x3000, 0x0001);
        CfgInstruction variantA = _harness.InstructionHelper.WriteAndParse(addrVariants,
            w => w.WriteUInt8(0x90));

        // Overwrite memory at 0x3000:0x0001 with a different opcode so the second parse
        // produces a CfgInstruction with a different signature, but the same address. We
        // pick a HLT (0xF4) — a different one-byte instruction with a distinct signature.
        CfgInstruction variantB = _harness.InstructionHelper.WriteAndParse(addrVariants,
            w => w.WriteUInt8(0xF4));

        // Wire predecessor → variantA first.
        _harness.Link(predecessor, variantA);

        // Now wire predecessor → variantB. Since variantA already occupies addrVariants in
        // the predecessor's SuccessorsPerAddress, the linker injects a SelectorNode between
        // predecessor and {variantA, variantB} via CreateSelectorNodeBetween.
        _harness.Link(predecessor, variantB);

        // The predecessor's block now has the SelectorNode as its terminator.
        CfgBlock predecessorBlock = predecessor.ContainingBlock ?? throw new InvalidOperationException("predecessor.ContainingBlock should not be null");
        predecessorBlock.Terminator.Should().BeOfType<SelectorNode>(
            "the predecessor's block must have a SelectorNode as its terminator");

        _harness.ContextManager.ExecutingNode = predecessor;
        CfgCpuViewModel viewModel = CreateViewModel();
        viewModel.MaxNodesToDisplay = 50;

        // Act
        await UpdateGraphAndWait(viewModel);

        // Assert — every edge whose source is the SelectorNode-terminated block uses the
        // Selector edge styling.
        Graph graph = viewModel.Graph.Should().NotBeNull("UpdateGraph must produce a non-null Graph").And.BeOfType<Graph>().Subject;
        List<CfgGraphEdgeLabel> selectorEdgeLabels = graph.Edges
            .Select(e => e.Label as CfgGraphEdgeLabel)
            .OfType<CfgGraphEdgeLabel>()
            .Where(l => l.EdgeType == CfgEdgeType.Selector)
            .ToList();
        selectorEdgeLabels.Should().NotBeEmpty(
            "edges originating from a SelectorNode-terminated CfgBlock must be rendered with " +
            "CfgEdgeType.Selector styling");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Locates the rendered <see cref="CfgGraphNode"/> for <paramref name="block"/> in the
    /// view model's <see cref="CfgCpuViewModel.Graph"/>. Throws if not found.
    /// </summary>
    private static CfgGraphNode FindGraphNodeForBlock(CfgCpuViewModel viewModel, CfgBlock block) {
        Graph graph = viewModel.Graph.Should().NotBeNull("UpdateGraph must produce a non-null Graph").And.BeOfType<Graph>().Subject;
        foreach (Edge edge in graph.Edges) {
            if (edge.Tail is CfgGraphNode tail && tail.NodeId == block.Id) {
                return tail;
            }
            if (edge.Head is CfgGraphNode head && head.NodeId == block.Id) {
                return head;
            }
        }
        throw new InvalidOperationException(
            $"CfgGraphNode for block id {block.Id} not found in the rendered graph; " +
            $"the BFS may have stopped before reaching it.");
    }

    /// <summary>
    /// Locates the edge label between the rendered nodes for <paramref name="fromBlockId"/> and
    /// <paramref name="toBlockId"/>. Throws if not found.
    /// </summary>
    private static CfgGraphEdgeLabel FindEdgeLabelBetween(
        CfgCpuViewModel viewModel, int fromBlockId, int toBlockId) {
        Graph graph = viewModel.Graph.Should().NotBeNull("UpdateGraph must produce a non-null Graph").And.BeOfType<Graph>().Subject;
        Edge? matchingEdge = graph.Edges.FirstOrDefault(edge =>
            edge.Tail is CfgGraphNode from
            && edge.Head is CfgGraphNode to
            && from.NodeId == fromBlockId
            && to.NodeId == toBlockId
            && edge.Label is CfgGraphEdgeLabel);

        if (matchingEdge?.Label is CfgGraphEdgeLabel label) {
            return label;
        }
        throw new InvalidOperationException(
            $"Edge from block {fromBlockId} to block {toBlockId} not found in the rendered graph");
    }

    /// <summary>Counts non-overlapping occurrences of <paramref name="needle"/> in <paramref name="haystack"/>.</summary>
    private static int CountSubstringOccurrences(string haystack, string needle) {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1) {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}

/// <summary>
/// Synchronous <see cref="IUIDispatcher"/> stand-in: runs callbacks inline so command
/// continuations execute within the test thread without requiring an Avalonia
/// <see cref="Avalonia.Threading.Dispatcher"/> pump.
/// </summary>
internal sealed class InlineUIDispatcher : IUIDispatcher {
    public Task InvokeAsync(Action callback, Avalonia.Threading.DispatcherPriority priority = default) {
        callback();
        return Task.CompletedTask;
    }

    public void Post(Action callback, Avalonia.Threading.DispatcherPriority priority = default) {
        callback();
    }

    public bool CheckAccess() => true;
}
