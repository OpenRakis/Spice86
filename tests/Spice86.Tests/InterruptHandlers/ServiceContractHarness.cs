namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

/// <summary>
/// Differential harness for interrupt service contracts. A test case is run twice on
/// two fresh machines with the same contract inputs and different values in everything
/// the contract does not declare. The declared outputs must be identical across the two
/// runs and everything not declared as written must be preserved.
/// </summary>
internal static class ServiceContractHarness {
    /// <summary>The selector register of <paramref name="handler"/>, or
    /// <see langword="null"/> for a single-service handler.</summary>
    internal static ServiceSelector? SelectorOf(IInterruptHandler handler) =>
        (handler.Services as HandlerServices.SelectedServices)?.Selector;

    /// <summary>The contract <paramref name="handler"/> declares for
    /// <paramref name="selectorValue"/>, or <see langword="null"/> when undeclared.</summary>
    internal static ServiceContract? ContractOf(IInterruptHandler handler, int selectorValue) =>
        handler.Services switch {
            HandlerServices.SingleService single => single.Contract,
            HandlerServices.SelectedServices selected =>
                selected.Contracts.GetValueOrDefault(selectorValue),
            _ => null
        };

    /// <summary>Every contract <paramref name="handler"/> declares.</summary>
    internal static IEnumerable<ServiceContract> DeclaredContracts(IInterruptHandler handler) =>
        handler.Services switch {
            HandlerServices.SingleService single => [single.Contract],
            HandlerServices.SelectedServices selected => selected.Contracts.Values,
            _ => []
        };

    private const int UpperWordBitCount = 16;
    private const uint LowerWordMask = 0xFFFF;
    private const int MemorySnapshotEnd = 0x100000;
    private const ushort ScratchSegment = 0x7000;
    private const ushort ScratchOffset = 0x0000;
    private const uint ScratchLinear = ScratchSegment * 16 + ScratchOffset;
    private const ushort ScratchReturnOffset = ScratchOffset + 2;
    private const int StubStepLimit = 100000;

    private enum ServiceRunMode { Callback, Stub }

    private sealed record RegisterField(CpuValues Bit, string Name,
        Func<State, uint> Get, Action<State, uint> Set);

    private static readonly RegisterField[] Fields = [
        new(CpuValues.Al, "AL", s => s.AL, (s, v) => s.AL = (byte)v),
        new(CpuValues.Ah, "AH", s => s.AH, (s, v) => s.AH = (byte)v),
        new(CpuValues.Bl, "BL", s => s.BL, (s, v) => s.BL = (byte)v),
        new(CpuValues.Bh, "BH", s => s.BH, (s, v) => s.BH = (byte)v),
        new(CpuValues.Cl, "CL", s => s.CL, (s, v) => s.CL = (byte)v),
        new(CpuValues.Ch, "CH", s => s.CH, (s, v) => s.CH = (byte)v),
        new(CpuValues.Dl, "DL", s => s.DL, (s, v) => s.DL = (byte)v),
        new(CpuValues.Dh, "DH", s => s.DH, (s, v) => s.DH = (byte)v),
        new(CpuValues.Si, "SI", s => s.SI, (s, v) => s.SI = (ushort)v),
        new(CpuValues.Di, "DI", s => s.DI, (s, v) => s.DI = (ushort)v),
        new(CpuValues.Bp, "BP", s => s.BP, (s, v) => s.BP = (ushort)v),
        new(CpuValues.Sp, "SP", s => s.SP, (s, v) => s.SP = (ushort)v),
        new(CpuValues.EaxHigh, "EAX_HI", s => s.EAX >> UpperWordBitCount,
            (s, v) => s.EAX = (s.EAX & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EbxHigh, "EBX_HI", s => s.EBX >> UpperWordBitCount,
            (s, v) => s.EBX = (s.EBX & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EcxHigh, "ECX_HI", s => s.ECX >> UpperWordBitCount,
            (s, v) => s.ECX = (s.ECX & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EdxHigh, "EDX_HI", s => s.EDX >> UpperWordBitCount,
            (s, v) => s.EDX = (s.EDX & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EsiHigh, "ESI_HI", s => s.ESI >> UpperWordBitCount,
            (s, v) => s.ESI = (s.ESI & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EdiHigh, "EDI_HI", s => s.EDI >> UpperWordBitCount,
            (s, v) => s.EDI = (s.EDI & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EbpHigh, "EBP_HI", s => s.EBP >> UpperWordBitCount,
            (s, v) => s.EBP = (s.EBP & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.EspHigh, "ESP_HI", s => s.ESP >> UpperWordBitCount,
            (s, v) => s.ESP = (s.ESP & LowerWordMask) | (v << UpperWordBitCount)),
        new(CpuValues.Ds, "DS", s => s.DS, (s, v) => s.DS = (ushort)v),
        new(CpuValues.Es, "ES", s => s.ES, (s, v) => s.ES = (ushort)v),
        new(CpuValues.Fs, "FS", s => s.FS, (s, v) => s.FS = (ushort)v),
        new(CpuValues.Gs, "GS", s => s.GS, (s, v) => s.GS = (ushort)v),
        new(CpuValues.Ss, "SS", s => s.SS, (s, v) => s.SS = (ushort)v),
        new(CpuValues.Cs, "CS", s => s.CS, (s, v) => s.CS = (ushort)v)
    ];

    private static readonly (CpuValues Bit, string Name, ushort Mask, Action<State, bool> Set)[] FlagFields = [
        (CpuValues.Carry, "CF", Flags.Carry, (s, v) => s.CarryFlag = v),
        (CpuValues.Parity, "PF", Flags.Parity, (s, v) => s.ParityFlag = v),
        (CpuValues.Auxiliary, "AF", Flags.Auxiliary, (s, v) => s.AuxiliaryFlag = v),
        (CpuValues.Zero, "ZF", Flags.Zero, (s, v) => s.ZeroFlag = v),
        (CpuValues.Sign, "SF", Flags.Sign, (s, v) => s.SignFlag = v),
        (CpuValues.Overflow, "OF", Flags.Overflow, (s, v) => s.OverflowFlag = v),
        (CpuValues.Direction, "DF", Flags.Direction, (s, v) => s.DirectionFlag = v)
    ];

    private sealed record RunResult(ServiceContract Contract,
        IReadOnlyDictionary<RegisterField, uint> Inputs,
        IReadOnlyDictionary<RegisterField, uint> Outputs,
        ushort InputFrameFlags, ushort OutputFrameFlags,
        byte[] MemoryBefore, byte[] MemoryAfter, IReadOnlySet<int> FrameBytes,
        int IoAccesses, int MemoryReads, int MemoryWrites);

    /// <summary>
    /// Runs the service twice on two fresh machines and asserts the contract.
    /// </summary>
    public static void AssertContract(ServiceContractCase testCase) {
        AssertContract(testCase, ServiceRunMode.Callback);
    }

    /// <summary>
    /// Same assertions as <see cref="AssertContract"/>, but the service runs through the
    /// vector's installed in-RAM stub: an <c>int</c> instruction at a scratch address steps
    /// the CPU until the stub's <c>iret</c> returns, so stub code outside the C# callback
    /// is covered too (in particular a stub that set IF would fail).
    /// </summary>
    public static void AssertContractThroughStub(ServiceContractCase testCase) {
        AssertContract(testCase, ServiceRunMode.Stub);
    }

    private static void AssertContract(ServiceContractCase testCase, ServiceRunMode mode) {
        RunResult first = RunCase(testCase, 0, mode);
        RunResult second = RunCase(testCase, 1, mode);
        ServiceContract contract = first.Contract;

        foreach (RegisterField field in Fields) {
            if (!contract.Registers.Writes.HasFlag(field.Bit)) {
                continue;
            }
            second.Outputs[field].Should().Be(first.Outputs[field],
                "{0} is in Writes of {1} but differed between the two runs", field.Name, contract.Name);
        }

        ushort frameFlagsMask = FrameFlagsOf(contract.Registers.Writes);
        (second.OutputFrameFlags & frameFlagsMask).Should().Be(first.OutputFrameFlags & frameFlagsMask,
            "frame flags in FlagsWritten of {0} differed between the two runs", contract.Name);

        if (contract.Memory is ServiceMemoryEffect.Write or ServiceMemoryEffect.ReadWrite) {
            int differingOffset = FirstDifference(first.MemoryAfter, second.MemoryAfter, first.FrameBytes);
            differingOffset.Should().Be(-1,
                "memory differed between the two runs of {0} at offset 0x{1:X}", contract.Name,
                Math.Max(differingOffset, 0));
        } else {
            foreach (RunResult run in new[] { first, second }) {
                int differingOffset = FirstDifference(run.MemoryBefore, run.MemoryAfter, run.FrameBytes);
                differingOffset.Should().Be(-1,
                    "Memory of {0} does not allow writes but memory at offset 0x{1:X} changed",
                    contract.Name, Math.Max(differingOffset, 0));
            }
        }

        foreach (RunResult run in new[] { first, second }) {
            foreach (RegisterField field in Fields) {
                if (contract.Registers.Writes.HasFlag(field.Bit)) {
                    continue;
                }
                run.Outputs[field].Should().Be(run.Inputs[field],
                    "{0} is not in Writes of {1} but was not preserved", field.Name, contract.Name);
            }
            ushort preservedMask = (ushort)(0xFF7 & ~frameFlagsMask);
            (run.OutputFrameFlags & preservedMask).Should().Be(run.InputFrameFlags & preservedMask,
                "a frame flag not in FlagsWritten of {0} was not preserved", contract.Name);
            if (!contract.Io) {
                run.IoAccesses.Should().Be(0,
                    "Io of {0} is false but an I/O port was accessed", contract.Name);
            }
            if (contract.Memory is ServiceMemoryEffect.None or ServiceMemoryEffect.Read) {
                run.MemoryWrites.Should().Be(0,
                    "Memory of {0} does not allow writes but a memory write happened", contract.Name);
            }
            if (contract.Memory is ServiceMemoryEffect.None or ServiceMemoryEffect.Write) {
                run.MemoryReads.Should().Be(0,
                    "Memory of {0} does not allow reads but a memory read happened", contract.Name);
            }
        }
    }

    private static int FirstDifference(byte[] first, byte[] second, IReadOnlySet<int> frameBytes) {
        for (int i = 0; i < first.Length; i++) {
            if (frameBytes.Contains(i)) {
                continue;
            }
            if (first[i] != second[i]) {
                return i;
            }
        }
        return -1;
    }

    private static ushort FrameFlagsOf(CpuValues flags) {
        ushort mask = 0;
        foreach ((CpuValues bit, _, ushort flagMask, _) in FlagFields) {
            if (flags.HasFlag(bit)) {
                mask |= flagMask;
            }
        }
        return mask;
    }

    private static RunResult RunCase(ServiceContractCase testCase, int runIndex, ServiceRunMode mode) {
        using ServiceContractMachine machine = new(testCase.NeedsEms, testCase.NeedsXms);
        Spice86DependencyInjection spice86 = machine.Spice86;

        if (!spice86.Machine.InstalledInterruptHandlers.Handlers.TryGetValue(testCase.Vector,
                out IInterruptHandler? handler)) {
            throw new InvalidOperationException($"No handler installed on vector 0x{testCase.Vector:X2}");
        }
        ServiceSelector? selector = SelectorOf(handler);
        ServiceContract? contract = ContractOf(handler, testCase.SelectorValue);
        if (contract is null) {
            throw new InvalidOperationException(
                $"No contract declared for {testCase} (handler selector is {selector})");
        }

        if (handler is not InterruptHandler interruptHandler) {
            throw new InvalidOperationException(
                $"Handler on vector 0x{testCase.Vector:X2} ({handler.GetType().Name}) does not derive " +
                "from InterruptHandler and cannot be run by the harness");
        }

        State state = spice86.Machine.CpuState;
        Random random = new(testCase.Vector * 131071 + testCase.SelectorValue * 131 + runIndex);
        RandomizeInputs(state, random);
        FillContractReads(state, selector, testCase.SelectorValue, contract);
        testCase.Setup?.Invoke(machine);
        // A Setup that invokes services (like EMS handle allocation) leaves the
        // selector register at its own result value; a real `int` caller sets it
        // right before the instruction, so the run does too.
        SetSelector(state, selector, testCase.SelectorValue);
        SelectorValue(state, selector).Should().Be((uint)testCase.SelectorValue,
            "the selector register must hold the case value when {0} runs",
            contract.Name);

        ushort inputFrameFlags;
        ushort frameBaseOffset;
        if (mode == ServiceRunMode.Stub) {
            // A real `int` at a scratch address pushes the frame itself.
            spice86.Machine.Memory.UInt8[ScratchLinear] = 0xCD;
            spice86.Machine.Memory.UInt8[ScratchLinear + 1] = testCase.Vector;
            state.CS = ScratchSegment;
            state.IP = ScratchOffset;
            // The `int` pushes the flags as it finds them; IF is cleared here so the
            // recorded input flags are the word the frame will hold.
            state.InterruptFlag = false;
            inputFrameFlags = state.Flags.FlagRegister16;
            frameBaseOffset = (ushort)(state.SP - 6);
        } else {
            spice86.Machine.Stack.Push16(state.Flags.FlagRegister16);
            spice86.Machine.Stack.Push16(0xF000);
            spice86.Machine.Stack.Push16(0x0100);
            inputFrameFlags = spice86.Machine.Stack.Peek16(4);
            frameBaseOffset = state.SP;
        }
        // The frame is 6 bytes at consecutive offsets; each is translated separately
        // because the offset can wrap (SP below 6 wraps to the segment top).
        HashSet<int> frameBytes = new();
        for (int i = 0; i < 6; i++) {
            frameBytes.Add((int)MemoryUtils.ToPhysicalAddress(state.SS,
                (ushort)(frameBaseOffset + i)));
        }

        IReadOnlyDictionary<RegisterField, uint> inputs = SnapshotRegisters(state);
        byte[] memoryBefore = SnapshotMemory(spice86.Machine.Memory);

        int ioAccesses = 0;
        int memoryReads = 0;
        int memoryWrites = 0;
        // Stub mode also fetches instructions through the monitored memory reads; those
        // addresses are subtracted at the end of the run.
        List<uint> readAddresses = new();
        HashSet<uint> fetchedBytes = new();
        BreakPoint ioBreakpoint = new UnconditionalBreakPoint(BreakPointType.IO_ACCESS,
            (_, _) => ioAccesses++, false);
        // The pushed interrupt frame is the call mechanism, not an effect of the
        // service: the Run() dispatch peeks the return address and flag writes on
        // the frame land inside these 6 bytes.
        HashSet<long> excludedAddresses = new(frameBytes.Select(frameByte => (long)frameByte));
        if (mode == ServiceRunMode.Stub) {
            // The `int` instruction reads the 4-byte IVT entry; that is the call
            // mechanism, not a memory read of the service.
            for (int i = 0; i < 4; i++) {
                excludedAddresses.Add(testCase.Vector * 4 + i);
            }
        }
        BreakPoint memoryReadBreakpoint = new UnconditionalBreakPoint(BreakPointType.MEMORY_READ,
            (_, address) => {
                if (address < MemorySnapshotEnd && !excludedAddresses.Contains(address)) {
                    readAddresses.Add((uint)address);
                }
            }, false);
        BreakPoint memoryWriteBreakpoint = new UnconditionalBreakPoint(BreakPointType.MEMORY_WRITE,
            (_, address) => {
                if (address < MemorySnapshotEnd && !excludedAddresses.Contains(address)) {
                    memoryWrites++;
                }
            }, false);
        EmulatorBreakpointsManager breakpoints = spice86.Machine.EmulatorBreakpointsManager;
        breakpoints.ToggleBreakPoint(ioBreakpoint, true);
        breakpoints.ToggleBreakPoint(memoryReadBreakpoint, true);
        breakpoints.ToggleBreakPoint(memoryWriteBreakpoint, true);
        // The `int` instruction clears IF; a precise service must leave it clear,
        // so no hardware interrupt can be delivered inside a typed service call.
        state.InterruptFlag = false;
        try {
            if (mode == ServiceRunMode.Stub) {
                RunThroughStub(spice86, state, testCase, contract);
            } else {
                interruptHandler.Run();
            }
        } finally {
            breakpoints.ToggleBreakPoint(ioBreakpoint, false);
            breakpoints.ToggleBreakPoint(memoryReadBreakpoint, false);
            breakpoints.ToggleBreakPoint(memoryWriteBreakpoint, false);
        }
        if (mode == ServiceRunMode.Stub) {
            // Instruction fetch is a monitored memory read too: every instruction the
            // feeder parses lands in the previous-instruction index, including graph
            // successors it linked but never executed (branch targets inside the stub).
            foreach (CfgInstruction instruction in spice86.Machine.CfgCpu.CfgNodeFeeder
                         .InstructionsFeeder.PreviousInstructions.GetAll()) {
                for (uint address = instruction.Address.Linear;
                     address < instruction.Address.Linear + instruction.Length; address++) {
                    fetchedBytes.Add(address);
                }
            }
        }
        state.InterruptFlag.Should().BeFalse(
            "{0} ran with IF clear, the state an `int` leaves; a precise service that set IF "
            + "would deliver a hardware interrupt inside a typed call", contract.Name);
        memoryReads = readAddresses.Count(address => !fetchedBytes.Contains(address));
        ushort outputFrameFlags = mode == ServiceRunMode.Stub ? state.Flags.FlagRegister16 : spice86.Machine.Stack.Peek16(4);

        return new RunResult(contract,
            inputs,
            SnapshotRegisters(state),
            inputFrameFlags,
            outputFrameFlags,
            memoryBefore,
            SnapshotMemory(spice86.Machine.Memory),
            frameBytes,
            ioAccesses,
            memoryReads,
            memoryWrites);
    }

    private static void RunThroughStub(Spice86DependencyInjection spice86, State state,
        ServiceContractCase testCase, ServiceContract contract) {
        int steps = 0;
        while ((state.CS != ScratchSegment || state.IP != ScratchReturnOffset)
               && steps < StubStepLimit) {
            spice86.Machine.CfgCpu.ExecuteNext();
            steps++;
            state.InterruptFlag.Should().BeFalse(
                "the stub of {0} set IF; a hardware interrupt could be delivered "
                + "inside a typed call", contract.Name);
        }
        (state.CS == ScratchSegment && state.IP == ScratchReturnOffset).Should().BeTrue(
            "the stub of {0} did not return to the scratch address within {1} steps",
            contract.Name, StubStepLimit);
    }

    internal static void RandomizeInputs(State state, Random random) {
        state.EAX = (uint)random.Next();
        state.EBX = (uint)random.Next();
        state.ECX = (uint)random.Next();
        state.EDX = (uint)random.Next();
        state.ESI = (uint)random.Next();
        state.EDI = (uint)random.Next();
        state.EBP = (uint)random.Next();
        state.DS = (ushort)random.Next(0x10000);
        state.ES = (ushort)random.Next(0x10000);
        state.FS = (ushort)random.Next(0x10000);
        state.GS = (ushort)random.Next(0x10000);
        foreach ((_, _, _, Action<State, bool> set) in FlagFields) {
            set(state, random.Next(2) == 0);
        }
    }

    internal static void FillContractReads(State state, ServiceSelector? selector, int selectorValue,
        ServiceContract contract) {
        foreach (RegisterField field in Fields.Where(field => contract.Registers.Reads.HasFlag(field.Bit))) {
            field.Set(state, 0);
        }
        foreach ((CpuValues bit, _, _, Action<State, bool> set) in FlagFields) {
            if (contract.Registers.Reads.HasFlag(bit)) {
                set(state, false);
            }
        }
        SetSelector(state, selector, selectorValue);
    }

    internal static void SetSelector(State state, ServiceSelector? selector, int selectorValue) {
        switch (selector) {
            case ServiceSelector.Ah:
                state.AH = (byte)selectorValue;
                break;
            case ServiceSelector.Al:
                state.AL = (byte)selectorValue;
                break;
            case ServiceSelector.Ax:
                state.AX = (ushort)selectorValue;
                break;
        }
    }

    internal static uint SelectorValue(State state, ServiceSelector? selector) {
        return selector switch {
            ServiceSelector.Ah => state.AH,
            ServiceSelector.Al => state.AL,
            ServiceSelector.Ax => state.AX,
            _ => 0
        };
    }

    private static Dictionary<RegisterField, uint> SnapshotRegisters(State state) {
        Dictionary<RegisterField, uint> values = new();
        foreach (RegisterField field in Fields) {
            values[field] = field.Get(state);
        }
        return values;
    }

    internal static byte[] SnapshotMemory(IMemory memory) {
        byte[] snapshot = new byte[MemorySnapshotEnd];
        IList<byte> view = memory.GetSlice(0, MemorySnapshotEnd);
        for (int i = 0; i < snapshot.Length; i++) {
            snapshot[i] = view[i];
        }
        return snapshot;
    }
}
