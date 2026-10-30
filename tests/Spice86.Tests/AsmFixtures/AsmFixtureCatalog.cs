namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.VM;
using Spice86.Core.CLI;

/// <summary>One method per fixture scenario. Every call returns a new fixture, so per-run state stays private.</summary>
internal static class AsmFixtureCatalog {
    /// <summary>Runs byte-string and word writes to mapped ROM without changing its contents.</summary>
    public static AsmFixture RomWrite() {
        byte[] originalRom = [];
        AsmRunSettings settings = new() {
            BinName = "romwrite",
            MaxCycles = 1000,
            ConfigureMachine = machine => {
                VgaRom rom = new(NullLogger.Instance);
                originalRom = machine.Memory.ReadRam(rom.Size, VgaRom.Segment << 4);
            }
        };
        return ResultFixture(settings, result => {
            result.Memory.ReadRam((uint)originalRom.Length, VgaRom.Segment << 4).Should().Equal(originalRom);
            result.State.SI.Should().Be(0x0104);
            result.State.DI.Should().Be(0x9194);
            result.State.CX.Should().Be(0);
            result.State.DS.Should().Be(0x1000);
            result.State.ES.Should().Be(0xB800);
            result.State.AX.Should().Be(0x600D);
        });
    }

    /// <summary>Runs synthetic Flashback-style unchained scanout or standard BIOS mode 13h.</summary>
    public static AsmFixture FlashbackVideo(bool unchained) {
        return FlashbackVideo(unchained, 0, false, true);
    }

    /// <summary>Runs a Flashback-style page with logical panning and an optional split below its first row.</summary>
    public static AsmFixture FlashbackVideoPanning(byte panning, bool splitScreen, bool alternatePage) {
        return FlashbackVideo(true, panning, splitScreen, !alternatePage);
    }

    private static AsmFixture FlashbackVideo(bool unchained, byte panning, bool splitScreen, bool mainPage) {
        string binName = unchained ? "flashback_video" : "flashback_mode13";
        return ResultFixture(new AsmRunSettings {
            BinName = binName,
            InstallInterruptVectors = true,
            MaxCycles = 2000000,
            RenderingMode = RenderingMode.Sync,
            AsmHandlersSegment = 0xD000,
            ConfigureMachine = machine => {
                machine.Memory.UInt8[0, 0x0500] = panning;
                machine.Memory.UInt8[0, 0x0501] = splitScreen ? (byte)0x01 : (byte)0x41;
                machine.Memory.UInt8[0, 0x0502] = splitScreen ? (byte)0x01 : (byte)0xFF;
                machine.Memory.UInt8[0, 0x0503] = mainPage ? (byte)0x3A : (byte)0;
                machine.Memory.UInt8[0, 0x0504] = splitScreen ? (byte)0x2E : (byte)0x3E;
            }
        }, result => {
            uint[] frame = result.CaptureVideoFrame();
            int width = unchained ? 256 : 320;
            int height = unchained ? 224 : 200;
            result.VideoRenderer.Width.Should().Be(width);
            result.VideoRenderer.Height.Should().Be(height);
            frame.Length.Should().Be(width * height);
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y + x;
                    if (unchained) {
                        int pan = (panning & 7) / 2;
                        int sourceRow = y;
                        int pageOffset = mainPage ? 1 : 65;
                        if (splitScreen && y > 0) {
                            sourceRow = y - 1;
                            pan = 0;
                            pageOffset = 65;
                        }
                        int sourcePixel = x + pan;
                        index = sourceRow + sourcePixel / 256 + sourcePixel % 256 / 4
                            + pageOffset + (1 << (sourcePixel & 3));
                    }
                    uint color = result.VideoState.DacRegisters.PaletteMap[(byte)index];
                    frame[y * width + x].Should().Be(color, $"pixel ({x}, {y})");
                }
            }
        });
    }

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

    /// <summary>Runs flagconditions: each conditional jump case records 1 when taken and 2 when not, each value case
    /// records its value, once per input pair; the region holds pass p at offset 0x40 * p.</summary>
    public static AsmFixture FlagConditions() {
        byte[] expectedMemory = [
            0x01, 0x02, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02,
            0x01, 0x02, 0x01, 0x01, 0x01, 0x02, 0x02, 0x01,
            0x01, 0x01, 0x02, 0x02, 0x01, 0x02, 0x01, 0x01,
            0x01, 0x01, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x02, 0x01, 0x01, 0x00, 0x02, 0x03, 0x00,
            0xFF, 0xFF, 0x02, 0x00, 0x01, 0x00, 0xFF, 0xFF,
            0x03, 0x00, 0x06, 0x00, 0x03, 0x07, 0x09, 0x04,

            0x02, 0x01, 0x01, 0x02, 0x01, 0x02, 0x02, 0x01,
            0x01, 0x02, 0x02, 0x02, 0x02, 0x01, 0x02, 0x02,
            0x02, 0x02, 0x01, 0x01, 0x02, 0x02, 0x01, 0x02,
            0x02, 0x02, 0x01, 0x02, 0x00, 0x02, 0x01, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x02, 0x02, 0x00, 0x02, 0x01, 0x01, 0x04, 0x00,
            0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0xFE, 0xFF,
            0x02, 0x00, 0x07, 0x00, 0x04, 0x07, 0x09, 0x03,

            0x02, 0x01, 0x02, 0x01, 0x02, 0x01, 0x01, 0x02,
            0x01, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x01, 0x02, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x00, 0x01, 0x01, 0x01,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x02, 0x01, 0x01, 0x01, 0x00, 0x01, 0x01, 0x80,
            0xFF, 0x7F, 0x01, 0x80, 0x00, 0x00, 0x00, 0x80,
            0x01, 0x80, 0x05, 0x01, 0x02, 0x07, 0x09, 0x02,

            0x01, 0x02, 0x02, 0x01, 0x01, 0x02, 0x02, 0x01,
            0x02, 0x01, 0x01, 0x02, 0x01, 0x02, 0x02, 0x01,
            0x01, 0x01, 0x02, 0x02, 0x01, 0x02, 0x01, 0x01,
            0x01, 0x01, 0x02, 0x01, 0x01, 0x01, 0x02, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x02, 0x01, 0x01, 0x00, 0x02, 0x01, 0x80,
            0x01, 0x80, 0x02, 0x00, 0xFF, 0x7F, 0xFF, 0xFF,
            0x01, 0x80, 0x06, 0x00, 0x03, 0x07, 0x09, 0x01,
        ];
        AsmRunSettings settings = new() {
            BinName = "flagconditions",
            EnableSpeculativeCfgExploration = false
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
        expectedMemory[0x401] = arm == SpeculativeArm.Unobserved ? (byte)0xBB : (byte)0xAA;
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

    /// <summary>Runs callstackunwind on the given arm: the observed arm returns leaf to recovery, while the
    /// unobserved arm's leaf discards its immediate return and returns straight to inner_return.</summary>
    /// <param name="arm">Selector arm of the program.</param>
    /// <returns>The fixture for that arm.</returns>
    public static AsmFixture CallStackUnwind(SpeculativeArm arm) {
        byte[] expectedMemory = new byte[0x13];
        expectedMemory[0x10] = 0xAA;
        expectedMemory[0x11] = 0xBB;
        expectedMemory[0x12] = arm == SpeculativeArm.Unobserved ? (byte)0xEE : (byte)0xDD;
        return SpeculativeFixture("callstackunwind", expectedMemory, arm);
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
            case "partition_jump_table":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0111, $"{binName}: each of the three cases adds its own digit to DX");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the loop runs the three table entries");
                    result.State.BX.Should().Be(0x0000, $"{binName}: the last iteration jumps through table entry 0");
                });
            case "partition_untested_indirect":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.CX.Should().Be(0x0002, $"{binName}: the indirect jump and call are never taken");
                });
            case "partition_jump_table_mixed":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0011, $"{binName}: case zero and case one each add their digit; the third entry jumps straight to the join");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the loop runs the three table entries");
                    result.State.BX.Should().Be(0x0000, $"{binName}: the last iteration jumps through table entry 0");
                });
            case "partition_far_dispatch":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x1111, $"{binName}: each far call and far jump target adds its own digit");
                    result.State.CX.Should().Be(0x0000, $"{binName}: both loops run their two table entries");
                });
            case "partition_far_call_chain":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 2000 }, result => {
                    AssertResultRegion(result, binName, ResultRegion(0x20,
                        (0x00, 2, 0xF001), (0x02, 2, 0x3000),
                        (0x04, 2, 0xF000), (0x06, 2, 0x3000),
                        (0x08, 2, 0xF010), (0x0A, 2, 0x2000),
                        (0x0C, 2, 0xF000), (0x0E, 2, 0x1000)));
                    result.State.DX.Should().Be(0x1111, $"{binName}: each of the four original far call targets adds its own digit");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the loop runs the eight table entries");
                });
            case "partition_far_jump_segment_change":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x40,
                    (0x00, 2, 0xAAAA), (0x02, 2, 0xAAAA), (0x04, 2, 0xF100),
                    (0x06, 2, 0xBBBB), (0x08, 2, 0xBBBB), (0x0A, 2, 0xF100),
                    (0x0C, 2, 0xF100)));
            case "partition_sign_conditions":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0110, $"{binName}: only the negative compare and the negative test add their digit");
                });
            case "partition_untested_far_indirect":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.CX.Should().Be(0x0002, $"{binName}: the far indirect jump and call are never taken");
                });
            case "partition_untaken_cpuid":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.CX.Should().Be(0x0002, $"{binName}: cpuid is never executed");
                });
            case "partition_signed_min_compare":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0001, $"{binName}: 1 is not below int.MinValue, and int.MinValue is below or equal to itself");
                    result.State.AX.Should().Be(0x0000, $"{binName}: the end clears AX");
                });
            case "partition_back_edge_fallthrough":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0003, $"{binName}: the loop body runs three times");
                    result.State.CX.Should().Be(0x0000, $"{binName}: the latch leaves the loop when CX reaches zero");
                });
            case "partition_jump_to_next":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: start sets SP to 0x0100");
                    result.State.DX.Should().Be(0x0001, $"{binName}: both arms of the jump reach the next instruction");
                });
            case "partition_two_fault_handlers":
                return ResultFixture(new AsmRunSettings { BinName = binName, MaxCycles = 1000 }, result => {
                    result.State.SS.Should().Be(0x1000, $"{binName}: start sets SS to 0x1000");
                    result.State.SP.Should().Be(0x0100, $"{binName}: each handler returns with iret");
                    result.State.SI.Should().Be(0x0011, $"{binName}: the first and the second handler each run once");
                    result.State.AX.Should().Be(0x0002, $"{binName}: the third division succeeds");
                    result.State.BX.Should().Be(0x0005, $"{binName}: the second handler sets the divisor");
                });
            case "dataflow_stack_save_restore":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 2, 0x1234), (0x02, 2, 0x3000), (0x04, 2, 0x0002), (0x06, 2, 0x0103), (0x08, 2, 0x0100),
                    (0x0A, 2, 0xBEEF), (0x0C, 2, 0x0FFE), (0x0E, 2, 0x5A5A), (0x10, 2, 0x6B6B), (0x12, 2, 0x2222),
                    (0x14, 2, 0x0000), (0x16, 2, 0x4000), (0x18, 2, 0x0011),
                    (0x40, 2, 0xFFFF), (0x42, 2, 0x5000), (0x44, 2, 0x0002), (0x46, 2, 0x0001), (0x48, 2, 0xFFFE),
                    (0x4A, 2, 0x0001), (0x4C, 2, 0x0FFE), (0x4E, 2, 0x5A5A), (0x50, 2, 0x6B6B), (0x52, 2, 0x0000),
                    (0x54, 2, 0x4444), (0x56, 2, 0x6000), (0x58, 2, 0x0020)));
            case "dataflow_flag_definitions":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x100,
                    (0x00, 1, 1), (0x02, 2, 0x0004), (0x04, 1, 1), (0x05, 1, 1), (0x06, 2, 0x0002), (0x08, 1, 2),
                    (0x0A, 2, 0x0095), (0x0C, 1, 1), (0x0E, 2, 0x1000), (0x10, 2, 0x0003),
                    (0x40, 1, 1), (0x42, 2, 0x0000), (0x44, 1, 1), (0x45, 1, 2), (0x46, 2, 0x0005), (0x48, 1, 1),
                    (0x4A, 2, 0x0004), (0x4C, 1, 2), (0x4E, 2, 0x1000), (0x50, 2, 0x0007),
                    (0x80, 1, 2), (0x82, 2, 0x1235), (0x84, 1, 2), (0x85, 1, 2), (0x86, 2, 0x8009), (0x88, 1, 2),
                    (0x8A, 2, 0x0044), (0x8C, 1, 2), (0x8E, 2, 0x1001), (0x90, 2, 0x0012),
                    (0xC0, 1, 2), (0xC2, 2, 0x0001), (0xC4, 1, 2), (0xC5, 1, 2), (0xC6, 2, 0x0000), (0xC8, 1, 2),
                    (0xCA, 2, 0x0084), (0xCC, 1, 2), (0xCE, 2, 0x0000), (0xD0, 2, 0xFFFF)));
            case "dataflow_partial_registers":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 2, 0x12CD), (0x02, 2, 0xAB34), (0x04, 4, 0x89AB1234), (0x08, 2, 0xCDAB), (0x0A, 2, 0x0735),
                    (0x0C, 2, 0xAB01), (0x0E, 2, 0x1234), (0x10, 2, 0x2200), (0x12, 2, 0xABCD), (0x14, 2, 0x2200),
                    (0x40, 2, 0x0000), (0x42, 2, 0xFFFF), (0x44, 4, 0x000000FF), (0x48, 2, 0x00FF), (0x4A, 2, 0x0700),
                    (0x4C, 2, 0xFF01), (0x4E, 2, 0x00FF), (0x50, 2, 0x2200), (0x52, 2, 0xFF00), (0x54, 2, 0x2200)));
            case "dataflow_constant_folding":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x100,
                    (0x00, 2, 0xA1A1), (0x02, 2, 0x00F4), (0x04, 1, 2), (0x05, 1, 2), (0x06, 1, 1), (0x07, 1, 2),
                    (0x08, 2, 0x1357), (0x0A, 2, 0x2468), (0x0C, 2, 0xA1A1), (0x0E, 2, 0x5A5A), (0x10, 2, 0x6B6B),
                    (0x40, 2, 0xC3C3), (0x42, 2, 0x007F), (0x44, 1, 1), (0x45, 1, 1), (0x46, 1, 1), (0x47, 1, 2),
                    (0x48, 2, 0x1357), (0x4A, 2, 0x2468), (0x4C, 2, 0xA1A1), (0x4E, 2, 0x5A5A), (0x50, 2, 0x6B6B),
                    (0x80, 2, 0xA100), (0x82, 2, 0xFFFE), (0x84, 1, 2), (0x85, 1, 2), (0x86, 1, 1), (0x87, 1, 1),
                    (0x88, 2, 0x1357), (0x8A, 2, 0x2468), (0x8C, 2, 0xA1A1), (0x8E, 2, 0x5A5A), (0x90, 2, 0x6B6B),
                    (0xC0, 2, 0xC3C3), (0xC2, 2, 0x7FFF), (0xC4, 1, 2), (0xC5, 1, 1), (0xC6, 1, 2), (0xC7, 1, 2),
                    (0xC8, 2, 0x1357), (0xCA, 2, 0x2468), (0xCC, 2, 0xA1A1), (0xCE, 2, 0x5A5A), (0xD0, 2, 0x6B6B)));
            case "dataflow_double_precision":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x100,
                    (0x00, 2, 0x0000), (0x02, 2, 0x0001), (0x04, 2, 0xFFFE), (0x06, 2, 0x0000), (0x08, 2, 0x0001),
                    (0x0A, 2, 0xFFFF), (0x0C, 2, 0xFFFE), (0x0E, 2, 0x0001), (0x10, 2, 0x7FFF), (0x12, 2, 0x0000),
                    (0x40, 2, 0x0001), (0x42, 2, 0x0001), (0x44, 2, 0xFFFF), (0x46, 2, 0x0000), (0x48, 2, 0x0000),
                    (0x4A, 2, 0xFFFF), (0x4C, 2, 0x0000), (0x4E, 2, 0x0002), (0x50, 2, 0x8000), (0x52, 2, 0x0000),
                    (0x80, 2, 0xFFFF), (0x82, 2, 0x7FFF), (0x84, 2, 0x0001), (0x86, 2, 0x8000), (0x88, 2, 0x0000),
                    (0x8A, 2, 0x8000), (0x8C, 2, 0x0000), (0x8E, 2, 0x0000), (0x90, 2, 0x0000), (0x92, 2, 0x4000),
                    (0xC0, 2, 0x7FFF), (0xC2, 2, 0x1235), (0xC4, 2, 0x8001), (0xC6, 2, 0x1233), (0xC8, 2, 0x8000),
                    (0xCA, 2, 0xEDCB), (0xCC, 2, 0x0000), (0xCE, 2, 0x2469), (0xD0, 2, 0x4000), (0xD2, 2, 0x091A)));
            case "dataflow_idioms":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 2, 0xABCD), (0x02, 2, 0x1234), (0x04, 2, 0x1234), (0x06, 2, 0xFF80), (0x08, 2, 0x0000),
                    (0x0A, 2, 0x0080), (0x0C, 2, 0x5678), (0x0E, 2, 0x9ABC), (0x10, 2, 0x1234), (0x12, 2, 0x0000),
                    (0x14, 2, 0xABCD), (0x16, 2, 0x0000), (0x18, 2, 0x1239),
                    (0x40, 2, 0x0000), (0x42, 2, 0xFFFB), (0x44, 2, 0xFFFB), (0x46, 2, 0x007F), (0x48, 2, 0xFFFF),
                    (0x4A, 2, 0xFF7F), (0x4C, 2, 0x0001), (0x4E, 2, 0x0002), (0x50, 2, 0xFFFB), (0x52, 2, 0x0000),
                    (0x54, 2, 0x0000), (0x56, 2, 0x0000), (0x58, 2, 0x0000)));
            case "alu_dead_flags":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x200,
                    (0x000, 2, 0x0000), (0x002, 2, 0x4000), (0x004, 2, 0xC000), (0x006, 2, 0x0000), (0x008, 2, 0x0000),
                    (0x00A, 2, 0xFFFF), (0x00C, 2, 0x8000), (0x00E, 2, 0x2000), (0x010, 2, 0x8000), (0x012, 2, 0x8000),
                    (0x014, 2, 0x0800), (0x016, 2, 0x0001), (0x018, 2, 0xC000), (0x01A, 2, 0x8006), (0x01C, 2, 0x7FFA),
                    (0x01E, 2, 0x8000), (0x020, 2, 0x0001), (0x022, 2, 0x8000), (0x024, 2, 0x0001), (0x026, 2, 0x8000),
                    (0x028, 2, 0x8000), (0x02A, 1, 0x00), (0x02B, 1, 0x00), (0x02C, 4, 0x10000000), (0x030, 4, 0xFFFFFFFF),
                    (0x034, 2, 0x8000), (0x036, 2, 0x8001), (0x038, 2, 0x8001), (0x03A, 2, 0x8000), (0x03C, 2, 0x8000),
                    (0x03E, 1, 2), (0x03F, 1, 1), (0x040, 1, 1), (0x041, 1, 1), (0x042, 1, 2),
                    (0x080, 2, 0x0002), (0x082, 2, 0x0000), (0x084, 2, 0x0000), (0x086, 2, 0x0010), (0x088, 2, 0x0000),
                    (0x08A, 2, 0x0000), (0x08C, 2, 0x0000), (0x08E, 2, 0x0000), (0x090, 2, 0x0001), (0x092, 2, 0x0001),
                    (0x094, 2, 0x1000), (0x096, 2, 0x0002), (0x098, 2, 0x0000), (0x09A, 2, 0x0006), (0x09C, 2, 0xFFFC),
                    (0x09E, 2, 0x0003), (0x0A0, 2, 0x0000), (0x0A2, 2, 0xFFFD), (0x0A4, 2, 0xFFFF), (0x0A6, 2, 0x0007),
                    (0x0A8, 2, 0xFFFF), (0x0AA, 1, 0x02), (0x0AB, 1, 0x00), (0x0AC, 4, 0x02468ACF), (0x0B0, 4, 0x00000000),
                    (0x0B4, 2, 0x0001), (0x0B6, 2, 0x0002), (0x0B8, 2, 0x0002), (0x0BA, 2, 0x0001), (0x0BC, 2, 0x0001),
                    (0x0BE, 1, 1), (0x0BF, 1, 2), (0x0C0, 1, 1), (0x0C1, 1, 2), (0x0C2, 1, 1),
                    (0x100, 2, 0x0000), (0x102, 2, 0x0000), (0x104, 2, 0x0000), (0x106, 2, 0x0000), (0x108, 2, 0x0000),
                    (0x10A, 2, 0x0000), (0x10C, 2, 0x0000), (0x10E, 2, 0x0000), (0x110, 2, 0x0000), (0x112, 2, 0x0000),
                    (0x114, 2, 0x0000), (0x116, 2, 0x0001), (0x118, 2, 0x8000), (0x11A, 2, 0x0006), (0x11C, 2, 0xFFFA),
                    (0x11E, 2, 0x0000), (0x120, 2, 0x0000), (0x122, 2, 0x0000), (0x124, 2, 0x0000), (0x126, 2, 0x0000),
                    (0x128, 2, 0x0000), (0x12A, 1, 0x00), (0x12B, 1, 0x00), (0x12C, 4, 0x1FFFFFFF), (0x130, 4, 0xFFFFFFFF),
                    (0x134, 2, 0x0000), (0x136, 2, 0x0001), (0x138, 2, 0x0001), (0x13A, 2, 0x0000), (0x13C, 2, 0x0000),
                    (0x13E, 1, 2), (0x13F, 1, 2), (0x140, 1, 2), (0x141, 1, 2), (0x142, 1, 1),
                    (0x180, 2, 0xFFFE), (0x182, 2, 0x3FFF), (0x184, 2, 0x3FFF), (0x186, 2, 0xFFF0), (0x188, 2, 0x0000),
                    (0x18A, 2, 0x0000), (0x18C, 2, 0x07FF), (0x18E, 2, 0x1FFF), (0x190, 2, 0x7FFF), (0x192, 2, 0x7FFF),
                    (0x194, 2, 0xF7FF), (0x196, 2, 0xFFFE), (0x198, 2, 0x3FFF), (0x19A, 2, 0x8004), (0x19C, 2, 0x7FFA),
                    (0x19E, 2, 0x7FFD), (0x1A0, 2, 0x0001), (0x1A2, 2, 0x8003), (0x1A4, 2, 0xFFFE), (0x1A6, 2, 0x7FF9),
                    (0x1A8, 2, 0x8001), (0x1AA, 1, 0xFE), (0x1AB, 1, 0xFF), (0x1AC, 4, 0x00000000), (0x1B0, 4, 0x00000000),
                    (0x1B4, 2, 0x7FFF), (0x1B6, 2, 0x8000), (0x1B8, 2, 0x8000), (0x1BA, 2, 0x7FFF), (0x1BC, 2, 0x7FFF),
                    (0x1BE, 1, 1), (0x1BF, 1, 2), (0x1C0, 1, 1), (0x1C1, 1, 2), (0x1C2, 1, 2)));
            case "dataflow_expression_propagation":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 2, 0x0000), (0x02, 2, 0x0000), (0x04, 2, 0x0000), (0x06, 2, 0x0000), (0x08, 1, 0x00),
                    (0x0A, 2, 0x0000), (0x0C, 2, 0xABCD), (0x0E, 2, 0xABCE), (0x10, 2, 0xFFFF), (0x12, 2, 0xABCD),
                    (0x14, 2, 0xFFFF), (0x16, 2, 0x7777), (0x18, 2, 0x1111), (0x1A, 2, 0x2222), (0x1C, 2, 0xABCC),
                    (0x1E, 2, 0xABCC), (0x20, 2, 0x0005), (0x22, 2, 0xAB01), (0x24, 2, 0xFFFF), (0x26, 2, 0x2200),
                    (0x28, 1, 2), (0x2A, 2, 0x55E7), (0x2C, 2, 0x0001), (0x2E, 2, 0x0005), (0x30, 2, 0x0001),
                    (0x40, 2, 0x4000), (0x42, 2, 0x4000), (0x44, 2, 0x4000), (0x46, 2, 0x0000), (0x48, 1, 0x49),
                    (0x4A, 2, 0xC000), (0x4C, 2, 0x00FE), (0x4E, 2, 0x00FF), (0x50, 2, 0x7FFF), (0x52, 2, 0x00FE),
                    (0x54, 2, 0x7FFF), (0x56, 2, 0x7777), (0x58, 2, 0x1111), (0x5A, 2, 0x2222), (0x5C, 2, 0x80FD),
                    (0x5E, 2, 0x80FD), (0x60, 2, 0x0005), (0x62, 2, 0x0001), (0x64, 2, 0x7FFF), (0x66, 2, 0x2200),
                    (0x68, 1, 1), (0x6A, 2, 0x007F), (0x6C, 2, 0x8001), (0x6E, 2, 0x0005), (0x70, 2, 0x0001)));
            case "dataflow_interrupt_boundaries":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName, MaxCycles = 0xFFFFFFF, EnablePit = true }, ResultRegion(0x80,
                    (0x00, 2, 0x0010), (0x02, 2, 0x0004), (0x04, 2, 0x0007), (0x06, 2, 0x0064), (0x08, 2, 0x0E16),
                    (0x0A, 2, 0x1003), (0x0C, 2, 0x0000), (0x0E, 2, 0x1000), (0x10, 2, 0x1002), (0x12, 2, 0x7708),
                    (0x14, 2, 0x0E1E), (0x16, 2, 0x0E2E), (0x18, 2, 0x0E42), (0x1A, 2, 0xBEEF),
                    (0x40, 2, 0x0005), (0x42, 2, 0x0000), (0x44, 2, 0x0004), (0x46, 2, 0x0000), (0x48, 2, 0x0E16),
                    (0x4A, 2, 0x0002), (0x4C, 2, 0x0000), (0x4E, 2, 0xFFFF), (0x50, 2, 0x0001), (0x52, 2, 0x7702),
                    (0x54, 2, 0x0E1E), (0x56, 2, 0x0E2E), (0x58, 2, 0x0E42), (0x5A, 2, 0x0102)));
            case "dataflow_rewrite_traps":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 4, 0x89ABCDEF), (0x04, 4, 0x00001234), (0x08, 2, 0x0005), (0x0A, 1, 0x00),
                    (0x0C, 2, 0x0000), (0x0E, 2, 0x0001),
                    (0x40, 4, 0x11223344), (0x44, 4, 0x00005678), (0x48, 2, 0x9905), (0x4A, 1, 0x99),
                    (0x4C, 2, 0x0001), (0x4E, 2, 0x0000)));
            case "dataflow_legality_traps":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x80,
                    (0x00, 2, 0x0001), (0x02, 2, 0x00EF), (0x04, 2, 0x0004), (0x08, 1, 0x01),
                    (0x0A, 1, 0x97), (0x0C, 2, 0x0001), (0x0E, 2, 0x0002), (0x10, 2, 0x0001),
                    (0x12, 2, 0xBEEF), (0x14, 2, 0x0000), (0x16, 1, 0x11), (0x18, 2, 0x0012),
                    (0x06, 2, 0x0041), (0x1A, 2, 0xBBBB), (0x1E, 2, 0x00A1), (0x22, 2, 0x0000),
                    (0x26, 2, 0x3344), (0x28, 2, 0x0042), (0x2A, 2, 0x0E6F), (0x2C, 2, 0x00A5),
                    (0x2E, 2, 0x1234), (0x38, 2, 0x1234), (0x3A, 2, 0x0007),
                    (0x3C, 4, 0x12340009), (0x30, 4, 0xAABB0077),
                    (0x40, 2, 0x0002), (0x42, 2, 0x01CD), (0x44, 2, 0x0008), (0x48, 1, 0x00),
                    (0x4A, 1, 0x02), (0x4C, 2, 0x0001), (0x4E, 2, 0x0000), (0x50, 2, 0x0001),
                    (0x52, 2, 0xBEEF), (0x54, 2, 0x0001), (0x56, 1, 0x99), (0x58, 2, 0x0012),
                    (0x46, 2, 0x0055), (0x5A, 2, 0xBBBB), (0x5E, 2, 0x00C3), (0x62, 2, 0x0001),
                    (0x66, 2, 0x3344), (0x68, 2, 0x0042), (0x6A, 2, 0x0E6F), (0x6C, 2, 0x00A5),
                    (0x6E, 2, 0x1234), (0x78, 2, 0x1234), (0x7A, 2, 0x0007),
                    (0x74, 2, 0x0000), (0x76, 2, 0x0076),
                    (0x7C, 4, 0x12340009), (0x70, 4, 0xAABB0077)));
            case "instruction_coverage_386":
                return ResultRegionFixture(new AsmRunSettings { BinName = binName }, ResultRegion(0x50,
                    (0x00, 1, 1), (0x01, 1, 0), (0x02, 1, 1), (0x03, 1, 0), (0x04, 2, 0x01E1), (0x06, 2, 0x0009),
                    (0x08, 2, 0x000B), (0x0A, 2, 0x234A), (0x0C, 2, 0xD123), (0x0E, 2, 0x000C), (0x10, 2, 0x0005),
                    (0x12, 2, 0x0009), (0x14, 2, 0x0009), (0x16, 4, 0x78563412), (0x1A, 2, 0x0004), (0x1C, 2, 0x0000),
                    (0x1E, 2, 0x1234), (0x20, 2, 0x0001), (0x22, 2, 0x0085), (0x24, 2, 0xFF85), (0x26, 4, 0xFFFF8001),
                    (0x2A, 4, 0x00008001), (0x2E, 4, 0xFFFF8000), (0x32, 4, 0xFFFFFFFF), (0x36, 2, 0x0001),
                    (0x38, 2, 0x0002), (0x3A, 2, 0x0003), (0x3C, 2, 0x1000), (0x3E, 2, 0x0005),
                    (0x40, 1, 1), (0x41, 1, 0), (0x42, 1, 1), (0x43, 1, 0), (0x44, 1, 1), (0x45, 1, 0), (0x46, 1, 1),
                    (0x47, 1, 0), (0x48, 1, 1), (0x49, 1, 0), (0x4A, 1, 0), (0x4B, 1, 1), (0x4C, 1, 1), (0x4D, 1, 0)));
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

    /// <summary>Runs sameretloop: the same-address return loop ran four times and the hlt ended the run.</summary>
    public static AsmFixture SameretLoop() {
        return ResultFixture(new AsmRunSettings { BinName = "sameretloop", MaxCycles = 1000 }, result => {
            result.Memory.UInt8[0xF000, 0x0024].Should().Be(4, "sameretloop: the counter byte counts the same-address returns");
            result.State.SS.Should().Be(0, "sameretloop: start sets SS to 0");
            result.State.SP.Should().Be(0x0100, "sameretloop: every loop iteration pushes one word and the ret pops it back");
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

    private const uint ResultRegionAddress = 0x20000;

    /// <summary>
    /// A fixture whose oracle compares the result region at physical 0x20000 with <paramref name="expectedRegion"/>,
    /// listing every differing byte, and checks that the stack at 2000:1000 is balanced at the end.
    /// </summary>
    private static AsmFixture ResultRegionFixture(AsmRunSettings settings, byte[] expectedRegion) {
        return ResultFixture(settings, result => {
            AssertResultRegion(result, settings.BinName, expectedRegion);
            result.State.SS.Should().Be(0x2000, $"{settings.BinName}: start sets SS to 0x2000");
            result.State.SP.Should().Be(0x1000, $"{settings.BinName}: the stack is balanced at the end");
        });
    }

    /// <summary>Asserts that the result region at physical 0x20000 equals <paramref name="expectedRegion"/>,
    /// listing every differing byte.</summary>
    private static void AssertResultRegion(AsmFixtureResult result, string binName, byte[] expectedRegion) {
        byte[] actual = result.Memory.ReadRam((uint)expectedRegion.Length, ResultRegionAddress);
        List<string> differences = Enumerable.Range(0, expectedRegion.Length)
            .Where(i => actual[i] != expectedRegion[i])
            .Select(i => $"[{i:X3}] expected 0x{expectedRegion[i]:X2} actual 0x{actual[i]:X2}")
            .ToList();
        differences.Should().BeEmpty($"{binName}: every byte of the result region at 0x{ResultRegionAddress:X5} is the expected one");
    }

    /// <summary>
    /// An expected result region of <paramref name="length"/> bytes: zero, except for the little-endian values given
    /// as (offset, size in bytes, value).
    /// </summary>
    private static byte[] ResultRegion(int length, params (int Offset, int Size, uint Value)[] values) {
        byte[] region = new byte[length];
        foreach ((int offset, int size, uint value) in values) {
            for (int i = 0; i < size; i++) {
                region[offset + i] = (byte)(value >> (8 * i));
            }
        }
        return region;
    }

    private static byte[] ReadMemoryDump(string binName) {
        return File.ReadAllBytes($"Resources/cpuTests/res/MemoryDumps/{binName}.bin");
    }

    private static AsmFixture SpeculativeFixture(string binName, byte[] expectedMemory, SpeculativeArm arm) {
        AsmRunSettings settings = new() { BinName = binName, MaxCycles = 1000 };
        if (arm == SpeculativeArm.Observed) {
            return MemoryFixture(settings, expectedMemory);
        }
        settings = settings with { ConfigureMachine = SelectUnobservedArm, VariantName = "unobserved" };
        return MemoryFixture(settings, expectedMemory);
    }

    private static byte[] SpeculativeMemory(byte b400, byte b401, byte b402) {
        byte[] expectedMemory = new byte[0x403];
        expectedMemory[0x400] = b400;
        expectedMemory[0x401] = b401;
        expectedMemory[0x402] = b402;
        return expectedMemory;
    }
}
