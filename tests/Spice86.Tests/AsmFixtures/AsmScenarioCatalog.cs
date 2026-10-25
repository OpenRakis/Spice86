namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;

using Spice86.Tests.Dos;
using Spice86.Tests.Utility;

/// <summary>
/// Enumerable catalogue of the runnable ASM test scenarios: the CPU fixtures and the prepared DOS
/// scenarios. Both the interpreted and the generated entry points run a scenario from here through the
/// same preparation, the same oracle and the same golden comparisons. A scenario owns its identity,
/// its resource dependencies, its fresh-run preparation and one oracle; helper programs such as
/// child.com run under their parent scenario and are resource dependencies, not standalone entries.
/// </summary>
internal static class AsmScenarioCatalog {
    private const string LargestFreeBlockObservation = "largestFreeBlock";
    private const string TestPortResultsObservation = "testPortResults";
    private const byte TestPortSuccess = 0x00;
    private const byte TestPortFailure = 0xFF;
    private const ushort MinFreeParagraphsAfterTsr = 0x8000;

    /// <summary>Every registered scenario: the CPU fixtures first, then the DOS scenarios, then the
    /// BIOS, video and sound scenarios.</summary>
    public static IReadOnlyList<AsmScenario> All =>
        [.. BuildCpuScenarios(), .. DosScenarios, .. BiosDeviceScenarios];

    /// <summary>Every DOS program scenario, so one entry point can cover them all.</summary>
    public static IReadOnlyList<AsmScenario> DosScenarios => [
        .. TsrScenarios,
        .. ExecScenarios,
        .. ReallocScenarios,
        .. EmsScenarios,
        .. XmsScenarios,
        .. Int21Scenarios,
        .. BatchScenarios,
        .. BootstrapScenarios,
        .. ConsoleProgramScenarios
    ];

    /// <summary>Batch scripts routed through COMMAND.COM: redirection, labels, subroutines, call expansion,
    /// FOR loops, CHOICE and IF routing, TSR continuation and the script effects on the filesystem.</summary>
    public static IReadOnlyList<AsmScenario> BatchScenarios => DosBatchScenarioCatalog.BatchScenarios;

    /// <summary>Boot-time scripts: AUTOEXEC.BAT content, the boot-time batch execution and the
    /// slash-path bootstrap under a live shell.</summary>
    public static IReadOnlyList<AsmScenario> BootstrapScenarios => DosBatchScenarioCatalog.BootstrapScenarios;

    /// <summary>Console programs: IOCTL subfunctions, INT 21h console character and string services, the
    /// unsupported call matrix and the INT 16h extended-key services.</summary>
    public static IReadOnlyList<AsmScenario> ConsoleProgramScenarios => ConsoleProgramScenarioCatalog.Scenarios;

    /// <summary>DOS INT 21h programs: simple resources, wildcard/short-name searches, file attributes, seek
    /// offsets, environment/PSP checks, program arguments, child return codes, redirected stdin, mounted
    /// CD-ROM and floppy images, and the Ctrl-C/Ctrl-Break keyboard cases.</summary>
    public static IReadOnlyList<AsmScenario> Int21Scenarios => Int21ScenarioCatalog.Scenarios;

    /// <summary>BIOS, RTC, video and sound programs: CMOS/RTC port access, INT 1A/15h/70h time services,
    /// DOS date/time services, the VGA scroll-up and the OPL and Sound Blaster timing programs.</summary>
    public static IReadOnlyList<AsmScenario> BiosDeviceScenarios => BiosDeviceScenarioCatalog.Scenarios;

    /// <summary>Finds a BIOS, video or sound scenario by its stable identity.</summary>
    /// <param name="identity">Identity of the scenario.</param>
    /// <returns>The scenario with that identity.</returns>
    /// <exception cref="InvalidOperationException">No BIOS or device scenario has that identity.</exception>
    public static AsmScenario BiosDeviceScenario(string identity) => BiosDeviceScenarioCatalog.Scenario(identity);

    /// <summary>TSR programs: the exit-code cases, the parent-PSP allocation regression and the batch that
    /// continues after the resident program terminated.</summary>
    public static IReadOnlyList<AsmScenario> TsrScenarios => [
        TsrExitCode("tsr22h.com", 0),
        TsrExitCode("tsr_basic.com", 0),
        TsrExitCode("tsr_zero_paragraphs.com", 0),
        TsrExitCode("tsr_valid_paragraphs.com", 0),
        TsrExitCode("tsr_return_code.com", 66),
        TsrExitCode("tsr_large_paragraphs.com", 0),
        TsrExitCode("tsr_interrupt_vector.com", 0),
        TsrParentPspAllocation(),
        TsrBatchContinues()
    ];

    /// <summary>EXEC scenarios: the master program with its overlay and the FCB parent with its child.</summary>
    public static IReadOnlyList<AsmScenario> ExecScenarios => [
        ExecModesAndOverlays(),
        FcbChild()
    ];

    /// <summary>DOS reallocation programs, one per padding size, each checking the carry flag of AH=4Ah.</summary>
    public static IReadOnlyList<AsmScenario> ReallocScenarios => [
        ReallocPadding("realloc_test_pad0.com"),
        ReallocPadding("realloc_test_pad1.com"),
        ReallocPadding("realloc_test_pad12.com"),
        ReallocPadding("realloc_test_pad13.com"),
        ReallocPadding("realloc_test_pad15.com"),
        ReallocPadding("realloc_test_pad16.com")
    ];

    /// <summary>EMS programs, run with EMS enabled and XMS disabled.</summary>
    public static IReadOnlyList<AsmScenario> EmsScenarios => [
        EmsResource("ems_detection_interrupt_vector.com"),
        EmsResource("ems_detection_device_name.com"),
        EmsResource("ems_get_status.com"),
        EmsResource("ems_get_page_frame_segment.com"),
        EmsResource("ems_get_unallocated_page_count.com"),
        EmsResource("ems_allocate_pages.com"),
        EmsResource("ems_map_handle_page.com"),
        EmsResource("ems_memory_access_mapped_pages.com"),
        EmsResource("ems_deallocate_pages.com"),
        EmsResource("ems_get_version.com"),
        EmsResource("ems_save_page_map.com"),
        EmsResource("ems_restore_page_map.com"),
        EmsResource("ems_get_handle_count.com"),
        EmsResource("ems_get_handle_pages.com"),
        EmsResource("ems_allocate_zero_pages.com"),
        EmsResource("ems_map_invalid_handle.com"),
        EmsResource("ems_logical_pages_independent.com"),
        EmsResource("ems_map_physical_page_4.com"),
        EmsResource("ems_handle_allocation_after_deallocation.com")
    ];

    /// <summary>XMS programs, run with XMS enabled and the A20 gate closed.</summary>
    public static IReadOnlyList<AsmScenario> XmsScenarios => [
        XmsResource("xms_installation_check.com"),
        XmsResource("xms_entry_point.com"),
        XmsResource("xms_request_release_umb.com"),
        TestPortResourceScenario("XmsTests", "xms_umb_disabled.com", enableEms: false, enableXms: true,
            enableUmb: false)
    ];

    /// <summary>Finds a DOS scenario by its stable identity.</summary>
    /// <param name="identity">Identity of the scenario.</param>
    /// <returns>The scenario with that identity.</returns>
    /// <exception cref="InvalidOperationException">No DOS scenario has that identity.</exception>
    public static AsmScenario DosScenario(string identity) {
        return DosScenarios.FirstOrDefault(scenario => scenario.Identity == identity)
            ?? throw new InvalidOperationException($"No DOS scenario with identity '{identity}'.");
    }

    /// <summary>A simple CPU fixture: the bin run and its recorded memory dump checked as the oracle.</summary>
    /// <param name="binName">Bin name under Resources/cpuTests.</param>
    /// <returns>The scenario for that bin.</returns>
    public static AsmScenario CpuBin(string binName) {
        return AsmScenario.FromFixture(binName, () => AsmFixtureCatalog.Dump(binName));
    }

    /// <summary>
    /// Runs a TSR program that terminates and returns its exit code in AX. Each run prepares an
    /// independent directory holding the program, so discovery and generated execution never share a
    /// host file.
    /// </summary>
    /// <param name="programName">File name of the TSR program under Resources/DosTsrTests.</param>
    /// <param name="expectedExitCode">Exit code the program leaves in AX.</param>
    /// <returns>The scenario for that program.</returns>
    public static AsmScenario TsrExitCode(string programName, byte expectedExitCode) {
        string identity = Path.GetFileNameWithoutExtension(programName);
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string target = CopyResourceFile("DosTsrTests", programName, directory);
            AsmRunSettings settings = new() {
                BinName = target,
                ScenarioName = identity,
                MaxCycles = 200000L,
                InstallInterruptVectors = true,
                EnableA20Gate = true
            };
            return new AsmFixture(settings, [], result =>
                result.State.AX.Should().Be(expectedExitCode, $"{programName} returns its exit code in AX"));
        }, [$"DosTsrTests/{programName}"]);
    }

    /// <summary>
    /// Reproduces the Maupiti Island bug: the game reads the parent (COMMAND.COM) PSP[0x02]
    /// ("first segment beyond parent's allocation") and uses it directly as the TSR paragraph
    /// count (DX). In Spice86 the fake COMMAND.COM PSP had CurrentSize set to
    /// LastFreeSegment (0x9FFF), so DX became 0x9FFF - exhausting all conventional memory.
    /// After the fix, COMMAND.COM PSP[0x02] = CommandComSegment + PspSizeInParagraphs = 0x70,
    /// so DX = 0x70, and nearly all of conventional memory remains free afterwards.
    /// </summary>
    /// <returns>The scenario, whose oracle reads the DOS allocation map of the finished run.</returns>
    public static AsmScenario TsrParentPspAllocation() {
        const string identity = "tsr_parent_psp_size";
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string target = CopyResourceFile("DosTsrTests", "tsr_parent_psp_size.com", directory);
            scope.AddObservationProvider(LargestFreeBlockObservation,
                machine => machine.Dos.MemoryManager.FindLargestFree());
            AsmRunSettings settings = new() {
                BinName = target,
                ScenarioName = identity,
                MaxCycles = 200000L,
                InstallInterruptVectors = true,
                EnableA20Gate = true
            };
            return new AsmFixture(settings, [], result => {
                DosMemoryControlBlock largestFree =
                    result.Observations.Get<DosMemoryControlBlock>(LargestFreeBlockObservation);
                largestFree.IsFree.Should().BeTrue(
                    "after a TSR that uses parent PSP[0x02] as DX, conventional memory must have a large free block");
                largestFree.Size.Should().BeGreaterThan(MinFreeParagraphsAfterTsr,
                    "after TSR the bulk of conventional memory must still be free;" +
                    " if only 2 paragraphs (the env block) are free the bug is still present");
            });
        }, ["DosTsrTests/tsr_parent_psp_size.com"]);
    }

    /// <summary>
    /// Runs START.BAT, which launches a resident program and then a second program. The batch interpreter
    /// must continue with the next command after the TSR terminated, so the second program writes its
    /// marker to the first video cell.
    /// </summary>
    /// <returns>The scenario, with tsr_basic.com as a resource dependency.</returns>
    public static AsmScenario TsrBatchContinues() {
        const string identity = "tsr_batch_continues";
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            CopyResourceFile("DosTsrTests", "tsr_basic.com", "TSR_BASIC.COM", directory);
            CopyResourceFile("DosConsoleProgramTests", "video_writer_z_0.com", "WRITER.COM", directory);
            string startBatch = Path.Join(directory, "START.BAT");
            File.WriteAllText(startBatch, "TSR_BASIC.COM\r\nWRITER.COM\r\n");
            AsmRunSettings settings = new() {
                BinName = startBatch,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = 300000L,
                InstallInterruptVectors = true,
                CDrive = directory
            };
            return new AsmFixture(settings, [], result =>
                VideoMemoryReader.ReadCells(result.Memory, 1).Should().Be("Z",
                    "the batch must continue to WRITER.COM after the TSR terminated"));
        }, ["DosTsrTests/tsr_basic.com"]);
    }

    /// <summary>The master program EXECs children in several modes and reads back an overlay; generated
    /// code must follow every child entry and unwind back to the parent on terminate.</summary>
    /// <returns>The scenario, with child.com, tsr_hook.com and the overlay as resource dependencies.</returns>
    public static AsmScenario ExecModesAndOverlays() {
        return new AsmScenario("dos_exec_modes_and_overlays", scope => {
            string directory = scope.CreateWorkingDirectory("dos_exec_modes_and_overlays");
            CopyResourceFile("DosExecTests", "dos_exec_master.com", directory);
            CopyResourceFile("DosExecTests", "child.com", directory);
            CopyResourceFile("DosExecTests", "tsr_hook.com", directory);
            CopyResourceFile("DosExecTests", "overlay_driver.bin", directory);
            File.Move(Path.Join(directory, "overlay_driver.bin"), Path.Join(directory, "dos_exec_master.000"));
            AsmRunSettings settings = ExecSettings("dos_exec_modes_and_overlays", directory, "dos_exec_master.com");
            return new AsmFixture(settings, [], result =>
                VideoMemoryReader.ReadCells(result.Memory, 80 * 25).Should().Contain("SEMJCTLOAV"));
        }, [
            "DosExecTests/dos_exec_master.com",
            "DosExecTests/child.com",
            "DosExecTests/tsr_hook.com",
            "DosExecTests/overlay_driver.bin"
        ]);
    }

    /// <summary>An FCB-using parent EXECs a child and keeps its own FCB state; generated code must follow the
    /// child entry and unwind back to the parent on terminate.</summary>
    /// <returns>The scenario, with fcbchild.com as a resource dependency.</returns>
    public static AsmScenario FcbChild() {
        return new AsmScenario("dos_fcb_child", scope => {
            string directory = scope.CreateWorkingDirectory("dos_fcb_child");
            CopyResourceFile("DosFcbTests", "fcb_process_isolation.com", directory);
            CopyResourceFile("DosFcbTests", "fcbchild.com", directory);
            AsmRunSettings settings = ExecSettings("dos_fcb_child", directory, "fcb_process_isolation.com");
            return new AsmFixture(settings, [], result =>
                VideoMemoryReader.ReadCells(result.Memory, 80 * 25).Should().Contain("SCWOXER"));
        }, [
            "DosFcbTests/fcb_process_isolation.com",
            "DosFcbTests/fcbchild.com"
        ]);
    }

    private static AsmRunSettings ExecSettings(string scenarioName, string cDrive, string programName) {
        return new AsmRunSettings {
            BinName = Path.Join(cDrive, programName),
            ScenarioName = scenarioName,
            EnablePit = true,
            MaxCycles = 300000L,
            InstallInterruptVectors = true,
            EnableXms = true,
            EnableEms = true,
            CDrive = cDrive,
            ConfigureMachine = machine => machine.CpuState.Flags.CpuModel = CpuModel.INTEL_80286
        };
    }

    /// <summary>
    /// Runs INT 21h, AH=4Ah with the padding the program was assembled with. The program reports the
    /// outcome through the carry flag when the machine stops, so the scenario captures CF at the stop
    /// breakpoint exactly as the original test did.
    /// </summary>
    /// <param name="fileName">File name of the program under Resources/DosReallocTest.</param>
    /// <returns>The scenario for that program.</returns>
    private static AsmScenario ReallocPadding(string fileName) {
        string identity = Path.GetFileNameWithoutExtension(fileName);
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string target = CopyResourceFile("DosReallocTest", fileName, directory);
            bool reallocSucceeded = false;
            AsmRunSettings settings = new() {
                BinName = target,
                ScenarioName = identity,
                MaxCycles = 50000L,
                InstallInterruptVectors = true,
                ConfigureMachine = machine => machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                    new AddressBreakPoint(BreakPointType.MACHINE_STOP, 0,
                        _ => reallocSucceeded = !machine.CpuState.CarryFlag, true), true)
            };
            return new AsmFixture(settings, [], result => reallocSucceeded.Should().BeTrue(
                $"DOS INT 21h AH=4Ah (realloc) should succeed (CF=0) for {fileName}"));
        }, [$"DosReallocTest/{fileName}"]);
    }

    private static AsmScenario EmsResource(string fileName) {
        return TestPortResourceScenario("EmsTests", fileName, enableEms: true, enableXms: false,
            enableUmb: true);
    }

    private static AsmScenario XmsResource(string fileName) {
        return TestPortResourceScenario("XmsTests", fileName, enableEms: false, enableXms: true,
            enableUmb: true);
    }

    /// <summary>
    /// Runs a DOS program that reports every checked call through the shared test result ports 0x999 and
    /// 0x998. Each run installs its own handler on the fresh machine, and the oracle requires at least one
    /// success byte and no failure byte.
    /// </summary>
    /// <param name="resourceSubDirectory">Resource folder holding the program.</param>
    /// <param name="fileName">File name of the program.</param>
    /// <param name="enableEms">Whether the run provides EMS.</param>
    /// <param name="enableXms">Whether the run provides XMS.</param>
    /// <param name="enableUmb">Whether the run provides DOS upper memory blocks.</param>
    /// <returns>The scenario for that program.</returns>
    private static AsmScenario TestPortResourceScenario(string resourceSubDirectory, string fileName,
        bool enableEms, bool enableXms, bool enableUmb) {
        string identity = Path.GetFileNameWithoutExtension(fileName);
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string target = CopyResourceFile(resourceSubDirectory, fileName, directory);
            AsmRunSettings settings = new() {
                BinName = target,
                ScenarioName = identity,
                EnablePit = true,
                MaxCycles = 100000L,
                InstallInterruptVectors = true,
                EnableEms = enableEms,
                EnableXms = enableXms,
                EnableUmb = enableUmb,
                ConfigureMachine = machine => scope.Register(TestPortResultsObservation,
                    new TestIoPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher))
            };
            return new AsmFixture(settings, [], result => {
                TestIoPortHandler testHandler =
                    result.Observations.Get<TestIoPortHandler>(TestPortResultsObservation);
                testHandler.Results.Should().Contain(TestPortSuccess, $"{fileName} must report success");
                testHandler.Results.Should().NotContain(TestPortFailure, $"{fileName} must not report failure");
            });
        }, [$"{resourceSubDirectory}/{fileName}"]);
    }

    private static string CopyResourceFile(string resourceSubDirectory, string fileName, string targetDirectory) {
        return CopyResourceFile(resourceSubDirectory, fileName, fileName, targetDirectory);
    }

    private static string CopyResourceFile(string resourceSubDirectory, string sourceFileName, string targetFileName,
        string targetDirectory) {
        string source = Path.Join(AppContext.BaseDirectory, "Resources", resourceSubDirectory, sourceFileName);
        string target = Path.Join(targetDirectory, targetFileName);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static IReadOnlyList<AsmScenario> BuildCpuScenarios() {
        List<AsmScenario> scenarios = [
            CpuBin("add"),
            CpuBin("bcdcnv"),
            CpuBin("cmpneg"),
            CpuBin("datatrnf"),
            CpuBin("div"),
            CpuBin("interrupt"),
            CpuBin("jump1"),
            CpuBin("jump2"),
            CpuBin("rep"),
            CpuBin("rotate"),
            CpuBin("segpr"),
            CpuBin("shifts"),
            CpuBin("strings"),
            CpuBin("sub"),
            AsmScenario.FromFixture("bitwise", AsmFixtureCatalog.Bitwise),
            AsmScenario.FromFixture("control", AsmFixtureCatalog.Control),
            AsmScenario.FromFixture("mul", AsmFixtureCatalog.Mul),
            AsmScenario.FromFixture("div2", AsmFixtureCatalog.Div2),
            AsmScenario.FromFixture("jmpmov", AsmFixtureCatalog.JmpMov),
            AsmScenario.FromFixture("returnedterminator", AsmFixtureCatalog.ReturnedTerminator),
            AsmScenario.FromFixture("lockprefix", AsmFixtureCatalog.LockPrefix),
            AsmScenario.FromFixture("selfmodifyvalue", AsmFixtureCatalog.SelfModifyValue),
            AsmScenario.FromFixture("selfmodifyinstructions", AsmFixtureCatalog.SelfModifyInstructions),
            AsmScenario.FromFixture("selfmodifyrep", AsmFixtureCatalog.SelfModifyRep),
            AsmScenario.FromFixture("selfmodifyterminator", AsmFixtureCatalog.SelfModifyTerminator),
            AsmScenario.FromFixture("selfmodifyje", AsmFixtureCatalog.SelfModifyJe),
            AsmScenario.FromFixture("selfmodifycall", AsmFixtureCatalog.SelfModifyCall),
            AsmScenario.FromFixture("selfmodifycalltarget", AsmFixtureCatalog.SelfModifyCallTarget),
            AsmScenario.FromFixture("multimisalignedcall", AsmFixtureCatalog.MultiMisalignedCall),
            AsmScenario.FromFixture("pushcs_callnear_retfar", AsmFixtureCatalog.PushCsCallNearRetFar),
            AsmScenario.FromFixture("externalint", AsmFixtureCatalog.ExternalInt),
            AsmScenario.FromFixture("stipending", AsmFixtureCatalog.StiPending),
            AsmScenario.FromFixture("ioports", AsmFixtureCatalog.IoPorts),
            AsmScenario.FromFixture("interior_entry", AsmFixtureCatalog.InteriorEntry),
            AsmScenario.FromFixture("divfaultloop", AsmFixtureCatalog.DivFaultLoop),
            AsmScenario.FromFixture("flagconditions", AsmFixtureCatalog.FlagConditions),
            AsmScenario.FromFixture("linearsamesegmenteddifferent", AsmFixtureCatalog.LinearSameSegmentedDifferent),
            AsmScenario.FromFixture("sticli", AsmFixtureCatalog.StiCli),
            AsmScenario.FromFixture("ownerlesscallcycle", AsmFixtureCatalog.OwnerlessCallCycle),
            AsmScenario.FromFixture("sameretloop", AsmFixtureCatalog.SameretLoop),
            AsmScenario.FromFixture("intchain", AsmFixtureCatalog.IntChain),
            AsmScenario.FromFixture("test386", AsmFixtureCatalog.Test386),
            AsmScenario.FromFixture("jump1", AsmFixtureCatalog.Jump1WithoutSpeculation),
            AsmScenario.FromFixture("speculative_branch", () => AsmFixtureCatalog.SpeculativeBranch(SpeculativeArm.Observed)),
            AsmScenario.FromFixture("speculative_branch", () => AsmFixtureCatalog.SpeculativeBranch(SpeculativeArm.Unobserved)),
            AsmScenario.FromFixture("speculative_branch", AsmFixtureCatalog.SpeculativeBranchWithoutSpeculation),
            AsmScenario.FromFixture("speculative_closure", () => AsmFixtureCatalog.SpeculativeClosure(SpeculativeArm.Observed)),
            AsmScenario.FromFixture("speculative_closure", () => AsmFixtureCatalog.SpeculativeClosure(SpeculativeArm.Unobserved)),
            AsmScenario.FromFixture("speculative_convergence", () => AsmFixtureCatalog.SpeculativeConvergence(SpeculativeArm.Observed)),
            AsmScenario.FromFixture("speculative_convergence", () => AsmFixtureCatalog.SpeculativeConvergence(SpeculativeArm.Unobserved)),
            AsmScenario.FromFixture("speculative_mixed_block", () => AsmFixtureCatalog.SpeculativeMixedBlock(SpeculativeArm.Observed)),
            AsmScenario.FromFixture("speculative_mixed_block", () => AsmFixtureCatalog.SpeculativeMixedBlock(SpeculativeArm.Unobserved)),
            AsmScenario.FromFixture("speculative_call_entry", AsmFixtureCatalog.SpeculativeCallEntry),
            AsmScenario.FromFixture("speculative_invalid_opcode", AsmFixtureCatalog.SpeculativeInvalidOpcode),
            AsmScenario.FromFixture("speculative_smc_guard", AsmFixtureCatalog.SpeculativeSmcGuard),
            AsmScenario.FromFixture("speculative_discard", AsmFixtureCatalog.SpeculativeDiscard),
            AsmScenario.FromFixture("callstackunwind",
                () => AsmFixtureCatalog.CallStackUnwind(SpeculativeArm.Observed)),
            AsmScenario.FromFixture("callstackunwind",
                () => AsmFixtureCatalog.CallStackUnwind(SpeculativeArm.Unobserved)),
            PartitionScenario("partition_jump_into_function_middle"),
            PartitionScenario("partition_shared_tail"),
            PartitionScenario("partition_multi_entry_dominated_shared"),
            PartitionScenario("partition_multi_entry_irreducible_shared"),
            PartitionScenario("partition_cross_function_loop"),
            PartitionScenario("partition_mixed_activation_cycle"),
            PartitionScenario("partition_indirect_call_jump"),
            PartitionScenario("partition_jump_table"),
            PartitionScenario("partition_untested_indirect"),
            PartitionScenario("partition_jump_table_mixed"),
            PartitionScenario("partition_far_dispatch"),
            PartitionScenario("partition_sign_conditions"),
            PartitionScenario("partition_untested_far_indirect"),
            PartitionScenario("partition_untaken_cpuid"),
            PartitionScenario("partition_signed_min_compare"),
            PartitionScenario("partition_back_edge_fallthrough"),
            PartitionScenario("partition_jump_to_next"),
            PartitionScenario("partition_two_fault_handlers"),
            AsmScenario.FromFixture("partition_mutual_recursion_unwind", AsmFixtureCatalog.PartitionMutualRecursionUnwind),
            AsmScenario.FromFixture("romwrite", AsmFixtureCatalog.RomWrite),
            AsmScenario.FromFixture("flashback_mode13", () => AsmFixtureCatalog.FlashbackVideo(false)),
            AsmScenario.FromFixture("flashback_video", () => AsmFixtureCatalog.FlashbackVideo(true))
        ];
        return scenarios;
    }

    private static AsmScenario PartitionScenario(string binName) {
        return AsmScenario.FromFixture(binName, () => AsmFixtureCatalog.Partition(binName));
    }
}
