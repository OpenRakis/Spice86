namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Tests.Utility;

using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

/// <summary>
/// Behavioral tests for the two switchable memory-indirect far calls of the mouse stubs
/// (<c>CALL FAR CS:[disp16]</c>): the int 74h stub's driver call and the mouse driver's user-routine
/// call. The 4-byte far pointer each call reads sits right after the stub's terminating far ret /
/// iret, outside any instruction path, so <see cref="InMemoryAddressSwitcher"/> can rewrite it at run
/// time without ever patching instruction bytes. These tests pin the stub layout, the speculative
/// link each pointer write reports to the CFG, its persistence across CFG reload, and the generated
/// code reading the pointer at call time.
/// </summary>
public class MouseSwitchableCallTest {
    /// <summary>Offset of the <c>disp16</c> operand inside the <c>2E FF 1E</c> encoding.</summary>
    private const int DisplacementOffset = 3;

    /// <summary>Offset of the pointer inside the int 74h stub: call(5) + callback(4) + iret(1).</summary>
    private const int Int74PointerOffset = 10;

    /// <summary>Offset of the user-routine call inside the driver stub, after its first callback(4).</summary>
    private const int DriverUserCallOffset = 4;

    /// <summary>Offset of the pointer inside the driver stub: call ends at +9, callback(4), far ret(1).</summary>
    private const int DriverPointerOffset = 14;

    private static AsmRunSettings Settings(Action<Machine>? configureMachine = null) => new() {
        BinName = "mouse_irq12",
        MaxCycles = 0x400000,
        InstallInterruptVectors = true,
        // The program is a BIOS image at F000: the BIOS handlers go to D000.
        AsmHandlersSegment = 0xD000,
        ConfigureMachine = configureMachine ?? AsmRunSettings.NoMachineConfiguration
    };

    [Fact]
    public void Int74DriverCallReadsSwitchablePointerAndLinksToItsTarget() {
        // No program run and no IRQ 12: CFG seeding and the explicit post-seed registration must
        // discover the stub and wire the switchable call to the driver entry the pointer holds.
        using Spice86Creator creator = Settings().CreateEmulatedCreator(JitMode.InterpretedOnly);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;

        SegmentedAddress callSite = machine.InterruptVectorTable[0x74];
        AssertIndirectFarCall(machine.Memory, callSite);
        machine.Memory.UInt8[callSite.Segment, callSite.Offset + Int74PointerOffset - 1]
            .Should().Be(0xCF, "the stub terminates with IRET right before the switchable pointer");
        SegmentedAddress driverEntry = Int74PointerTarget(machine);

        CfgInstruction call = NodeAt(machine, callSite);
        call.IsCall.Should().BeTrue();
        call.Successors.Should().Contain(node => node.Address == driverEntry,
            "the pointer write at install must have linked the call to the driver entry");
    }

    [Fact]
    public void DriverUserCallReadsSwitchablePointerAndLinksToDefault() {
        using Spice86Creator creator = Settings().CreateEmulatedCreator(JitMode.InterpretedOnly);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;

        SegmentedAddress userCallSite = DriverUserCallSite(machine);
        AssertIndirectFarCall(machine.Memory, userCallSite);
        SegmentedAddress defaultHandler = FarPointerTarget(machine.Memory, userCallSite);

        CfgInstruction call = NodeAt(machine, userCallSite);
        call.Successors.Should().Contain(node => node.Address == defaultHandler,
            "no user routine is registered, so the pointer holds the default far-ret handler");
    }

    [Fact]
    public void ReassignedPointerLinksNewTargetSpeculatively() {
        using Spice86Creator creator = Settings().CreateEmulatedCreator(JitMode.InterpretedOnly);
        using Spice86DependencyInjection di = creator.Create();
        Machine machine = di.Machine;

        SegmentedAddress userCallSite = DriverUserCallSite(machine);
        SegmentedAddress defaultHandler = FarPointerTarget(machine.Memory, userCallSite);
        CfgInstruction call = NodeAt(machine, userCallSite);

        // Register a user routine and let the driver callback switch the pointer, exactly like it
        // does when the interrupt fires during a program run.
        SegmentedAddress routine = new(0x1234, 0x0010);
        machine.Memory.UInt8[routine.Linear] = 0xCB; // far ret, so the target decodes to safe code
        MouseDriver driver = (MouseDriver)machine.MouseDriver;
        driver.RegisterCallback(new MouseUserCallback(MouseEventMask.Movement, routine.Segment, routine.Offset));
        machine.InputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(1, 1));
        machine.InputEventHub.ProcessAllPendingInputEvents();

        driver.BeforeUserHandlerExecution();

        FarPointerTarget(machine.Memory, userCallSite).Should().Be(routine,
            "the callback rewrote the far pointer, not the instruction");
        machine.Memory.UInt8[userCallSite.Linear].Should().Be(0x2E,
            "the call instruction itself is never patched");
        call.Successors.Should().Contain(node => node.Address == routine,
            "the pointer write must have linked the call to the registered routine");
        call.Successors.Should().Contain(node => node.Address == defaultHandler,
            "the previously linked default target edge stays");
    }

    [Fact]
    public void Int74ReassignedTargetLinksSpeculativelyAndSurvivesReload() {
        using TempFile recordedDataDirectory = new(nameof(Int74ReassignedTargetLinksSpeculativelyAndSurvivesReload));
        SegmentedAddress newDriverTarget = new(0x1234, 0x0020);
        AsmRunSettings settings = Settings(machine => {
            // A far ret keeps the reassigned target decodable, like a real driver entry would be.
            machine.Memory.UInt8[newDriverTarget.Linear] = 0xCB;
            BiosMouseInt74Handler handler =
                (BiosMouseInt74Handler)machine.InstalledInterruptHandlers.Handlers[0x74];
            handler.SetMouseDriverAddress(newDriverTarget);
            // A non-zero user subroutine registers the mouse fact the reload dump carries.
            ((MouseDriver)machine.MouseDriver).RegisterCallback(
                new MouseUserCallback(MouseEventMask.Movement, 0x2222, 0x0010));
        }) with { RecordedDataDirectory = recordedDataDirectory.Path };

        SegmentedAddress callSite;
        using (AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly)) {
            Machine machine = run.Machine;
            callSite = machine.InterruptVectorTable[0x74];
            AssertIndirectFarCall(machine.Memory, callSite);
            FarPointerTarget(machine.Memory, callSite).Should().Be(newDriverTarget,
                "SetMouseDriverAddress rewrote the pointer, never the instruction");
            CfgInstruction call = NodeAt(machine, callSite);
            call.Successors.Should().Contain(node => node.Address == newDriverTarget,
                "the pointer write must have linked the call to the reassigned driver");
            call.SpeculativelyWiredSuccessors.Should().Contain(node => node.Address == newDriverTarget,
                "IRQ 12 never fired, so the edge was wired speculatively, not observed");
        }

        AsmRunSettings reloadSettings = settings with { ReloadCfgGraph = true };
        // AsmFixtureRun builds the emulator without running the program: the reload already merged
        // the user-subroutine registration the first run recorded.
        using (AsmFixtureRun beforeRun = new(reloadSettings.CreateEmulatedCreator(JitMode.InterpretedOnly),
                new AsmRunScope())) {
            beforeRun.DependencyInjection.GeneratorInputsCollector
                .Capture(beforeRun.Machine.CfgCpu.ExecutionContextManager).HardwareInterrupts
                .MouseUserSubroutineRegistered.Should().BeTrue(
                    "the reloading run merged the subroutine registration the dump recorded");
        }

        using AsmFixtureRun reloaded = AsmFixtureRunner.RunSettings(reloadSettings, JitMode.InterpretedOnly);
        NodeAt(reloaded.Machine, callSite).Successors
            .Should().Contain(node => node.Address == newDriverTarget,
                "the reassigned call-target edge must be restored by the reload dump");
        FarPointerTarget(reloaded.Machine.Memory, callSite).Should().Be(newDriverTarget,
            "the pointer survives reload because the stub is rewritten from the dump state");
    }

    [Fact]
    public void Int74ReassignedTargetEdgeIsObservedWhenTheCallExecutes() {
        // The reassigned driver target is linked speculatively before the interrupt fires; the
        // run then delivers IRQ 12 so the stub's call executes and confirms that edge.
        SegmentedAddress newDriverTarget = new(0x1234, 0x0020);
        AsmRunSettings settings = Settings(machine => {
            machine.Memory.UInt8[newDriverTarget.Linear] = 0xCB;
            BiosMouseInt74Handler handler =
                (BiosMouseInt74Handler)machine.InstalledInterruptHandlers.Handlers[0x74];
            handler.SetMouseDriverAddress(newDriverTarget);
            machine.Memory.UInt8[0, 0x0500] = 0x01;
            machine.DualPic.SetIrqMask(12, false);
            machine.InputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(1, 1));
            machine.InputEventHub.ProcessAllPendingInputEvents();
        });
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly);
        Machine machine = run.Machine;

        SegmentedAddress callSite = machine.InterruptVectorTable[0x74];
        CfgInstruction call = NodeAt(machine, callSite);
        call.Successors.Should().Contain(node => node.Address == newDriverTarget && !node.IsSpeculative,
            "the int 74h stub ran its call to the reassigned driver target");
        call.SpeculativelyWiredSuccessors.Should().NotContain(node => node.Address == newDriverTarget,
            "observed execution clears the speculative provenance of the reassigned edge");
        FarPointerTarget(machine.Memory, callSite).Should().Be(newDriverTarget);
    }

    [Fact]
    public void RegisteredRoutineEdgeIsObservedWhenTheCallExecutes() {
        // End-to-end: the program registers the routine through INT 33h and IRQ 12 fires during its
        // wait loop, so the call runs and the speculatively linked edge becomes an observed one.
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(
            AsmFixtureCatalog.MouseIrq12RegisteredRoutine(), JitMode.InterpretedOnly);
        Machine machine = run.Machine;

        SegmentedAddress userCallSite = DriverUserCallSite(machine);
        SegmentedAddress routine = FarPointerTarget(machine.Memory, userCallSite);
        routine.Segment.Should().NotBe(userCallSite.Segment,
            "the pointer now holds the program routine, not the stub-segment default handler");
        CfgInstruction call = NodeAt(machine, userCallSite);
        call.Successors.Should().Contain(node => node.Address == routine && !node.IsSpeculative,
            "executing the call must confirm the edge to the registered routine");
        call.SpeculativelyWiredSuccessors.Should().NotContain(node => node.Address == routine,
            "observed execution clears the speculative provenance of the routine edge");

        // The same IRQ 12 delivery ran the int 74h stub's switchable call to the driver entry:
        // its edge must be observed too, with speculative provenance cleared.
        SegmentedAddress int74CallSite = machine.InterruptVectorTable[0x74];
        SegmentedAddress driverEntry = FarPointerTarget(machine.Memory, int74CallSite);
        CfgInstruction int74Call = NodeAt(machine, int74CallSite);
        int74Call.Successors.Should().Contain(node => node.Address == driverEntry && !node.IsSpeculative,
            "IRQ 12 ran the int 74h stub's call to the driver entry");
        int74Call.SpeculativelyWiredSuccessors.Should().NotContain(node => node.Address == driverEntry,
            "observed execution clears the speculative provenance of the driver edge");
    }

    [Fact]
    public void CallTargetEdgesSurviveCfgReload() {
        using TempFile recordedDataDirectory = new(nameof(CallTargetEdgesSurviveCfgReload));
        AsmRunSettings settings = Settings(machine => {
            machine.Memory.UInt8[0, 0x0500] = 0x01;
            machine.DualPic.SetIrqMask(12, false);
            machine.InputEventHub.PostMouseMoveEvent(new MouseMoveEventArgs(1, 1));
            machine.InputEventHub.ProcessAllPendingInputEvents();
        }) with { RecordedDataDirectory = recordedDataDirectory.Path };

        SegmentedAddress userCallSite;
        SegmentedAddress routine;
        using (AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly)) {
            Machine machine = run.Machine;
            userCallSite = DriverUserCallSite(machine);
            routine = FarPointerTarget(machine.Memory, userCallSite);
            NodeAt(machine, userCallSite).Successors
                .Should().Contain(node => node.Address == routine);
        }

        AsmRunSettings reloadSettings = settings with { ReloadCfgGraph = true };
        using AsmFixtureRun reloaded = AsmFixtureRunner.RunSettings(reloadSettings, JitMode.InterpretedOnly);
        NodeAt(reloaded.Machine, userCallSite).Successors
            .Should().Contain(node => node.Address == routine,
                "the observed call-target edge must be restored by the reload dump");
    }

    [Fact]
    public void RegisterSpeculativeTargetLinksAnyIndexedInstruction() {
        // The explorer API is generic: it links whichever instruction is indexed at the source
        // address to the reported target. Nothing call-specific is checked - the switcher knows it
        // emitted a call - so an indexed NOP and an indexed direct call both accept the edge.
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(Settings(), JitMode.InterpretedOnly);
        Machine machine = run.Machine;

        SegmentedAddress nopSite = new(0xD100, 0x0100);
        SegmentedAddress directCallSite = new(0xD100, 0x0200);
        SegmentedAddress target = new(0xD100, 0x0300);
        machine.Memory.UInt8[nopSite.Segment, nopSite.Offset] = 0x90;
        machine.Memory.UInt8[directCallSite.Segment, directCallSite.Offset] = 0x9A;
        // Direct far call operand ptr16:16 (offset then segment); the operand target is irrelevant
        // here, the reported target is what gets linked.
        machine.Memory.UInt8[target.Segment, target.Offset] = 0xCB;

        CfgInstruction nopNode = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder
            .GetInstructionFromMemory(nopSite);
        CfgInstruction directCallNode = machine.CfgCpu.CfgNodeFeeder.InstructionsFeeder
            .GetInstructionFromMemory(directCallSite);
        nopNode.IsCall.Should().BeFalse("the API is not gated on the source being a call");
        directCallNode.IsCall.Should().BeTrue();

        machine.CfgCpu.CallTargetRegistry.RegisterCallTarget(nopSite, target);
        machine.CfgCpu.CallTargetRegistry.RegisterCallTarget(directCallSite, target);

        nopNode.Successors.Should().Contain(node => node.Address == target,
            "a non-call source accepts the reported edge");
        directCallNode.Successors.Should().Contain(node => node.Address == target,
            "a direct call source accepts the reported edge too");
    }

    [Fact]
    public void GeneratedCodeReadsSwitchablePointerAtCallTime() {
        // The int 74h stub's call is a runtime-target far call: generated code must declare
        // targetSegment/targetOffset read from the pointer in emulated memory at call time and
        // dispatch on them - never a baked-in destination and never a byte check of the call.
        using AsmRunScope scope = new(AsmFixtureCatalog.MouseIrq12RegisteredRoutine());
        GeneratedCSharpProgram program = new GeneratedCodeMachineTestRunner().GenerateProgramAndSource(scope);
        Regex.Matches(program.SourceText, @"targetSegment_\w+").Count.Should().BeGreaterThanOrEqualTo(2,
            "both switchable calls read their segment from the pointer at call time");
        Regex.Matches(program.SourceText, @"targetOffset_\w+").Count.Should().BeGreaterThanOrEqualTo(2,
            "both switchable calls read their offset from the pointer at call time");
        program.SourceText.Should().Contain("SegmentD000Globals.Word006D",
            "the int 74h stub's call reads the offset word of its pointer");
        program.SourceText.Should().Contain("CsGlobals.Word00FB",
            "the driver's user-routine call reads the offset word of its pointer");
        program.SourceText.Should().NotContain("PatchedCallGuard",
            "no mouse-specific byte guard is emitted for the switchable call");
    }

    private static SegmentedAddress Int74PointerTarget(Machine machine) =>
        FarPointerTarget(machine.Memory, machine.InterruptVectorTable[0x74]);

    private static SegmentedAddress DriverUserCallSite(Machine machine) {
        SegmentedAddress driverEntry = Int74PointerTarget(machine);
        return new SegmentedAddress(driverEntry.Segment, (ushort)(driverEntry.Offset + DriverUserCallOffset));
    }

    private static void AssertIndirectFarCall(IMemory memory, SegmentedAddress callSite) {
        memory.UInt8[callSite.Segment, callSite.Offset].Should().Be(0x2E,
            "the switchable call is CALL FAR CS:[disp16], whose CS override is required because "
            + "callers do not keep DS on the stub's segment");
        memory.UInt8[callSite.Segment, (uint)callSite.Offset + 1].Should().Be(0xFF);
        memory.UInt8[callSite.Segment, (uint)callSite.Offset + 2].Should().Be(0x1E);
    }

    /// <summary>Reads the target of a <c>CALL FAR CS:[disp16]</c> like the CPU does: the 16-bit
    /// displacement selects a 4-byte far pointer in the call's own code segment.</summary>
    private static SegmentedAddress FarPointerTarget(IMemory memory, SegmentedAddress callSite) {
        ushort displacement = memory.UInt16[callSite.Segment, (uint)callSite.Offset + DisplacementOffset];
        return memory.SegmentedAddress16[callSite.Segment, displacement];
    }

    private static CfgInstruction NodeAt(Machine machine, SegmentedAddress address) =>
        machine.CfgCpu.CfgNodeFeeder.NodeIndex.GetAtAddress(address).Single();
}
