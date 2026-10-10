namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.VM;

/// <summary>One method per fixture scenario. Every call returns a new fixture, so per-run state stays private.</summary>
internal static class AsmFixtureCatalog {
    /// <summary>Selects the unobserved arm of a speculative fixture, selector byte 0000:0500 = 1.</summary>
    public static readonly Action<Machine> SelectUnobservedArm = machine => machine.Memory.UInt8[0, 0x0500] = 0x01;

    /// <summary>Runs the bin and checks RAM from linear 0 against its recorded memory dump.</summary>
    public static AsmFixture Dump(string binName) {
        return MemoryFixture(new AsmRunSettings { BinName = binName }, ReadMemoryDump(binName));
    }

    /// <summary>Runs bitwise: the recorded dump patched with the four dosbox values.</summary>
    public static AsmFixture Bitwise() {
        byte[] expectedMemory = ReadMemoryDump("bitwise");
        // dosbox values
        expectedMemory[0x9F] = 0x12;
        expectedMemory[0x9D] = 0x12;
        expectedMemory[0x9B] = 0x12;
        expectedMemory[0x99] = 0x12;
        return MemoryFixture(new AsmRunSettings { BinName = "bitwise" }, expectedMemory);
    }

    /// <summary>Runs control: the recorded dump patched with the dosbox value at 0x1.</summary>
    public static AsmFixture Control() {
        byte[] expectedMemory = ReadMemoryDump("control");
        // dosbox value
        expectedMemory[0x1] = 0x78;
        return MemoryFixture(new AsmRunSettings { BinName = "control" }, expectedMemory);
    }

    /// <summary>Runs mul: the recorded dump patched with the 15 dosbox values.</summary>
    public static AsmFixture Mul() {
        byte[] expectedMemory = ReadMemoryDump("mul");
        // dosbox values
        expectedMemory[0xA2] = 0x86;
        expectedMemory[0x9E] = 0x46;
        expectedMemory[0x9C] = 0x87;
        expectedMemory[0x9A] = 0x83;
        expectedMemory[0x98] = 0x82;
        expectedMemory[0x96] = 0x86;
        expectedMemory[0x92] = 0x46;
        expectedMemory[0x73] = 0x2;
        expectedMemory[0xAA] = 0x42;
        expectedMemory[0xAE] = 0x2;
        expectedMemory[0xB0] = 0x3;
        expectedMemory[0xB2] = 0x2;
        expectedMemory[0xB4] = 0x3;
        expectedMemory[0xB6] = 0x42;
        expectedMemory[0xBA] = 0x2;
        return MemoryFixture(new AsmRunSettings { BinName = "mul" }, expectedMemory);
    }

    /// <summary>Runs div2: the quotient, remainder and divisor words the program stored.</summary>
    public static AsmFixture Div2() {
        byte[] expectedMemory = new byte[6];
        expectedMemory[0x00] = 0x3D; // quotient low  (AX = 0x8F3D)
        expectedMemory[0x01] = 0x8F; // quotient high
        expectedMemory[0x02] = 0x89; // remainder low (DX = 0x9089)
        expectedMemory[0x03] = 0x90; // remainder high
        expectedMemory[0x04] = 0xC3; // divisor low   (CX = 0xE4C3)
        expectedMemory[0x05] = 0xE4; // divisor high
        return MemoryFixture(new AsmRunSettings { BinName = "div2" }, expectedMemory);
    }

    /// <summary>Runs jmpmov: the far jump target word it stored.</summary>
    public static AsmFixture JmpMov() {
        // 0x4001 in little endian
        byte[] expectedMemory = [0x01, 0x40];
        return MemoryFixture(new AsmRunSettings { BinName = "jmpmov" }, expectedMemory);
    }

    /// <summary>Runs returnedterminator: the four words pushed onto the stack.</summary>
    public static AsmFixture ReturnedTerminator() {
        byte[] expectedMemory = new byte[8];
        expectedMemory[0x04] = 0x22;
        expectedMemory[0x05] = 0x22;
        expectedMemory[0x06] = 0x11;
        expectedMemory[0x07] = 0x11;
        return MemoryFixture(new AsmRunSettings { BinName = "returnedterminator", MaxCycles = 1000 }, expectedMemory);
    }

    /// <summary>Runs lockprefix: three invalid LOCK uses fire INT 6, three valid ones complete.</summary>
    public static AsmFixture LockPrefix() {
        return ResultFixture(new AsmRunSettings { BinName = "lockprefix" }, result => {
            // [0x0000] = invalid_lock_count: LOCK MOV [mem], LOCK ADD reg, LOCK INC reg
            result.Memory.UInt16[0, 0x0000].Should().Be(3, "lockprefix: three invalid LOCK uses should each trigger INT 6");
            // [0x0002] = valid_lock_count: set to 3 after the three valid tests complete
            result.Memory.UInt16[0, 0x0002].Should().Be(3, "lockprefix: three valid LOCK uses should complete without triggering INT 6");
        });
    }

    /// <summary>Runs selfmodifyvalue: the immediate the program overwrote in memory.</summary>
    public static AsmFixture SelfModifyValue() {
        byte[] expectedMemory = new byte[4];
        expectedMemory[0x00] = 0x0a;
        expectedMemory[0x01] = 0x00;
        expectedMemory[0x02] = 0xff;
        expectedMemory[0x03] = 0xff;
        return MemoryFixture(new AsmRunSettings { BinName = "selfmodifyvalue" }, expectedMemory);
    }

    /// <summary>Runs selfmodifyinstructions: the counters the program left in memory.</summary>
    public static AsmFixture SelfModifyInstructions() {
        byte[] expectedMemory = new byte[6];
        expectedMemory[0x00] = 0x03;
        expectedMemory[0x01] = 0x00;
        expectedMemory[0x02] = 0x02;
        expectedMemory[0x03] = 0x00;
        expectedMemory[0x04] = 0x01;
        expectedMemory[0x05] = 0x00;
        return MemoryFixture(new AsmRunSettings { BinName = "selfmodifyinstructions" }, expectedMemory);
    }

    /// <summary>Runs selfmodifyrep: the 0x21 bytes of the rep-patched buffer.</summary>
    public static AsmFixture SelfModifyRep() {
        byte[] expectedMemory = new byte[0x21];
        for (int i = 0; i < 8; i++) {
            expectedMemory[i] = 0xAB;
        }
        expectedMemory[0x20] = 0x02;
        return MemoryFixture(new AsmRunSettings { BinName = "selfmodifyrep" }, expectedMemory);
    }

    /// <summary>Runs selfmodifyterminator: the stack holds both the first-pass and the patched marker.</summary>
    public static AsmFixture SelfModifyTerminator() {
        // Expected stack memory: 42 00 FF FF
        // First push: AX=0xFFFF (first pass marker)
        // Second push: AX=0x0042 (after patch, at 'done' label)
        byte[] expectedMemory = [0x42, 0x00, 0xFF, 0xFF];
        return MemoryFixture(new AsmRunSettings { BinName = "selfmodifyterminator" }, expectedMemory);
    }

    /// <summary>Runs selfmodifyje: the patched immediate landed in AX and the loop ran once more.</summary>
    public static AsmFixture SelfModifyJe() {
        return ResultFixture(new AsmRunSettings { BinName = "selfmodifyje" }, result => {
            result.State.AX.Should().Be(5678, "selfmodifyje: mov ax, 1234 was patched to mov ax, 5678");
            result.State.CX.Should().Be(1, "selfmodifyje: CX is 1 after the patched comparison ends the loop");
        });
    }

    /// <summary>Runs selfmodifycall: the counter was bumped once and the stack is back where start set it.</summary>
    public static AsmFixture SelfModifyCall() {
        return ResultFixture(new AsmRunSettings { BinName = "selfmodifycall" }, result => {
            result.Memory.UInt8[result.State.DS, 0x002B].Should().Be(1, "selfmodifycall: the counter byte was incremented once");
            result.State.SS.Should().Be(0, "selfmodifycall: start sets SS to 0");
            result.State.SP.Should().Be(0x0100, "selfmodifycall: start sets SP to 0x0100");
        });
    }

    /// <summary>Runs externalint: the timer interrupt handler counted one tick.</summary>
    public static AsmFixture ExternalInt() {
        byte[] expectedMemory = new byte[6];
        expectedMemory[0x00] = 0x01;
        AsmRunSettings settings = new() {
            BinName = "externalint",
            MaxCycles = 0xFFFFFFF,
            EnablePit = true
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs stipending: both tick counters advanced while interrupts were pending.</summary>
    public static AsmFixture StiPending() {
        byte[] expectedMemory = new byte[2];
        expectedMemory[0x00] = 0x01;
        expectedMemory[0x01] = 0x01;
        AsmRunSettings settings = new() {
            BinName = "stipending",
            MaxCycles = 0xFFFFFFF,
            EnablePit = true
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs ioports: every IN/OUT/INS/OUTS form reads back through a latch port.</summary>
    public static AsmFixture IoPorts() {
        byte[] expectedMemory = [
            0x5A, 0x00, 0x5A, 0xA5, 0x78, 0x56, 0x34, 0x12,
            0xC3, 0x00, 0xC3, 0x3C, 0x21, 0x43, 0x65, 0x87,
            0x11, 0x00, 0x1A, 0x2B, 0x0A, 0x0B, 0x0C, 0x0D,
            0xEE, 0xEE, 0xEE, 0xEE, 0x9A, 0x00, 0xA6, 0xB7,
            0xC4, 0xD3, 0xE2, 0xF1, 0x03,
        ];
        AsmRunSettings settings = new() {
            BinName = "ioports",
            MaxCycles = 10000,
            FailOnUnhandledPort = true,
            ConfigureMachine = machine => new LatchIoPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher, 0xE0)
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs the interior-entry IRQ fixture: all three phases finish without an IRQ seeing a broken stack.</summary>
    public static AsmFixture InteriorEntry() {
        AsmRunSettings settings = new() {
            BinName = "interior_entry",
            MaxCycles = 100000,
            EnablePit = true
        };
        return ResultFixture(settings, AssertInteriorEntryCompleted);
    }

    /// <summary>Runs divfaultloop: the fault handler retried once and the division produced 2.</summary>
    public static AsmFixture DivFaultLoop() {
        byte[] expectedMemory = new byte[4];
        expectedMemory[0x00] = 0x03; // retrycount low
        expectedMemory[0x01] = 0x00; // retrycount high
        expectedMemory[0x02] = 0x02; // quotient low (10 / 5 = 2)
        expectedMemory[0x03] = 0x00; // quotient high
        return MemoryFixture(new AsmRunSettings { BinName = "divfaultloop" }, expectedMemory);
    }

    /// <summary>Runs linearsamesegmenteddifferent with the A20 gate open: the wrap-around marker is there.</summary>
    public static AsmFixture LinearSameSegmentedDifferent() {
        byte[] expectedMemory = [0x02, 0x00];
        AsmRunSettings settings = new() {
            BinName = "linearsamesegmenteddifferent",
            EnableA20Gate = true
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs sticli: mov [si], ax with DS = 0x1000 and SI = 0 wrote 0x1234 to 1000:0000.</summary>
    public static AsmFixture StiCli() {
        return ResultFixture(new AsmRunSettings { BinName = "sticli", MaxCycles = 1000 }, result => {
            result.Memory.UInt16[0x1000, 0x0000].Should().Be(0x1234, "sticli: mov [si], ax stores AX at DS:SI");
        });
    }

    /// <summary>Runs speculative_branch on the observed or unobserved arm and checks the arm's result bytes.</summary>
    public static AsmFixture SpeculativeBranch(SpeculativeArm arm) {
        if (arm == SpeculativeArm.Unobserved) {
            return SpeculativeFixture("speculative_branch", SpeculativeMemory(0x01, 0xEE, 0xAA), arm);
        }
        return SpeculativeFixture("speculative_branch", SpeculativeMemory(0x01, 0xDD, 0xAA), arm);
    }

    /// <summary>Runs speculative_closure on the given arm: the unobserved arm loops once more.</summary>
    public static AsmFixture SpeculativeClosure(SpeculativeArm arm) {
        if (arm == SpeculativeArm.Unobserved) {
            return SpeculativeFixture("speculative_closure", SpeculativeMemory(0x01, 0x03, 0xBB), arm);
        }
        return SpeculativeFixture("speculative_closure", SpeculativeMemory(0x01, 0xDD, 0xBB), arm);
    }

    /// <summary>Runs speculative_convergence on the given arm: both arms converge on the same merge point.</summary>
    public static AsmFixture SpeculativeConvergence(SpeculativeArm arm) {
        byte[] expectedMemory = new byte[0x404];
        expectedMemory[0x400] = 0x01;
        if (arm == SpeculativeArm.Unobserved) {
            expectedMemory[0x401] = 0xBB;
        } else {
            expectedMemory[0x401] = 0xAA;
        }
        expectedMemory[0x402] = 0xCC;
        expectedMemory[0x403] = 0xFF;
        return SpeculativeFixture("speculative_convergence", expectedMemory, arm);
    }

    /// <summary>Runs speculative_mixed_block on the given arm: the unobserved arm falls through to 0xEE.</summary>
    public static AsmFixture SpeculativeMixedBlock(SpeculativeArm arm) {
        if (arm == SpeculativeArm.Unobserved) {
            return SpeculativeFixture("speculative_mixed_block", SpeculativeMemory(0x01, 0xEE, 0xAA), arm);
        }
        return SpeculativeFixture("speculative_mixed_block", SpeculativeMemory(0x01, 0xDD, 0xAA), arm);
    }

    /// <summary>Runs speculative_call_entry on its observed arm: the call target wrote the observed bytes.</summary>
    public static AsmFixture SpeculativeCallEntry() {
        return SpeculativeFixture("speculative_call_entry", SpeculativeMemory(0x01, 0xDD, 0xAA), SpeculativeArm.Observed);
    }

    /// <summary>Runs speculative_invalid_opcode on its observed arm: the observed bytes were written.</summary>
    public static AsmFixture SpeculativeInvalidOpcode() {
        return SpeculativeFixture("speculative_invalid_opcode", SpeculativeMemory(0x01, 0xDD, 0xEE), SpeculativeArm.Observed);
    }

    /// <summary>Runs speculative_smc_guard on its observed arm: the run completes without self-modification.</summary>
    public static AsmFixture SpeculativeSmcGuard() {
        return SpeculativeFixture("speculative_smc_guard", SpeculativeMemory(0x01, 0xDD, 0xAA), SpeculativeArm.Observed);
    }

    /// <summary>Runs speculative_discard on its observed arm: the run completes without divergence.</summary>
    public static AsmFixture SpeculativeDiscard() {
        return SpeculativeFixture("speculative_discard", SpeculativeMemory(0x01, 0xDD, 0xAA), SpeculativeArm.Observed);
    }

    /// <summary>Returns the register oracle of the given partition fixture bin.</summary>
    /// <param name="binName">Partition bin name.</param>
    /// <returns>The fixture for that bin.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bin name is not a partition fixture.</exception>
    public static AsmFixture Partition(string binName) {
        switch (binName) {
            case "partition_jump_into_function_middle":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 10000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1111, $"{binName}: routine sets AX");
                    result.State.BX.Should().Be(0x2222, $"{binName}: routine_middle sets BX");
                });
            case "partition_shared_tail":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1111, $"{binName}: function_a sets AX");
                    result.State.BX.Should().Be(0x2222, $"{binName}: function_b sets BX");
                    result.State.CX.Should().Be(0x3333, $"{binName}: shared_tail sets CX");
                });
            case "partition_multi_entry_dominated_shared":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 10000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1001, $"{binName}: the last root entry leaves AL = 1");
                    result.State.BX.Should().Be(0xB222, $"{binName}: the shared region sets BX");
                    result.State.CX.Should().Be(0xC333, $"{binName}: the shared region sets CX");
                });
            case "partition_multi_entry_irreducible_shared":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 10000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1000, $"{binName}: start sets AX to 0x1000 and no call changes it");
                    result.State.CX.Should().Be(0x0001, $"{binName}: the last root entry leaves CX = 1");
                });
            case "partition_cross_function_loop":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 10000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1000, $"{binName}: start sets AX to 0x1000 and no call changes it");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the cross-function loop burns CX down to 0");
                });
            case "partition_mixed_activation_cycle":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 10000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1000, $"{binName}: start sets AX to 0x1000 and no call changes it");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the activation cycle burns CX down to 0");
                });
            case "partition_indirect_call_jump":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.AX.Should().Be(0x1111, $"{binName}: indirect_target sets AX");
                    result.State.BX.Should().Be(0x0016, $"{binName}: BX holds the address of jump_target");
                });
            default:
                throw new ArgumentOutOfRangeException(nameof(binName), binName, "Unknown partition fixture.");
        }
    }

    /// <summary>Runs partition_mutual_recursion_unwind: both mutually recursive functions reached their done marker.</summary>
    public static AsmFixture PartitionMutualRecursionUnwind() {
        byte[] expectedMemory = new byte[0x12];
        expectedMemory[0x10] = 0xAA; // function_a_done marker
        expectedMemory[0x11] = 0xBB; // function_b_done marker
        AsmRunSettings settings = new() {
            BinName = "partition_mutual_recursion_unwind",
            MaxCycles = 10000
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs selfmodifycalltarget: every self-modified call-target variant reached its marker.</summary>
    public static AsmFixture SelfModifyCallTarget() {
        byte[] expectedMemory = new byte[0x13];
        expectedMemory[0x10] = 0xAA; // variant A marker
        expectedMemory[0x11] = 0xBB; // variant B marker
        expectedMemory[0x12] = 0xCC; // variant C marker
        AsmRunSettings settings = new() {
            BinName = "selfmodifycalltarget",
            MaxCycles = 20000
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs multimisalignedcall: the dispatcher ran three times and every continuation wrote its marker.</summary>
    public static AsmFixture MultiMisalignedCall() {
        byte[] expectedMemory = new byte[0x13];
        expectedMemory[0x00] = 0x03; // counter reached 3 (three dispatcher runs)
        expectedMemory[0x10] = 0xAA; // cont0 marker
        expectedMemory[0x11] = 0xBB; // cont1 marker
        expectedMemory[0x12] = 0xCC; // cont2 marker
        AsmRunSettings settings = new() {
            BinName = "multimisalignedcall",
            MaxCycles = 10000
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs pushcs_callnear_retfar: AX holds the value the program stored.</summary>
    public static AsmFixture PushCsCallNearRetFar() {
        byte[] expectedMemory = new byte[2];
        expectedMemory[0x00] = 0x42; // AX low byte = 0x42
        expectedMemory[0x01] = 0x00; // AX high byte = 0x00
        AsmRunSettings settings = new() {
            BinName = "pushcs_callnear_retfar",
            MaxCycles = 1000
        };
        return MemoryFixture(settings, expectedMemory);
    }

    /// <summary>Runs jump1 with speculative exploration off: the recorded memory dump of jump1.</summary>
    public static AsmFixture Jump1WithoutSpeculation() {
        AsmFixture fixture = Dump("jump1");
        AsmRunSettings settings = fixture.Settings with { EnableSpeculativeCfgExploration = false };
        return new AsmFixture(settings, fixture.ExpectedMemory, fixture.AssertResult);
    }

    /// <summary>Runs the observed arm of speculative_branch with speculative exploration off: 01 DD AA.</summary>
    public static AsmFixture SpeculativeBranchWithoutSpeculation() {
        AsmFixture fixture = SpeculativeBranch(SpeculativeArm.Observed);
        AsmRunSettings settings = fixture.Settings with { EnableSpeculativeCfgExploration = false };
        return new AsmFixture(settings, fixture.ExpectedMemory, fixture.AssertResult);
    }

    /// <summary>Runs intchain with interrupt vectors installed: the INT 8 handler increments the BIOS tick counter once.</summary>
    public static AsmFixture IntChain() {
        AsmRunSettings settings = new() {
            BinName = Path.GetFullPath("Resources/cpuTests/intchain.com"),
            MaxCycles = 1000,
            InstallInterruptVectors = true
        };
        return ResultFixture(settings, result =>
            result.Memory.UInt32[0x046C].Should().Be(1u, "intchain: the INT 8 handler increments the BIOS tick counter once"));
    }

    /// <summary>Runs ownerlesscallcycle: the rescued cycle turned the counter byte and AL to 3.</summary>
    public static AsmFixture OwnerlessCallCycle() {
        return ResultFixture(new AsmRunSettings { BinName = "ownerlesscallcycle", MaxCycles = 1000 }, result => {
            result.Memory.UInt8[0xF000, 0x0025].Should().Be(3, "ownerlesscallcycle: the counter byte in the code segment counts the loop runs");
            result.State.AL.Should().Be(3, "ownerlesscallcycle: AL holds the last counter value read");
            result.State.SS.Should().Be(0, "ownerlesscallcycle: start sets SS to 0");
            result.State.SP.Should().Be(0x0100, "ownerlesscallcycle: start sets SP to 0x0100");
        });
    }

    /// <summary>Runs test386 until its POST port reports the eight values of a normally finished test.</summary>
    public static AsmFixture Test386() {
        Test386PostPortHandler? handler = null;
        AsmRunSettings settings = new() {
            BinName = "test386",
            MaxCycles = long.MaxValue,
            FailOnUnhandledPort = true,
            ConfigureMachine = machine => {
                handler = new Test386PostPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher);
            }
        };
        return ResultFixture(settings, result => {
            Test386PostPortHandler postHandler = handler
                ?? throw new InvalidOperationException("The test386 POST port handler was not installed.");
            // 8 POST values, the last one 0xFF ("test finished normally").
            string postValues = string.Join(", ", postHandler.PostValues);
            string context = $"POST values [{postValues}], ascii error [{postHandler.AsciiError}]";
            postHandler.PostValues.Count.Should().Be(8, context);
            postHandler.PostValues.Last().Should().Be(0xFF, context);
        });
    }

    private const uint HookCountAddress = 0x0400;
    private const uint TailCountAddress = 0x0401;
    private const uint CompletedAddress = 0x0402;
    private const uint ObserverCountAddress = 0x040C;
    private const uint BadStackAddress = 0x040D;
    private const uint ObservedSsAddress = 0x040E;
    private const uint ObservedSpAddress = 0x0410;

    /// <summary>Shared completion and stack-safety assertions for the interior-entry IRQ fixture.</summary>
    /// <remarks>Requires all three phases to finish without an IRQ observing an incomplete stack switch.</remarks>
    private static void AssertInteriorEntryCompleted(AsmFixtureResult result) {
        result.Memory.UInt8[HookCountAddress].Should().Be(2);
        result.Memory.UInt8[TailCountAddress].Should().Be(3);
        result.Memory.UInt8[ObserverCountAddress].Should().Be(1);
        result.Memory.UInt8[CompletedAddress].Should().Be(0xA5);
        result.State.SS.Should().Be(0);
        result.State.SP.Should().Be(0x8000);
        result.Memory.UInt8[BadStackAddress].Should().Be(0,
            $"the IRQ must see a complete stack; observed SS:SP was {result.Memory.UInt16[ObservedSsAddress]:X4}:{result.Memory.UInt16[ObservedSpAddress]:X4}");
    }

    private static AsmFixture MemoryFixture(AsmRunSettings settings, byte[] expectedMemory) {
        return new AsmFixture(settings, expectedMemory, AsmFixture.NoResultAssertion);
    }

    private static AsmFixture ResultFixture(AsmRunSettings settings, Action<AsmFixtureResult> assertResult) {
        return new AsmFixture(settings, [], assertResult);
    }

    private static byte[] ReadMemoryDump(string binName) {
        return File.ReadAllBytes($"Resources/cpuTests/res/MemoryDumps/{binName}.bin");
    }

    private static AsmFixture SpeculativeFixture(string binName, byte[] expectedMemory, SpeculativeArm arm) {
        AsmRunSettings settings = new() { BinName = binName, MaxCycles = 1000 };
        if (arm == SpeculativeArm.Observed) {
            return MemoryFixture(settings, expectedMemory);
        }
        settings = settings with { ConfigureMachine = SelectUnobservedArm };
        return new AsmFixture(settings, expectedMemory, AsmFixture.NoResultAssertion) { MachineGoldenVariant = "unobserved" };
    }

    private static byte[] SpeculativeMemory(byte b400, byte b401, byte b402) {
        byte[] expectedMemory = new byte[0x403];
        expectedMemory[0x400] = b400;
        expectedMemory[0x401] = b401;
        expectedMemory[0x402] = b402;
        return expectedMemory;
    }
}
