namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.VM;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Runs the shared <c>externalint</c> CPU scenario and checks the PIC and PIT state it leaves behind.
/// The program and its behavioral oracle live once in <see cref="AsmFixtureCatalog.ExternalInt"/>, so
/// the interpreted and the generated runs of that scenario check the same tick in RAM; only the
/// device-state checks below are engine-level assertions that stay on the interpreted run.
/// </summary>
public sealed class DualPicIntegrationTests {
    [Fact]
    public void ExternalInterruptProgramRaisesHardwareIrq() {
        // Arrange & Act: the shared scenario runs the program and checks its oracle (one tick in RAM)
        using AsmRunScope scope = new(AsmFixtureCatalog.ExternalInt());
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, JitMode.InterpretedOnly);

        // Assert: engine-level device state of the finished interpreted run
        Machine machine = run.Machine;
        machine.CpuState.DX.Should().Be(1);
        machine.DualPic.GetPicSnapshot(DualPic.PicController.Primary).InterruptMaskRegister.Should().Be(0x00);

        PitChannelSnapshot snapshot = machine.Timer.GetChannelSnapshot(0);
        snapshot.Count.Should().Be(0x2251);
        snapshot.Mode.Should().Be(PitMode.SquareWave);
    }
}
