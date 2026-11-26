namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Engine checks of the finished machine (CFG nodes and blocks, graph transfers, CPU shadow state),
/// one method per catalogue fixture. They run on every emulated run of the fixture they are attached
/// to, after its behavioral oracle: a result oracle may only read what the emulated and the generated
/// run agree on, so these assertions live on the fixture, never in the oracle itself.
/// </summary>
internal static class AsmScenarioEngineAssertions {
    /// <summary>jmpmov ends on the HLT that follows the stored far-jump target.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void JmpMov(AsmRunScope scope, Machine machine) {
        State state = machine.CpuState;
        uint endAddress = MemoryUtils.ToPhysicalAddress(state.CS, state.IP);
        // Last instruction HLT is one byte long and is at 0xF400C
        Assert.Equal((uint)0xF400D, endAddress);
    }

    /// <summary>segpr: a division by 0 CPU fault is handled by an interrupt handler that rewrites the
    /// return address, checked at the instruction level, at the block level and in the graph transfers.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void Segpr(AsmRunScope scope, Machine machine) {
        // Here, a division by 0 occurred causing a CPU fault. It is handled by an interrupt handler.
        CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
        CfgInstruction? divBy0 = currentInstructions.GetAtAddress(new(0xF000, 0x005F));
        CfgInstruction? divBy0HandlerEntry = currentInstructions.GetAtAddress(new(0xF000, 0x1100));
        CfgInstruction? divBy0HandlerIret = currentInstructions.GetAtAddress(new(0xF000, 0x1111));
        CfgInstruction? divBy0NextInstruction = currentInstructions.GetAtAddress(new(0xF000, 0x0065));
        Assert.NotNull(divBy0);
        Assert.NotNull(divBy0HandlerEntry);
        Assert.NotNull(divBy0HandlerIret);
        Assert.NotNull(divBy0NextInstruction);
        // Check that the int handler is linked to the division by 0 as a cpu fault type successor
        Assert.Contains(divBy0HandlerEntry, divBy0.Successors);
        Assert.Contains(divBy0HandlerEntry, divBy0.SuccessorsPerType[InstructionSuccessorType.CpuFault]);
        // Check that the instruction next to the div by 0 to which the handler returned to  is linked to the division by 0 as a regular "Call to return" link.
        // Side-note, normally, div by 0 int handler should return to the div instruction. However, here the handler edits the call stack making it return to the next instruction which is how a regular function call in a high level language would behave
        Assert.Contains(divBy0NextInstruction, divBy0.Successors);
        Assert.Contains(divBy0NextInstruction, divBy0.SuccessorsPerType[InstructionSuccessorType.CallToReturn]);
        // Check that IRET is normally connected to the return target
        Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.Successors);
        Assert.Contains(divBy0NextInstruction, divBy0HandlerIret.SuccessorsPerType[InstructionSuccessorType.Normal]);

        // Block-level assertions: same scenario verified at the CfgBlock layer.

        // divBy0 carries an extra CpuFault successor, so it is the Terminator of its block.
        CfgBlock? divBy0Block = divBy0.ContainingBlock;
        Assert.NotNull(divBy0Block);
        divBy0Block.IsDiscoveryComplete.Should().BeTrue();
        divBy0Block.Terminator.Should().BeSameAs(divBy0);

        // The handler entry at F000:1100 is the entry of the handler block.
        CfgBlock? handlerBlock = divBy0HandlerEntry.ContainingBlock;
        Assert.NotNull(handlerBlock);
        handlerBlock.IsDiscoveryComplete.Should().BeTrue();
        handlerBlock.Entry.Should().BeSameAs(divBy0HandlerEntry);

        // The IRET remains in the handler block: returned terminators are valid interior
        // nodes, and the executor cold-steps them when entered directly.
        CfgBlock? iretBlock = divBy0HandlerIret.ContainingBlock;
        Assert.NotNull(iretBlock);
        iretBlock.Should().BeSameAs(handlerBlock);
        iretBlock.Entry.Should().BeSameAs(divBy0HandlerEntry);
        iretBlock.Terminator.Should().BeSameAs(divBy0HandlerIret);
        iretBlock.IsDiscoveryComplete.Should().BeTrue();

        // The instruction following divBy0 in memory is the entry of the post-fault block.
        CfgBlock? nextBlock = divBy0NextInstruction.ContainingBlock;
        Assert.NotNull(nextBlock);
        nextBlock.IsDiscoveryComplete.Should().BeTrue();
        nextBlock.Entry.Should().BeSameAs(divBy0NextInstruction);

        // The blocks are distinct.
        divBy0Block.Should().NotBeSameAs(handlerBlock);
        divBy0Block.Should().NotBeSameAs(nextBlock);
        iretBlock.Should().NotBeSameAs(nextBlock);
        handlerBlock.Should().NotBeSameAs(nextBlock);

        // Block-level edges derived from the underlying instruction-level edges.
        IEnumerable<CfgBlock?> divBlockSuccessors = divBy0Block.Successors.Select(s => s.ContainingBlock);
        // CpuFault edge: div block → handler block.
        divBlockSuccessors.Should().Contain(handlerBlock);
        // CallToReturn edge: div block → post-fault block (handler rewrote the stack
        // to return past the div instead of retrying it).
        divBlockSuccessors.Should().Contain(nextBlock);

        // Normal edge: IRET block -> post-fault block.
        iretBlock.Successors.Select(s => s.ContainingBlock).Should().Contain(nextBlock);

        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Should().Contain(t => t.Kind == "callOut",
            "the call whose callee never returns must produce a callOut transfer");
    }

    /// <summary>selfmodifyvalue: the overwritten immediate is read from memory, and the variant merge
    /// keeps the post-replacement instruction inside its CfgBlock.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void SelfModifyValue(AsmRunScope scope, Machine machine) {
        CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
        CfgInstruction? instruction = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x00D));
        Assert.NotNull(instruction);
        // The immediate value field is the last field in a MOV reg, imm16 instruction
        InstructionField<ushort> immField = (InstructionField<ushort>)instruction.FieldsInOrder[^1];
        // Code should have been modified so instruction should use memory and not stored value
        Assert.False(immField.UseValue);
        Assert.Equal(instruction.Address.Linear + 1, immField.PhysicalAddress);

        // Block-level assertions: variant merging must keep the post-replacement
        // instruction inside its CfgBlock.
        CfgBlock? movBlock = instruction.ContainingBlock;
        Assert.NotNull(movBlock);
        Assert.Contains(instruction, movBlock.Instructions);
        // The mov sits right after the jmp short 0x000D (a terminator), so it is the entry
        // of its own block.
        Assert.Same(instruction, movBlock.Entry);
        // The block's discovery is complete and keeps both graph successors: the loop-back
        // jump and the terminal HLT path.
        Assert.True(movBlock.IsDiscoveryComplete);
        CfgInstruction? loopJump = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x000B));
        CfgInstruction? hlt = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x001C));
        Assert.NotNull(loopJump);
        Assert.NotNull(hlt);
        movBlock.Successors.Should().BeEquivalentTo([loopJump, hlt]);
        foreach (ICfgNode successor in movBlock.Successors) {
            Assert.NotNull(successor.ContainingBlock);
            Assert.Same(successor, successor.ContainingBlock.Entry);
            Assert.True(successor.IsBlockTerminator);
        }
    }

    /// <summary>
    /// selfmodifyje: regression for the bug in <c>NodeLinker</c> where
    /// <c>SwitchPredecessorsToNew</c> adds the new successor before removing the old one.
    /// If adding the new successor brings <c>Successors.Count</c> to
    /// <c>MaxSuccessorsCount</c>, <c>CanHaveMoreSuccessors</c> is set to <c>false</c>.
    /// Removing the old successor afterwards drops the count back below the max, but
    /// <c>CanHaveMoreSuccessors</c> is never reset to <c>true</c>.
    /// This causes the <c>je -> hlt</c> fall-through link to never be added.
    /// </summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void SelfModifyJe(AsmRunScope scope, Machine machine) {
        CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
        // Layout (F000:0000 = start):
        //   0000: mov cx, 0       (3 bytes)
        //   0003: cmp cx, 0       (3 bytes)  <- jump label
        //   0006: je selfmodify   (2 bytes)
        //   0008: hlt             (1 byte)
        //   0009: mov ax, 1234    (3 bytes)  <- selfmodify label
        //   (selfmodify body grew by two result stores; internal offsets shifted)
        CfgInstruction? je = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x0006));
        CfgInstruction? hlt = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x0008));
        CfgInstruction? selfModify = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x0009));
        Assert.NotNull(je);
        Assert.NotNull(hlt);
        Assert.NotNull(selfModify);
        // Both successor paths are discovered: selfModify (taken) and hlt (fall-through).
        Assert.Equal(2, je.Successors.Count);
        // hlt must be a successor of je (fall-through path, added on iteration 3)
        Assert.Contains(hlt, je.Successors);
        // selfModify must still be a successor of je (taken path)
        Assert.Contains(selfModify, je.Successors);
    }

    /// <summary>selfmodifycall: the SelectorNode inserted mid-execution dispatches between the two
    /// variants at the patched address and finalises the predecessor's CfgBlock.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void SelfModifyCall(AsmRunScope scope, Machine machine) {
        // Block-level assertions: SelectorNode insertion via CreateSelectorNodeBetween
        // must finalise the predecessor's CfgBlock and must not disturb the variant
        // CfgInstructions' containing-block back-pointers.
        CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;
        // The call near 0x0017 sits at F000:000D and is the predecessor of the SelectorNode
        // injected at F000:0010 once the second-iteration return point switches from `nop`
        // to `hlt` (the byte at F000:0010 is patched mid-execution).
        CfgInstruction? call = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x000D));
        Assert.NotNull(call);
        SelectorNode selector = call.Successors.OfType<SelectorNode>().Single();

        // The SelectorNode dispatches between the two variants (nop and hlt) at F000:0010.
        Assert.Equal(new SegmentedAddress(0xF000, 0x0010), selector.Address);
        ICfgNode[] variants = selector.Successors.ToArray();
        Assert.Equal(2, variants.Length);
        // Each variant is itself a CfgInstruction with its own non-null ContainingBlock.
        foreach (ICfgNode variant in variants) {
            CfgInstruction variantInstruction = Assert.IsAssignableFrom<CfgInstruction>(variant);
            Assert.NotNull(variantInstruction.ContainingBlock);
            Assert.Contains(variantInstruction, variantInstruction.ContainingBlock.Instructions);
        }

        // The predecessor's (call's) CfgBlock is finalised: the call is itself a block
        // terminator, so its block was discovery-complete from the moment the call landed
        // in it, and the subsequent SelectorNode insertion did not regress that state.
        CfgBlock? callBlock = call.ContainingBlock;
        Assert.NotNull(callBlock);
        Assert.True(callBlock.IsDiscoveryComplete);
        Assert.True(callBlock.Terminator.IsBlockTerminator);
        Assert.Same(call, callBlock.Terminator);
        // The SelectorNode is reachable as a block-level successor (the underlying
        // instruction-level edge call → selector aliases through CfgBlock.Successors).
        Assert.Contains(selector, callBlock.Successors);

        // Graph-level assertion: speculation entered the overwritten address before the patch.
        AssertDeadBlockAt(GraphOf(machine), "selfmodifycall", new SegmentedAddress(0xF000, 0x0010));
    }

    /// <summary>selfmodifyterminator: the SelectorNode injected at the terminator's address is
    /// absorbed into the predecessor's block by normal continuation.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void SelfModifyTerminator(AsmRunScope scope, Machine machine) {
        // Block-level assertions: Case T continuation — the SelectorNode injected at the
        // terminator's address (F000:0019) is absorbed into the predecessor's block because
        // the predecessor (or ax, ax at F000:0017) is a non-terminator whose
        // NextInMemoryAddress matches the selector's address.
        CurrentInstructions currentInstructions = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;

        // The 'or ax, ax' at F000:0017 is the predecessor of the SelectorNode.
        CfgInstruction? orAxAx = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x0017));
        Assert.NotNull(orAxAx);

        // The SelectorNode at F000:0019 (where the jne was patched to jmp).
        SelectorNode selector = orAxAx.Successors.OfType<SelectorNode>().Single();
        Assert.Equal(new SegmentedAddress(0xF000, 0x0019), selector.Address);

        // The selector is appended to the predecessor block by normal continuation.
        CfgBlock? predecessorBlock = orAxAx.ContainingBlock;
        Assert.NotNull(predecessorBlock);
        CfgBlock? selectorBlock = selector.ContainingBlock;
        Assert.NotNull(selectorBlock);
        Assert.Same(predecessorBlock, selectorBlock);
        Assert.Contains(orAxAx, predecessorBlock.Instructions);
        Assert.Same(selector, selectorBlock.Terminator);
        Assert.True(predecessorBlock.IsDiscoveryComplete);
        Assert.True(selectorBlock.IsDiscoveryComplete);

        // The selector dispatches between two variants (original jne and patched jmp).
        ICfgNode[] variants = selector.Successors.ToArray();
        Assert.Equal(2, variants.Length);
        foreach (ICfgNode variant in variants) {
            CfgInstruction variantInstruction = Assert.IsAssignableFrom<CfgInstruction>(variant);
            Assert.NotNull(variantInstruction.ContainingBlock);
            Assert.Contains(variantInstruction, variantInstruction.ContainingBlock.Instructions);
        }

        // Graph-level assertion: speculation entered the rewritten terminator and its target.
        CfgCpuGraph graph = GraphOf(machine);
        AssertDeadBlockAt(graph, "selfmodifyterminator", new SegmentedAddress(0xF000, 0x0019));
        AssertDeadBlockAt(graph, "selfmodifyterminator", new SegmentedAddress(0xF000, 0x001B));
    }

    /// <summary>divfaultloop: the fault handler's retried division rewires the fault edges, checked
    /// at the instruction level, at the block level and in the graph transfers.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void DivFaultLoop(AsmRunScope scope, Machine machine) {
        CurrentInstructions currentInstructions =
            machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;

        // div word[cs:0x102] at F000:0029
        CfgInstruction? div = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x0029));
        // handler entry (mov [0x08],bp) at F000:003B
        CfgInstruction? handlerEntry = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x003B));
        // handler iret at F000:005C
        CfgInstruction? handlerIret = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x005C));
        // divblock entry (mov bp,05678h) at F000:001D — the handler rewrites the return
        // address so iret returns here instead of to the faulting div.
        CfgInstruction? divblockEntry = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x001D));
        // post-fault instruction: mov bx,[cs:0x100] at F000:002E
        CfgInstruction? postDiv = currentInstructions.GetAtAddress(new SegmentedAddress(0xF000, 0x002E));

        Assert.NotNull(div);
        Assert.NotNull(handlerEntry);
        Assert.NotNull(handlerIret);
        Assert.NotNull(divblockEntry);
        Assert.NotNull(postDiv);

        // Instruction-level: div has CpuFault edge to handler entry
        Assert.Contains(handlerEntry, div.Successors);
        Assert.Contains(handlerEntry, div.SuccessorsPerType[InstructionSuccessorType.CpuFault]);

        // Instruction-level: div has CallToReturn edge to divblockEntry
        // (handler rewrites return IP to divblock start)
        Assert.Contains(divblockEntry, div.Successors);
        Assert.Contains(postDiv, div.Successors);
        Assert.Contains(postDiv, div.SuccessorsPerType[InstructionSuccessorType.Normal]);

        // Block-level: div is a block terminator (CpuFault gives it multiple successor types)
        CfgBlock? divBlock = div.ContainingBlock;
        Assert.NotNull(divBlock);
        divBlock.Terminator.Should().BeSameAs(div);
        divBlock.IsDiscoveryComplete.Should().BeTrue();

        // Block-level: divblockEntry is in the same block as div (the block entry is divblockEntry)
        divBlock.Entry.Should().BeSameAs(divblockEntry);

        // Block-level: handler entry starts its own block
        CfgBlock? handlerBlock = handlerEntry.ContainingBlock;
        Assert.NotNull(handlerBlock);
        handlerBlock.Entry.Should().BeSameAs(handlerEntry);
        handlerBlock.Should().NotBeSameAs(divBlock);

        // Block-level: post-div instruction starts its own block
        CfgBlock? postDivBlock = postDiv.ContainingBlock;
        Assert.NotNull(postDivBlock);
        postDivBlock.Entry.Should().BeSameAs(postDiv);
        postDivBlock.Should().NotBeSameAs(divBlock);

        // Block-level edges
        IEnumerable<CfgBlock?> divBlockSuccessors = divBlock.Successors.Select(s => s.ContainingBlock);
        divBlockSuccessors.Should().Contain(handlerBlock, "CpuFault edge to handler");
        // The CallToReturn edge from div goes to divblockEntry which is the entry of
        // divBlock itself — so the block-level successor list includes divBlock (self-loop).
        divBlockSuccessors.Should().Contain(divBlock,
            "handler rewrites return to divblock entry, creating a self-loop at block level");

        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Should().Contain(t => t.Kind == "cpuFault",
            "the div fault edge to the handler must produce a cpuFault transfer");
    }

    /// <summary>
    /// Callback CFG assertions of intchain.com: the INT 8 chain, the layout of the handler behind IVT[8]
    /// and the block level edges around them. They run on the shared route, after the fixture's behavioral
    /// oracle and the listing and CFG goldens of the same run.
    /// </summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void IntChain(AsmRunScope scope, Machine machine) {
        IMemory memory = machine.Memory;
        // Entry INT 8 in the COM, observed on the fresh machine before the program ran
        SegmentedAddress entryPoint = scope.Observations.Get<SegmentedAddress>(AsmRunScope.EntryPointObservation);
        InterruptVectorTable ivt = new(memory);
        CurrentInstructions currentInstructions =
            machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;

        // Entry INT 8 in the COM
        CfgInstruction? int8Entry = currentInstructions.GetAtAddress(entryPoint);
        Assert.NotNull(int8Entry);

        // Post-INT8 instruction in COM: int 8 is 2 bytes long
        SegmentedAddress postInt8Addr = entryPoint + 2;
        CfgInstruction? postInt8 = currentInstructions.GetAtAddress(postInt8Addr);
        Assert.NotNull(postInt8);

        // INT 8 handler entry: callback at IVT[8]
        CfgInstruction? int8HandlerEntry = currentInstructions.GetAtAddress(ivt[8]);
        Assert.NotNull(int8HandlerEntry);

        // INT 1C handler entry: IRET-only at IVT[1C]
        CfgInstruction? int1CHandlerEntry = currentInstructions.GetAtAddress(ivt[0x1C]);
        Assert.NotNull(int1CHandlerEntry);

        // A) Entry INT8 has exactly two successors: handler entry and post-INT8 (call-to-return link)
        int8Entry.Successors.Should().BeEquivalentTo([int8HandlerEntry, postInt8]);

        // Inside INT8 handler exact layout:
        // [0] callback at IVT[8] (4 bytes)
        // [4] PUSH DS, PUSH AX, PUSH DX (3 bytes)
        // [7] INT 1C (2 bytes)
        // [9] EOI callback (4 bytes)
        // [13] POP DX, POP AX, POP DS (3 bytes)
        // [16] IRET (1 byte)

        SegmentedAddress addrPushDs = int8HandlerEntry.Address + 4;
        SegmentedAddress addrInt1C = addrPushDs + 3;
        SegmentedAddress addrEoiCallback = addrInt1C + 2;
        SegmentedAddress addrPopDx = addrEoiCallback + 4;
        SegmentedAddress addrIret8 = addrPopDx + 3;

        CfgInstruction? pushDs = currentInstructions.GetAtAddress(addrPushDs);
        CfgInstruction? intNode1C = currentInstructions.GetAtAddress(addrInt1C);
        CfgInstruction? eoiCallback = currentInstructions.GetAtAddress(addrEoiCallback);
        CfgInstruction? popDx = currentInstructions.GetAtAddress(addrPopDx);
        CfgInstruction? iret8 = currentInstructions.GetAtAddress(addrIret8);

        Assert.NotNull(pushDs);
        Assert.NotNull(intNode1C);
        Assert.NotNull(eoiCallback);
        Assert.NotNull(popDx);
        Assert.NotNull(iret8);

        // B) Callback (tick++) must fall through to the register saves only
        int8HandlerEntry.Successors.Should().BeEquivalentTo([pushDs]);

        // C) INT 1C node must have exactly two successors:
        //    - INT 1C handler entry (invoke)
        //    - fallthrough to EOI callback (return target after INT)
        intNode1C.Successors.Should().BeEquivalentTo([int1CHandlerEntry, eoiCallback]);

        // D) INT 1C handler (IRET-only) must return to EOI callback only
        int1CHandlerEntry.Successors.Should().BeEquivalentTo([eoiCallback]);

        // E) EOI callback must fall through to the register restores only
        eoiCallback.Successors.Should().BeEquivalentTo([popDx]);

        // F) INT8 IRET must return to post-INT8 instruction only
        iret8.Successors.Should().BeEquivalentTo([postInt8]);

        // G) No direct edge from entry INT8 to INT1C handler
        int8Entry.Successors.Should().NotContain(int1CHandlerEntry);

        // ---------------------------------------------------------------
        // Block-level assertions
        // ---------------------------------------------------------------
        CfgBlock? int8EntryBlock = int8Entry.ContainingBlock;
        CfgBlock? int8HandlerEntryBlock = int8HandlerEntry.ContainingBlock;
        CfgBlock? postInt8Block = postInt8.ContainingBlock;
        CfgBlock? iret8Block = iret8.ContainingBlock;
        CfgBlock? eoiCallbackBlock = eoiCallback.ContainingBlock;

        Assert.NotNull(int8EntryBlock);
        Assert.NotNull(int8HandlerEntryBlock);
        Assert.NotNull(postInt8Block);
        Assert.NotNull(iret8Block);
        Assert.NotNull(eoiCallbackBlock);

        int8EntryBlock.IsDiscoveryComplete.Should().BeTrue();
        int8HandlerEntryBlock.IsDiscoveryComplete.Should().BeTrue();
        postInt8Block.IsDiscoveryComplete.Should().BeTrue();
        iret8Block.IsDiscoveryComplete.Should().BeTrue();
        eoiCallbackBlock.IsDiscoveryComplete.Should().BeTrue();

        // INT 8 (call) is a block terminator, so it ends its block.
        int8EntryBlock.Terminator.Should().BeSameAs(int8Entry);

        // The INT 8 handler entry callback has unbounded MaxSuccessorsCount,
        // making it a block terminator on its own (single-instruction block).
        int8HandlerEntryBlock.Entry.Should().BeSameAs(int8HandlerEntry);
        int8HandlerEntryBlock.Terminator.Should().BeSameAs(int8HandlerEntry);

        // The post-INT8 fall-through is the entry of its block.
        postInt8Block.Entry.Should().BeSameAs(postInt8);

        // IRET is a Return, hence a block terminator.
        iret8Block.Terminator.Should().BeSameAs(iret8);

        // The EOI callback is a callback (unbounded successors), so it is the
        // terminator of its containing block.
        eoiCallbackBlock.Terminator.Should().BeSameAs(eoiCallback);

        // CfgBlock-to-block edges: block.Successors entries are CfgInstructions
        // (the underlying terminator's successors). Follow ContainingBlock to
        // get the corresponding blocks.
        int8EntryBlock.Successors.Select(s => s.ContainingBlock)
            .Should().Contain(int8HandlerEntryBlock); // Call edge
        int8EntryBlock.Successors.Select(s => s.ContainingBlock)
            .Should().Contain(postInt8Block);         // CallToReturn edge

        eoiCallbackBlock.Successors.Select(s => s.ContainingBlock)
            .Should().Contain(iret8Block);            // Normal fall-through to IRET

        iret8Block.Successors.Select(s => s.ContainingBlock)
            .Should().Contain(postInt8Block);         // Normal IRET return edge
    }

    /// <summary>
    /// sticli: the CfgBlock-level structure around STI / CLI. The fixture
    /// <c>sticli.bin</c> (BIOS-style, entry at F000:FFF0) contains:
    /// <code>
    /// mov ax, 0x1000
    /// mov ds, ax
    /// sti
    /// mov ax, 0x1234
    /// cli
    /// mov [si], ax
    /// hlt
    /// </code>
    /// STI is a Block_Terminator and CLI is a Block_Starter, so the linker must
    /// split the instruction stream into three distinct blocks chained via successors.
    /// </summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void StiCli(AsmRunScope scope, Machine machine) {
        machine.CpuState.IsRunning.Should().BeFalse(
            "the program is expected to reach HLT");
        machine.CpuState.InterruptShadowing.Should().BeFalse(
            "InterruptShadowing arms for one instruction after STI and is consumed at the next boundary");

        CurrentInstructions ci =
            machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions;

        // Code layout at F000:0000 (jumped to from the BIOS entry point at F000:FFF0):
        //   +0: B8 00 10           mov ax, 0x1000        (3 bytes)
        //   +3: 8E D8              mov ds, ax            (2 bytes)
        //   +5: FB                 sti                   (1 byte)  Block_Terminator
        //   +6: B8 34 12           mov ax, 0x1234        (3 bytes) middle block
        //   +9: FA                 cli                   (1 byte)  Block_Starter
        //   +A: 89 04              mov [si], ax          (2 bytes)
        //   +C: F4                 hlt                   (1 byte)
        SegmentedAddress stiAddr = new(0xF000, 0x0005);
        SegmentedAddress movAxImmAddr = new(0xF000, 0x0006);
        SegmentedAddress cliAddr = new(0xF000, 0x0009);

        CfgInstruction? sti = ci.GetAtAddress(stiAddr);
        CfgInstruction? movAxImm = ci.GetAtAddress(movAxImmAddr);
        CfgInstruction? cli = ci.GetAtAddress(cliAddr);

        Assert.NotNull(sti);
        Assert.NotNull(movAxImm);
        Assert.NotNull(cli);

        sti.IsBlockTerminator.Should().BeTrue("STI is an explicit Block_Terminator");
        cli.IsBlockStarter.Should().BeTrue("CLI is an explicit Block_Starter");
        movAxImm.IsBlockTerminator.Should().BeFalse("the middle MOV is not itself a terminator");
        movAxImm.IsBlockStarter.Should().BeFalse("the middle MOV is not itself a starter");

        CfgBlock? stiBlock = sti.ContainingBlock;
        CfgBlock? midBlock = movAxImm.ContainingBlock;
        CfgBlock? cliBlock = cli.ContainingBlock;

        Assert.NotNull(stiBlock);
        Assert.NotNull(midBlock);
        Assert.NotNull(cliBlock);

        stiBlock.IsDiscoveryComplete.Should().BeTrue();
        midBlock.IsDiscoveryComplete.Should().BeTrue();
        cliBlock.IsDiscoveryComplete.Should().BeTrue();

        // STI is the terminator of its block.
        stiBlock.Terminator.Should().BeSameAs(sti);

        // The middle block contains exactly one instruction (the MOV between STI and CLI).
        midBlock.Entry.Should().BeSameAs(movAxImm);
        midBlock.Terminator.Should().BeSameAs(movAxImm);

        // CLI is the entry of its block.
        cliBlock.Entry.Should().BeSameAs(cli);

        // The three blocks must be distinct.
        stiBlock.Should().NotBeSameAs(midBlock);
        midBlock.Should().NotBeSameAs(cliBlock);
        stiBlock.Should().NotBeSameAs(cliBlock);

        // Block-level successors: STI -> MID -> CLI.
        stiBlock.Successors.Select(s => s.ContainingBlock).Should().Contain(midBlock);
        midBlock.Successors.Select(s => s.ContainingBlock).Should().Contain(cliBlock);
    }

    /// <summary>partition_indirect_call_jump: one indirect call instruction has two observed targets,
    /// each transferring to a different landing entry while its return frame is open, and the program
    /// still exercises an indirect jump.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void PartitionIndirectCallJump(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);

        graph.Partitions.Should().NotBeNull();
        graph.Partitions.Should().HaveCountGreaterThan(2, "the two distinct landing entries and trampolines are observed");
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Where(t => t.Kind == "callOut")
            .GroupBy(t => t.From)
            .Should().Contain(group => group.Select(t => t.Target).Distinct().Count() == 2,
                "one indirect call instruction must dispatch to both trampoline targets");
        graph.Transfers.Where(t => t.Kind == "crossPartitionFlow")
            .Select(t => t.Target).Distinct().Should().HaveCountGreaterThanOrEqualTo(2,
                "each target must transfer to a different landing entry");
        graph.Transfers.Should().Contain(t => t.Kind == "alignedReturn");
    }

    /// <summary>One far call site observes four targets; two tail-jump to distinct landings.</summary>
    public static void PartitionFarDispatch(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Where(t => t.Kind == "callOut").GroupBy(t => t.From)
            .Should().Contain(group => group.Select(t => t.Target).Distinct().Count() == 4,
                "one far call instruction must reach all four targets");
        graph.Transfers.Where(t => t.Kind == "crossPartitionFlow").Select(t => t.Target)
            .Distinct().Should().HaveCountGreaterThanOrEqualTo(2,
                "the far trampolines must reach different function entries");
    }

    /// <summary>Two overlapping calls share a return address but reach different landings.</summary>
    public static void PartitionSharedReturnLandings(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Where(t => t.Kind == "callOut")
            .GroupBy(t => t.CallContinuationAddress)
            .Should().Contain(group => group.Key != null
                && group.Select(t => t.From).Distinct().Count() == 2
                && group.Select(t => t.Target).Distinct().Count() == 2,
                "two distinct CALL instructions must share one return address and call different targets");
        graph.Transfers.Where(t => t.Kind == "crossPartitionFlow")
            .Select(t => t.Target).Distinct().Should().HaveCountGreaterThanOrEqualTo(2,
                "the two callees must reach separate landing entries");
    }

    /// <summary>partition_shared_tail: the ret of the shared tail produces an alignedReturn transfer.</summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void PartitionSharedTail(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);
        graph.Transfers.Should().NotBeNull();
        graph.Transfers.Should().Contain(t => t.Kind == "alignedReturn",
            "the ret of the shared tail back to each caller must produce an alignedReturn transfer");
    }

    /// <summary>
    /// ownerlesscallcycle: a CFG function-partitioning failure on an ownership-preserving call/loop cycle that
    /// no partition root can reach. The fixture bootstraps a loop by pushing the loop continuation and
    /// executing RET into it. That continuation is also the aligned call-continuation target of the
    /// loop body's CALL, so the partitioner suppresses promoting the return target to a root. The loop
    /// head is reached only by the loop-back jump, so it is not a call target either. The whole cycle
    /// therefore had no root and every block in it was ownerless, which made partitioning throw and the
    /// snapshot degrade to a blocks-only graph (no partitions). The ownerless-region rescue fixes this
    /// by promoting a root for the unreachable cycle, so partitioning now attributes every block. The
    /// golden CfgBlocks/CfgPartitions fixtures lock in the recovered partitioning.
    /// </summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void OwnerlessCallCycle(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);

        graph.Truncated.Should().BeFalse("the graph is small and fully exported");
        graph.Partitions.Should().NotBeNull(
            "partitioning must not throw on the ownerless cycle and degrade the snapshot to a blocks-only graph");
        CfgPartitionInfo[] partitions = graph.Partitions ?? [];
        int[] partitionedBlocks = partitions.SelectMany(partition => partition.Blocks).Distinct().ToArray();
        partitionedBlocks.Should().BeEquivalentTo(graph.Blocks.Select(block => block.Id),
            "every exported block, including the rescued ownership-preserving cycle, must belong to exactly one partition");
    }

    /// <summary>
    /// sameretloop: a function whose near return pops its own entry address, so CS:IP does not move
    /// when the return executes. The CFG must still record the return as a real transfer into the loop
    /// body, and partitioning must attribute every block of the loop, so the generated-code run can keep
    /// dispatching the override instead of reading the unchanged address as a completion.
    /// </summary>
    /// <param name="scope">Prepared run, holding the entry point observed before the program ran.</param>
    /// <param name="machine">Machine of the finished run.</param>
    public static void SameretLoop(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);

        graph.Truncated.Should().BeFalse("the graph is small and fully exported");
        graph.Partitions.Should().NotBeNull("partitioning must attribute the same-address return loop");
        CfgPartitionInfo[] partitions = graph.Partitions ?? [];
        int[] partitionedBlocks = partitions.SelectMany(partition => partition.Blocks).Distinct().ToArray();
        partitionedBlocks.Should().BeEquivalentTo(graph.Blocks.Select(block => block.Id),
            "every block of the same-address return loop must belong to exactly one partition");
    }

    // ---- CFG shape assertions over block addresses ----
    // Every check below reads the CfgCpuGraph the CfgBlocksTestJson exporter produces, states facts by
    // SegmentedAddress (never by block id) and fails naming the scenario key and the address.

    private static string EntryOf(SegmentedAddress address) => $"{address.Segment:X4}:{address.Offset:X4}";

    private static CfgCpuGraph GraphOf(Machine machine) =>
        CfgBlocksTestJson.BuildGraph(machine.CfgCpu.ExecutionContextManager);

    private static CfgBlockInfo? BlockAt(CfgCpuGraph graph, SegmentedAddress address) =>
        graph.Blocks.FirstOrDefault(block => block.Entry == EntryOf(address));

    private static void AssertDeadBlockAt(CfgCpuGraph graph, string key, SegmentedAddress address) =>
        graph.Blocks.Should().Contain(block => block.Entry == EntryOf(address) && block.Dead == true,
            $"{key}: a dead version of the speculated arm must be entered at {EntryOf(address)}");

    private static void AssertDeadBlocksAt(CfgCpuGraph graph, string key, params SegmentedAddress[] addresses) {
        foreach (SegmentedAddress address in addresses) {
            AssertDeadBlockAt(graph, key, address);
        }
    }

    private static void AssertLiveBlockAt(CfgCpuGraph graph, string key, SegmentedAddress address) {
        CfgBlockInfo? block = BlockAt(graph, address);
        block.Should().NotBeNull($"{key}: a block must be entered at {EntryOf(address)}");
        block!.Dead.Should().NotBe(true, $"{key}: the block at {EntryOf(address)} ran in this run");
    }

    private static void AssertNoBlockAt(CfgCpuGraph graph, string key, SegmentedAddress address) =>
        BlockAt(graph, address).Should().BeNull($"{key}: no block may be entered at {EntryOf(address)}");

    private static void AssertNoDeadBlocks(CfgCpuGraph graph, string key) =>
        graph.Blocks.Should().OnlyContain(block => block.Dead != true,
            $"{key}: with speculation off no block is dead");

    private static void AssertPartitionEntryAt(CfgCpuGraph graph, string key, SegmentedAddress address) {
        graph.Partitions.Should().NotBeNull($"{key}: partitioning must run");
        graph.Partitions!.SelectMany(partition => partition.Entries)
            .Should().Contain(entry => entry.Address == EntryOf(address),
                $"{key}: a partition must be entered at {EntryOf(address)}");
    }

    private static void AssertPartitionEntryCountAt(CfgCpuGraph graph, string key, SegmentedAddress address, int count) {
        graph.Partitions.Should().NotBeNull($"{key}: partitioning must run");
        graph.Partitions!.Count(partition => partition.Entries.Any(entry => entry.Address == EntryOf(address)))
            .Should().Be(count, $"{key}: {EntryOf(address)} must be an entry of exactly {count} partitions");
    }

    private static void AssertSamePartitionHasEntries(CfgCpuGraph graph, string key, params SegmentedAddress[] addresses) {
        graph.Partitions.Should().NotBeNull($"{key}: partitioning must run");
        string[] entries = addresses.Select(EntryOf).ToArray();
        graph.Partitions!.Should().Contain(partition => entries.All(
                entry => partition.Entries.Any(candidate => candidate.Address == entry)),
            $"{key}: one partition must hold the entries {string.Join(", ", entries)}");
    }

    private static void AssertEntryPointAt(CfgCpuGraph graph, string key, SegmentedAddress address) =>
        graph.EntryPointAddresses.Should().Contain(EntryOf(address),
            $"{key}: {EntryOf(address)} must be an entry point of the graph");

    private static void AssertTransferKind(CfgCpuGraph graph, string key, string kind) {
        graph.Transfers.Should().NotBeNull($"{key}: transfers must be exported");
        graph.Transfers!.Should().Contain(transfer => transfer.Kind == kind,
            $"{key}: a {kind} transfer must exist");
    }

    private static void AssertTransferKindCount(CfgCpuGraph graph, string key, string kind, int count) {
        graph.Transfers.Should().NotBeNull($"{key}: transfers must be exported");
        graph.Transfers!.Count(transfer => transfer.Kind == kind)
            .Should().Be(count, $"{key}: exactly {count} {kind} transfers must exist");
    }

    private static void AssertSinglePartitionOfLiveBlocks(CfgCpuGraph graph, string key) {
        graph.Partitions.Should().NotBeNull($"{key}: partitioning must run");
        int[] liveBlocks = graph.Blocks.Where(block => block.Dead != true).Select(block => block.Id).ToArray();
        graph.Partitions!.Should().ContainSingle($"{key}: all live blocks belong to a single partition")
            .Which.Blocks.Should().BeEquivalentTo(liveBlocks,
                $"{key}: the single partition covers every live block");
    }

    /// <summary>speculative_branch observed arm: the JNZ-taken arm is a dead speculated block.</summary>
    public static void SpeculativeBranchObserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_branch", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_branch unobserved arm: the fallthrough arm is the dead speculated block.</summary>
    public static void SpeculativeBranchUnobserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_branch", new SegmentedAddress(0xF000, 0x000C));

    /// <summary>speculative_branch without speculation: no dead blocks and no block at the taken arm.</summary>
    public static void SpeculativeBranchNoSpec(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertNoDeadBlocks(graph, "speculative_branch");
        AssertNoBlockAt(graph, "speculative_branch", new SegmentedAddress(0xF000, 0x0013));
    }

    /// <summary>speculative_run_same_offset observed arm: dead blocks at the same offset in two segments.</summary>
    public static void SpeculativeRunSameOffsetObserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "speculative_run_same_offset",
            new SegmentedAddress(0xF010, 0x010D), new SegmentedAddress(0xF000, 0x010D));

    /// <summary>speculative_run_same_offset unobserved arm: the other pair of same-offset blocks is dead.</summary>
    public static void SpeculativeRunSameOffsetUnobserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "speculative_run_same_offset",
            new SegmentedAddress(0xF010, 0x0107), new SegmentedAddress(0xF000, 0x0107));

    /// <summary>speculative_closure observed arm: the speculated arm, its loop-back and continuation are dead.</summary>
    public static void SpeculativeClosureObserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "speculative_closure",
            new SegmentedAddress(0xF000, 0x0022), new SegmentedAddress(0xF000, 0x001B),
            new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_closure unobserved arm: the other arm's entry block is dead.</summary>
    public static void SpeculativeClosureUnobserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_closure", new SegmentedAddress(0xF000, 0x000C));

    /// <summary>speculative_convergence observed arm: the speculated arm is dead.</summary>
    public static void SpeculativeConvergenceObserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_convergence", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_convergence unobserved arm: the other arm's entry block is dead.</summary>
    public static void SpeculativeConvergenceUnobserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_convergence", new SegmentedAddress(0xF000, 0x000C));

    /// <summary>speculative_mixed_block observed arm: the unobserved arm block is dead.</summary>
    public static void SpeculativeMixedBlockObserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_mixed_block", new SegmentedAddress(0xF000, 0x000C));

    /// <summary>speculative_mixed_block unobserved arm: the observed arm block is dead.</summary>
    public static void SpeculativeMixedBlockUnobserved(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_mixed_block", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_call_entry: speculation created a call arm and entered the subroutine body it calls.</summary>
    public static void SpeculativeCallEntry(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "speculative_call_entry",
            new SegmentedAddress(0xF000, 0x0013), new SegmentedAddress(0xF000, 0x001B));

    /// <summary>speculative_invalid_opcode: speculation stopped at the invalid-opcode region, no block exists there.</summary>
    public static void SpeculativeInvalidOpcode(AsmRunScope scope, Machine machine) =>
        AssertNoBlockAt(GraphOf(machine), "speculative_invalid_opcode", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_zero_memory: speculation entered the zero region as a dead block while the
    /// patched-over code it shadows ran live.</summary>
    public static void SpeculativeZeroMemory(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertDeadBlockAt(graph, "speculative_zero_memory", new SegmentedAddress(0xF000, 0x0030));
        AssertLiveBlockAt(graph, "speculative_zero_memory", new SegmentedAddress(0xF000, 0x0043));
    }

    /// <summary>speculative_transfer_into_data: all four transfers into data produced dead blocks.</summary>
    public static void SpeculativeTransferIntoData(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "speculative_transfer_into_data",
            new SegmentedAddress(0xF000, 0x001F), new SegmentedAddress(0xF000, 0x0021),
            new SegmentedAddress(0xF000, 0x0026), new SegmentedAddress(0xF000, 0x002B));

    /// <summary>speculative_smc_guard: the self-modifying arm speculation found is dead.</summary>
    public static void SpeculativeSmcGuard(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_smc_guard", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_smc_lowram: the self-modifying store's arm in low RAM is a dead block.</summary>
    public static void SpeculativeSmcLowRam(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_smc_lowram", new SegmentedAddress(0x016F, 0x0118));

    /// <summary>speculative_discard: the speculated arm that gets discarded is dead.</summary>
    public static void SpeculativeDiscard(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_discard", new SegmentedAddress(0xF000, 0x0013));

    /// <summary>speculative_a20_smc_guard: the A20-wrapped speculated arm is dead.</summary>
    public static void SpeculativeA20SmcGuard(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "speculative_a20_smc_guard", new SegmentedAddress(0xFFFE, 0x0014));

    /// <summary>callstackunwind observed arm: the leaf's discarded-return path is dead; every nested call
    /// target is a partition entry.</summary>
    public static void CallStackUnwindObserved(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertDeadBlockAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x040D));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0100));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0200));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0300));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0400));
    }

    /// <summary>callstackunwind unobserved arm: the same partition entries, with the other leaf path dead.</summary>
    public static void CallStackUnwindUnobserved(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertDeadBlockAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0407));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0100));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0200));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0300));
        AssertPartitionEntryAt(graph, "callstackunwind", new SegmentedAddress(0xF000, 0x0400));
    }

    /// <summary>interior_entry: the interior landings are graph entry points.</summary>
    public static void InteriorEntry(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertEntryPointAt(graph, "interior_entry", new SegmentedAddress(0xF000, 0x0200));
        AssertEntryPointAt(graph, "interior_entry", new SegmentedAddress(0xF000, 0x020C));
        AssertEntryPointAt(graph, "interior_entry", new SegmentedAddress(0xF000, 0x0300));
    }

    /// <summary>jump1 without speculation: every branch lands on observed code, no dead blocks.</summary>
    public static void Jump1NoSpec(AsmRunScope scope, Machine machine) =>
        AssertNoDeadBlocks(GraphOf(machine), "jump1");

    /// <summary>selfmodifyinstructions: two dead block versions sit at the rewritten entry.</summary>
    public static void SelfModifyInstructions(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        graph.Blocks.Where(block => block.Entry == EntryOf(new SegmentedAddress(0xF000, 0x1400)) && block.Dead == true)
            .Should().HaveCount(2, "selfmodifyinstructions: two speculated versions of the rewritten entry are dead");
    }

    /// <summary>selfmodifyrep: the rep-prefixed rewrite's speculated version is dead.</summary>
    public static void SelfModifyRep(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "selfmodifyrep", new SegmentedAddress(0xF000, 0x001C));

    /// <summary>selfmodifycalltarget: the shared call-target address is an entry of three partitions.</summary>
    public static void SelfModifyCallTarget(AsmRunScope scope, Machine machine) =>
        AssertPartitionEntryCountAt(GraphOf(machine), "selfmodifycalltarget",
            new SegmentedAddress(0xF000, 0x0028), 3);

    /// <summary>partition_jump_into_function_middle: a partition is entered in the middle of the routine.</summary>
    public static void PartitionJumpIntoFunctionMiddle(AsmRunScope scope, Machine machine) =>
        AssertPartitionEntryAt(GraphOf(machine), "partition_jump_into_function_middle",
            new SegmentedAddress(0xF000, 0x0025));

    /// <summary>partition_multi_entry_dominated_shared: the three synthetic entries of the shared region.</summary>
    public static void PartitionMultiEntryDominatedShared(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_multi_entry_dominated_shared", new SegmentedAddress(0xF000, 0x0041));
        AssertPartitionEntryAt(graph, "partition_multi_entry_dominated_shared", new SegmentedAddress(0xF000, 0x0046));
        AssertPartitionEntryAt(graph, "partition_multi_entry_dominated_shared", new SegmentedAddress(0xF000, 0x004B));
    }

    /// <summary>partition_multi_entry_irreducible_shared: the irreducible region's two entries sit in one partition.</summary>
    public static void PartitionMultiEntryIrreducibleShared(AsmRunScope scope, Machine machine) =>
        AssertSamePartitionHasEntries(GraphOf(machine), "partition_multi_entry_irreducible_shared",
            new SegmentedAddress(0xF000, 0x006F), new SegmentedAddress(0xF000, 0x007D));

    /// <summary>partition_multi_entry_disconnected_shared: the two disconnected regions each get their two
    /// shared entries.</summary>
    public static void PartitionMultiEntryDisconnectedShared(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_multi_entry_disconnected_shared", new SegmentedAddress(0xF000, 0x0045));
        AssertPartitionEntryAt(graph, "partition_multi_entry_disconnected_shared", new SegmentedAddress(0xF000, 0x004B));
        AssertPartitionEntryAt(graph, "partition_multi_entry_disconnected_shared", new SegmentedAddress(0xF000, 0x0051));
        AssertPartitionEntryAt(graph, "partition_multi_entry_disconnected_shared", new SegmentedAddress(0xF000, 0x0055));
    }

    /// <summary>partition_cross_function_loop: both loop partners are partition entries and the loop shows a
    /// cyclic cross-partition flow.</summary>
    public static void PartitionCrossFunctionLoop(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_cross_function_loop", new SegmentedAddress(0xF000, 0x0028));
        AssertPartitionEntryAt(graph, "partition_cross_function_loop", new SegmentedAddress(0xF000, 0x002E));
        AssertTransferKind(graph, "partition_cross_function_loop", "cyclicCrossPartitionFlow");
    }

    /// <summary>partition_mixed_activation_cycle: both activation entries are partition entries joined by a
    /// cyclic cross-partition flow.</summary>
    public static void PartitionMixedActivationCycle(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_mixed_activation_cycle", new SegmentedAddress(0xF000, 0x0028));
        AssertPartitionEntryAt(graph, "partition_mixed_activation_cycle", new SegmentedAddress(0xF000, 0x002E));
        AssertTransferKind(graph, "partition_mixed_activation_cycle", "cyclicCrossPartitionFlow");
    }

    /// <summary>partition_jump_table: every live block belongs to the single partition.</summary>
    public static void PartitionJumpTable(AsmRunScope scope, Machine machine) =>
        AssertSinglePartitionOfLiveBlocks(GraphOf(machine), "partition_jump_table");

    /// <summary>partition_untested_indirect: the indirect dispatch's unobserved targets are dead blocks.</summary>
    public static void PartitionUntestedIndirect(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "partition_untested_indirect",
            new SegmentedAddress(0xF000, 0x001A), new SegmentedAddress(0xF000, 0x0013));

    /// <summary>partition_jump_table_mixed: every live block belongs to the single partition.</summary>
    public static void PartitionJumpTableMixed(AsmRunScope scope, Machine machine) =>
        AssertSinglePartitionOfLiveBlocks(GraphOf(machine), "partition_jump_table_mixed");

    /// <summary>partition_far_call_chain: the far calls enter partitions in two different segments.</summary>
    public static void PartitionFarCallChain(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_far_call_chain", new SegmentedAddress(0xF010, 0x0044));
        AssertPartitionEntryAt(graph, "partition_far_call_chain", new SegmentedAddress(0xF001, 0x0141));
    }

    /// <summary>partition_far_jump_segment_change: the far jump's landing segment is a partition entry.</summary>
    public static void PartitionFarJumpSegmentChange(AsmRunScope scope, Machine machine) =>
        AssertPartitionEntryAt(GraphOf(machine), "partition_far_jump_segment_change",
            new SegmentedAddress(0xF100, 0x004F));

    /// <summary>partition_sign_conditions: the untested sign arms are dead blocks.</summary>
    public static void PartitionSignConditions(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "partition_sign_conditions",
            new SegmentedAddress(0xF000, 0x0032), new SegmentedAddress(0xF000, 0x0012));

    /// <summary>partition_untested_far_indirect: the unobserved far indirect targets are dead blocks.</summary>
    public static void PartitionUntestedFarIndirect(AsmRunScope scope, Machine machine) =>
        AssertDeadBlocksAt(GraphOf(machine), "partition_untested_far_indirect",
            new SegmentedAddress(0xF000, 0x001B), new SegmentedAddress(0xF000, 0x0013));

    /// <summary>partition_untaken_cpuid: the CPUID arm the run never took is a dead block.</summary>
    public static void PartitionUntakenCpuid(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "partition_untaken_cpuid", new SegmentedAddress(0xF000, 0x0010));

    /// <summary>partition_signed_min_compare: the unobserved signed-min arm is a dead block.</summary>
    public static void PartitionSignedMinCompare(AsmRunScope scope, Machine machine) =>
        AssertDeadBlockAt(GraphOf(machine), "partition_signed_min_compare", new SegmentedAddress(0xF000, 0x0029));

    /// <summary>partition_back_edge_fallthrough: every live block belongs to the single partition.</summary>
    public static void PartitionBackEdgeFallthrough(AsmRunScope scope, Machine machine) =>
        AssertSinglePartitionOfLiveBlocks(GraphOf(machine), "partition_back_edge_fallthrough");

    /// <summary>partition_jump_to_next: every live block belongs to the single partition.</summary>
    public static void PartitionJumpToNext(AsmRunScope scope, Machine machine) =>
        AssertSinglePartitionOfLiveBlocks(GraphOf(machine), "partition_jump_to_next");

    /// <summary>partition_two_fault_handlers: both fault-handler partitions are entries and the two faults
    /// produce cpuFault transfers.</summary>
    public static void PartitionTwoFaultHandlers(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_two_fault_handlers", new SegmentedAddress(0xF000, 0x001A));
        AssertPartitionEntryAt(graph, "partition_two_fault_handlers", new SegmentedAddress(0xF000, 0x003A));
        AssertTransferKindCount(graph, "partition_two_fault_handlers", "cpuFault", 2);
    }

    /// <summary>partition_mutual_recursion_unwind: both mutually recursive call targets are partition entries
    /// joined by a cyclic cross-partition flow.</summary>
    public static void PartitionMutualRecursionUnwind(AsmRunScope scope, Machine machine) {
        CfgCpuGraph graph = GraphOf(machine);
        AssertPartitionEntryAt(graph, "partition_mutual_recursion_unwind", new SegmentedAddress(0xF000, 0x0015));
        AssertPartitionEntryAt(graph, "partition_mutual_recursion_unwind", new SegmentedAddress(0xF000, 0x0025));
        AssertTransferKind(graph, "partition_mutual_recursion_unwind", "cyclicCrossPartitionFlow");
    }
}
