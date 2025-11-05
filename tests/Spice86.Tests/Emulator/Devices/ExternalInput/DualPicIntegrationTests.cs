namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.VM;

using Xunit;

public sealed class DualPicIntegrationTests {
    public static IEnumerable<object[]> CfgCpuConfigurations() {
        yield return [false];
        yield return [true];
    }

    [Theory]
    [MemberData(nameof(CfgCpuConfigurations))]
    public void ExternalInterruptProgramRaisesHardwareIrq(bool enableCfgCpu) {
        using Spice86DependencyInjection spice86 = new Spice86Creator(
            "externalint",
            enableCfgCpu,
            true,
            maxCycles: 0x0FFFFFFF,
            installInterruptVectors: false).Create();

        spice86.ProgramExecutor.Run();

        Machine machine = spice86.Machine;
        machine.CpuState.DX.Should().Be(1);
        machine.DualPic.Ticks.Should().BeGreaterThan(0u);
        machine.DualPic.GetChannelSnapshot(DualPic.PicChannel.Primary).InterruptMaskRegister.Should().Be(0x00);

        PitChannelSnapshot snapshot = machine.Timer.GetChannelSnapshot(0);
        snapshot.Count.Should().Be(0x2251);
        snapshot.Mode.Should().Be(PitMode.SquareWave);
    }
}