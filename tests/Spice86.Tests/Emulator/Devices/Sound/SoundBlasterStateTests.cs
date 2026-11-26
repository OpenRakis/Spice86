namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.VM;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="SoundBlaster.CaptureState" /> and <see cref="SoundBlaster.RestoreState" /> on a machine with
/// a Sound Blaster 16 at 220h, IRQ 7, DMA 1 and high DMA 5, on a clock of one millisecond per CPU cycle. The card
/// is driven and read through its ports.
/// </summary>
public sealed class SoundBlasterStateTests : IDisposable {
    private const ushort MixerIndexPort = 0x224;
    private const ushort MixerDataPort = 0x225;
    private const ushort DspResetPort = 0x226;
    private const ushort DspReadDataPort = 0x22A;
    private const ushort DspWriteDataPort = 0x22C;
    private const ushort DspReadStatusPort = 0x22E;
    private const ushort DmaChannel1AddressPort = 0x02;
    private const ushort DmaChannel1CountPort = 0x03;
    private const ushort DmaMaskPort = 0x0A;
    private const ushort DmaFlipFlopResetPort = 0x0C;
    private const ushort DmaChannel1PagePort = 0x83;

    private const byte MasterVolumeLeftRegister = 0x30;
    private const byte MixerResetRegister = 0x00;
    private const byte GetDspVersion = 0xE1;
    private const byte WriteTestRegister = 0xE4;
    private const byte ReadTestRegister = 0xE8;
    private const byte DmaIdentification = 0xE2;
    private const byte SingleCycle8BitDmaAdc = 0x24;
    private const byte OutputDataWaitingBit = 0x80;
    private const byte DspResetReply = 0xAA;
    private const byte Sb16VersionMinor = 0x05;
    private const byte DmaIdentificationOfZero = 0x40;
    private const byte UnmaskChannel1 = 0x01;
    private const uint DmaChannel1Target = 0x21000;
    private const byte DmaChannel1TargetPage = 0x02;
    private const byte DmaChannel1TargetAddressLow = 0x00;
    private const byte DmaChannel1TargetAddressHigh = 0x10;

    private readonly Spice86Creator _creator;
    private readonly Spice86DependencyInjection _dependencyInjection;
    private readonly Machine _machine;

    public SoundBlasterStateTests() {
        _creator = new Spice86Creator("externalint", sbType: SbType.Sb16, instructionTimeScale: 1000);
        _dependencyInjection = _creator.Create();
        _machine = _dependencyInjection.Machine;
    }

    public void Dispose() {
        _dependencyInjection.Dispose();
        _creator.Dispose();
    }

    [Fact]
    public void RestoreAfterResetsGivesTheCapturedDspRepliesAndMixerRegisters() {
        // Arrange
        SoundBlasterState captured = SetUpDspAndMixer();

        // Act
        ResetDspAndMixer();
        _machine.SoundBlaster.RestoreState(captured);

        // Assert
        AssertDspAndMixerAsSetUp();
    }

    [Fact]
    public void RestoringTheSameStateTwiceGivesTheSameReplies() {
        // Arrange
        SoundBlasterState captured = SetUpDspAndMixer();

        // Act
        _machine.SoundBlaster.RestoreState(captured);
        AssertDspAndMixerAsSetUp();
        ResetDspAndMixer();
        _machine.SoundBlaster.RestoreState(captured);

        // Assert
        AssertDspAndMixerAsSetUp();
    }

    [Fact]
    public void StateReadBackFromJsonGivesTheCapturedReplies() {
        // Arrange
        SoundBlasterState captured = SetUpDspAndMixer();
        string json = JsonSerializer.Serialize(captured);

        // Act
        ResetDspAndMixer();
        SoundBlasterState? deserialized = JsonSerializer.Deserialize<SoundBlasterState>(json);
        _machine.SoundBlaster.RestoreState(deserialized ?? throw new InvalidOperationException("No state."));

        // Assert
        AssertDspAndMixerAsSetUp();
    }

    [Fact]
    public void PendingResetReplyArrivesAfterItsDelayFromTheRestore() {
        // Arrange: the reset bit was cleared, so the reply AAh is queued 20 microseconds ahead
        _machine.IoPortDispatcher.WriteByte(DspResetPort, 1);
        _machine.IoPortDispatcher.WriteByte(DspResetPort, 0);
        SoundBlasterState captured = _machine.SoundBlaster.CaptureState();
        AdvanceOneMillisecondAndProcessEvents();
        _machine.IoPortDispatcher.ReadByte(DspReadDataPort).Should().Be(DspResetReply);

        // Act
        _machine.SoundBlaster.RestoreState(captured);
        byte statusRightAfterRestore = _machine.IoPortDispatcher.ReadByte(DspReadStatusPort);
        _machine.EmulationLoopScheduler.ProcessEvents();
        byte statusBeforeTheDelay = _machine.IoPortDispatcher.ReadByte(DspReadStatusPort);
        AdvanceOneMillisecondAndProcessEvents();
        byte statusAfterTheDelay = _machine.IoPortDispatcher.ReadByte(DspReadStatusPort);

        // Assert
        (statusRightAfterRestore & OutputDataWaitingBit).Should().Be(0);
        (statusBeforeTheDelay & OutputDataWaitingBit).Should().Be(0);
        (statusAfterTheDelay & OutputDataWaitingBit).Should().Be(OutputDataWaitingBit);
        _machine.IoPortDispatcher.ReadByte(DspReadDataPort).Should().Be(DspResetReply);
    }

    [Fact]
    public void RestoredDmaIdentificationCallbackWritesItsByteWhenTheChannelIsUnmasked() {
        // Arrange: DSP command E2h waits for DMA channel 1 to be unmasked
        ProgramMaskedDmaChannel1();
        WriteDsp(DmaIdentification, 0x00);
        SoundBlasterState captured = _machine.SoundBlaster.CaptureState();

        // Act: a recording command replaces the callback, then the restore puts E2h's back
        WriteDsp(SingleCycle8BitDmaAdc, 0x00, 0x00);
        _machine.SoundBlaster.RestoreState(captured);
        _machine.IoPortDispatcher.WriteByte(DmaMaskPort, UnmaskChannel1);

        // Assert
        _machine.Memory[DmaChannel1Target].Should().Be(DmaIdentificationOfZero);
    }

    private SoundBlasterState SetUpDspAndMixer() {
        WriteDsp(GetDspVersion);
        _machine.IoPortDispatcher.ReadByte(DspReadDataPort);
        WriteDsp(WriteTestRegister);
        _machine.IoPortDispatcher.WriteByte(MixerIndexPort, MasterVolumeLeftRegister);
        _machine.IoPortDispatcher.WriteByte(MixerDataPort, 0x40);
        return _machine.SoundBlaster.CaptureState();
    }

    private void ResetDspAndMixer() {
        _machine.IoPortDispatcher.WriteByte(DspResetPort, 1);
        _machine.IoPortDispatcher.WriteByte(DspResetPort, 0);
        AdvanceOneMillisecondAndProcessEvents();
        _machine.IoPortDispatcher.WriteByte(MixerIndexPort, MixerResetRegister);
        _machine.IoPortDispatcher.WriteByte(MixerDataPort, 0);
    }

    /// <summary>
    /// The second version byte still waits in the output buffer, the test register command still waits for
    /// its parameter, and the mixer index and master volume are as written.
    /// </summary>
    private void AssertDspAndMixerAsSetUp() {
        (_machine.IoPortDispatcher.ReadByte(DspReadStatusPort) & OutputDataWaitingBit).Should().Be(OutputDataWaitingBit);
        _machine.IoPortDispatcher.ReadByte(DspReadDataPort).Should().Be(Sb16VersionMinor);
        _machine.IoPortDispatcher.ReadByte(MixerIndexPort).Should().Be(MasterVolumeLeftRegister);
        _machine.IoPortDispatcher.ReadByte(MixerDataPort).Should().Be(0x40);
        WriteDsp(0x77);
        WriteDsp(ReadTestRegister);
        _machine.IoPortDispatcher.ReadByte(DspReadDataPort).Should().Be(0x77);
    }

    private void ProgramMaskedDmaChannel1() {
        _machine.IoPortDispatcher.WriteByte(DmaFlipFlopResetPort, 0);
        _machine.IoPortDispatcher.WriteByte(DmaChannel1PagePort, DmaChannel1TargetPage);
        _machine.IoPortDispatcher.WriteByte(DmaChannel1AddressPort, DmaChannel1TargetAddressLow);
        _machine.IoPortDispatcher.WriteByte(DmaChannel1AddressPort, DmaChannel1TargetAddressHigh);
        _machine.IoPortDispatcher.WriteByte(DmaChannel1CountPort, 0);
        _machine.IoPortDispatcher.WriteByte(DmaChannel1CountPort, 0);
    }

    private void WriteDsp(params byte[] values) {
        foreach (byte value in values) {
            _machine.IoPortDispatcher.WriteByte(DspWriteDataPort, value);
        }
    }

    private void AdvanceOneMillisecondAndProcessEvents() {
        _machine.CpuState.Cycles++;
        _machine.EmulationLoopScheduler.ProcessEvents();
    }
}
