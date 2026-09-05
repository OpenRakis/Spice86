namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound;

using Xunit;

public sealed class GravisUltraSoundTests {
    private const ushort GusRegisterSelectPort = 0x343;
    private const ushort GusRegisterDataHighPort = 0x345;
    private const ushort GusVoiceIndexPort = 0x342;
    private const ushort GusIrqStatusPort = 0x246;
    private const ushort GusTimerControlPort = 0x249;
    private const ushort GusAdlibCommandPort = 0x24A;
    private const ushort AdlibCommandPort = 0x388;
    private const ushort GusMixControlPort = 0x240;
    private const byte ResetRegister = 0x4C;
    private const byte TimerControlRegister = 0x45;
    private const byte DmaControlRegister = 0x41;
    private const byte DmaAddressRegister = 0x42;
    private const byte DmaSamplingControlRegister = 0x49;
    private const byte RunningWithDacEnabled = 0x03;

    [Fact]
    public void ResetThenStartWithDacEnabled_EnablesMixerChannel() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        // Act
        gus.WriteByte(GusRegisterSelectPort, ResetRegister);
        gus.WriteByte(GusRegisterDataHighPort, 0);
        gus.WriteByte(GusRegisterSelectPort, ResetRegister);
        gus.WriteByte(GusRegisterDataHighPort, RunningWithDacEnabled);

        // Assert
        gus.Channel.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void EnabledTimer_RaisesIrqForEachEmulatedTimerPeriod() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        gus.WriteByte(GusRegisterSelectPort, TimerControlRegister);
        gus.WriteByte(GusRegisterDataHighPort, 0x04);
        gus.WriteByte(GusTimerControlPort, 0x01);

        // Act
        dependencyInjection.Machine.CpuState.Cycles += 100;
        dependencyInjection.Machine.EmulationLoopScheduler.ProcessEvents();
        byte firstStatus = gus.ReadByte(GusIrqStatusPort);

        dependencyInjection.Machine.CpuState.Cycles += 100;
        dependencyInjection.Machine.EmulationLoopScheduler.ProcessEvents();
        byte secondStatus = gus.ReadByte(GusIrqStatusPort);

        // Assert
        (firstStatus & 0x04).Should().Be(0x04);
        (secondStatus & 0x04).Should().Be(0x04);
    }

    [Fact]
    public void AdlibCommandWrite_IsMirroredToGus() {
        // Arrange
        using Spice86Creator creator = new("add", oplMode: OplMode.Opl3);
        using Spice86DependencyInjection dependencyInjection = creator.Create();

        // Act
        dependencyInjection.Machine.IoPortDispatcher.WriteByte(AdlibCommandPort, 0xA5);
        byte mirroredCommand = dependencyInjection.Machine.IoPortDispatcher.ReadByte(GusAdlibCommandPort);

        // Assert
        mirroredCommand.Should().Be(0xA5);
    }

    [Fact]
    public void IoWrite_RendersElapsedEmulatedTimeBeforeMutatingGusState() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        gus.WriteByte(GusRegisterSelectPort, ResetRegister);
        gus.WriteByte(GusRegisterDataHighPort, RunningWithDacEnabled);
        GusVoice voice = gus.Voices[0];
        voice.WaveStart = 0;
        voice.WaveEnd = 100_000;
        voice.WavePos = 0;
        voice.WaveInc = 512;
        voice.WaveState = 0;
        voice.VolPos = GravisUltraSound.VolumeIncScalar * (GravisUltraSound.VolLevels - 1);
        voice.VolState = 0;
        dependencyInjection.Machine.CpuState.Cycles += 1_000;

        // Act
        gus.WriteByte(GusMixControlPort, 0x0B);

        // Assert
        voice.WavePos.Should().BeGreaterThan(0);
    }

    [Fact]
    public void IoRead_RendersElapsedEmulatedTimeBeforeReportingGusState() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        gus.WriteByte(GusRegisterSelectPort, ResetRegister);
        gus.WriteByte(GusRegisterDataHighPort, RunningWithDacEnabled);
        GusVoice voice = gus.Voices[0];
        voice.WaveStart = 0;
        voice.WaveEnd = 100_000;
        voice.WavePos = 0;
        voice.WaveInc = 512;
        voice.WaveState = 0;
        voice.VolPos = GravisUltraSound.VolumeIncScalar * (GravisUltraSound.VolLevels - 1);
        voice.VolState = 0;
        dependencyInjection.Machine.CpuState.Cycles += 1_000;

        // Act
        gus.ReadByte(GusIrqStatusPort);

        // Assert
        voice.WavePos.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EnabledDma_TransfersHostMemoryToGusDramAtScheduledInterval() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");
        dependencyInjection.Machine.Memory[0] = 0x11;
        dependencyInjection.Machine.Memory[1] = 0x22;
        dependencyInjection.Machine.Memory[2] = 0x33;
        dependencyInjection.Machine.Memory[3] = 0x44;
        dependencyInjection.Machine.DmaSystem.WriteByte(0x0C, 0);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x06, 0);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x06, 0);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x07, 0x03);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x07, 0);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x82, 0);

        gus.WriteByte(GusRegisterSelectPort, DmaAddressRegister);
        gus.WriteWord(0x344, 0);
        gus.WriteByte(GusRegisterSelectPort, DmaControlRegister);
        gus.WriteByte(GusRegisterDataHighPort, 0x01);
        dependencyInjection.Machine.DmaSystem.WriteByte(0x0A, 0x03);

        // Act
        dependencyInjection.Machine.CpuState.Cycles += 1_000;
        dependencyInjection.Machine.EmulationLoopScheduler.ProcessEvents();

        // Assert
        gus.PeekDramByte(0).Should().Be(0x11);
        gus.PeekDramByte(1).Should().Be(0x22);
        gus.PeekDramByte(2).Should().Be(0x33);
        gus.PeekDramByte(3).Should().Be(0x44);
    }

    [Fact]
    public void DmaSamplingControlRegister_ReadsBackItsOwnValue() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        // Act
        gus.WriteByte(GusRegisterSelectPort, DmaSamplingControlRegister);
        gus.WriteByte(GusRegisterDataHighPort, 0xAA);
        ushort sampleControl = gus.ReadWord(0x344);

        // Assert
        sampleControl.Should().Be(0xAA00);
    }

    [Fact]
    public void VoiceIndexPort_SelectsVoiceForSubsequentRegisterWrites() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        // Act
        gus.WriteByte(GusVoiceIndexPort, 3);
        gus.WriteByte(GusRegisterSelectPort, 0x01);
        gus.WriteWord(0x344, 0x1234);

        // Assert
        gus.Voices[3].WaveRate.Should().Be(0x1234);
        gus.Voices[0].WaveRate.Should().Be(0);
    }

    [Fact]
    public void SchedulerTick_RendersGusVoicesWithoutFurtherPortAccess() {
        // Arrange
        using Spice86Creator creator = new("add");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        GravisUltraSound gus = dependencyInjection.Machine.GravisUltraSound
            ?? throw new InvalidOperationException("GUS should be enabled by default for the test harness.");

        gus.WriteByte(GusRegisterSelectPort, ResetRegister);
        gus.WriteByte(GusRegisterDataHighPort, RunningWithDacEnabled);
        GusVoice voice = gus.Voices[0];
        voice.WaveStart = 0;
        voice.WaveEnd = 100_000;
        voice.WavePos = 0;
        voice.WaveInc = 512;
        voice.WaveState = 0;
        voice.VolPos = GravisUltraSound.VolumeIncScalar * (GravisUltraSound.VolLevels - 1);
        voice.VolState = 0;

        // Act
        dependencyInjection.Machine.CpuState.Cycles += 1_000;
        dependencyInjection.Machine.EmulationLoopScheduler.ProcessEvents();

        // Assert
        voice.WavePos.Should().BeGreaterThan(0);
    }
}