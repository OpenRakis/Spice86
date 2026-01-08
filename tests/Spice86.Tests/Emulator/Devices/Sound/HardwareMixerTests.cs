namespace Spice86.Tests.Emulator.Devices.Sound;

using System.Collections.Generic;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for <see cref="HardwareMixer"/> mirroring DOSBox Staging mixer register semantics.
/// Expectations are derived from DOSBox Staging mixer.cpp register handling.
/// </summary>
public class HardwareMixerTests {
    [Fact]
    public void ResetMatchesDosboxDefaults() {
        MixerChannel pcmChannel;
        MixerChannel oplChannel;
        HardwareMixer mixer = CreateMixer(new SoundBlasterHardwareConfig(7, 1, 5, SbType.SBPro2), out pcmChannel, out oplChannel);

        mixer.CurrentAddress = MixerRegisters.Reset;
        mixer.Write(0x00);

        mixer.CurrentAddress = MixerRegisters.MasterVolume;
        byte masterVolume = mixer.ReadData();
        mixer.CurrentAddress = MixerRegisters.DacVolume;
        byte dacVolume = mixer.ReadData();
        mixer.CurrentAddress = MixerRegisters.FmVolume;
        byte fmVolume = mixer.ReadData();
        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        byte outputSelect = mixer.ReadData();

        masterVolume.Should().Be(0xFF, "DOSBox Staging resets master volume to 0xFF");
        dacVolume.Should().Be(0xFF, "DSP voice volume resets to full scale");
        fmVolume.Should().Be(0xFF, "FM volume resets to full scale");
        outputSelect.Should().Be(0x11, "default is mono with filter enabled per DOSBox Staging");
        mixer.StereoEnabled.Should().BeFalse();

        AudioFrame pcmAppVolume = pcmChannel.GetAppVolume();
        pcmAppVolume.Left.Should().Be(1.0f);
        pcmAppVolume.Right.Should().Be(1.0f);

        AudioFrame oplAppVolume = oplChannel.GetAppVolume();
        oplAppVolume.Left.Should().Be(1.0f);
        oplAppVolume.Right.Should().Be(1.0f);
    }

    [Fact]
    public void DacVolumeWriteScalesMixerChannelLikeDosbox() {
        MixerChannel pcmChannel;
        MixerChannel oplChannel;
        HardwareMixer mixer = CreateMixer(new SoundBlasterHardwareConfig(7, 1, 5, SbType.SBPro2), out pcmChannel, out oplChannel);

        mixer.CurrentAddress = MixerRegisters.Reset;
        mixer.Write(0x00);

        mixer.CurrentAddress = MixerRegisters.DacVolume;
        mixer.Write(0x00);

        AudioFrame mutedVolume = pcmChannel.GetAppVolume();
        mutedVolume.Left.Should().Be(0.0f, "0x00 maps to volume 0 which is muted");
        mutedVolume.Right.Should().Be(0.0f, "0x00 maps to volume 0 which is muted");

        mixer.CurrentAddress = MixerRegisters.DacVolume;
        mixer.Write(0xFF);

        AudioFrame maxVolume = pcmChannel.GetAppVolume();
        maxVolume.Left.Should().Be(1.0f, "0xFF maps to volume 31 (count=0) which DOSBox calculates as 10^(-0.05*0) = 1.0");
        maxVolume.Right.Should().Be(1.0f, "0xFF maps to volume 31 (count=0) which DOSBox calculates as 10^(-0.05*0) = 1.0");

        AudioFrame oplVolume = oplChannel.GetAppVolume();
        oplVolume.Left.Should().Be(1.0f, "FM channel mirrors master volume scaling");
        oplVolume.Right.Should().Be(1.0f, "FM channel mirrors master volume scaling");
    }

    [Fact]
    public void Sb16VolumeRegistersRespectLeftRightMapping() {
        MixerChannel pcmChannel;
        MixerChannel oplChannel;
        HardwareMixer mixer = CreateMixer(new SoundBlasterHardwareConfig(10, 3, 7, SbType.Sb16), out pcmChannel, out oplChannel);

        mixer.CurrentAddress = MixerRegisters.Reset;
        mixer.Write(0x00);

        mixer.CurrentAddress = MixerRegisters.MasterVolumeLeft;
        mixer.Write(0xE0);
        mixer.CurrentAddress = MixerRegisters.MasterVolumeRight;
        mixer.Write(0x08);

        mixer.CurrentAddress = MixerRegisters.MasterVolumeLeft;
        byte left = mixer.ReadData();
        mixer.CurrentAddress = MixerRegisters.MasterVolumeRight;
        byte right = mixer.ReadData();

        left.Should().Be(0xE0, "SB16 stores five-bit volume shifted left per DOSBox Staging");
        right.Should().Be(0x08, "SB16 stores five-bit volume shifted left per DOSBox Staging");

        AudioFrame pcmVolume = pcmChannel.GetAppVolume();
        // Volume 28: count=3, db=3*2=6, 10^(-0.05*6) ≈ 0.501187
        pcmVolume.Left.Should().BeApproximately(0.501187f, 0.001f, "0xE0 >> 3 = 28, DOSBox SB16 calc: count=3, db=6, vol=0.501");
        // Volume 1: count=30, db=30*2=60, 10^(-0.05*60) ≈ 0.001
        pcmVolume.Right.Should().BeApproximately(0.001f, 0.001f, "0x08 >> 3 = 1, DOSBox SB16 calc: count=30, db=60, vol≈0.001");

        AudioFrame oplVolume = oplChannel.GetAppVolume();
        oplVolume.Left.Should().BeApproximately(0.501187f, 0.001f, "OPL mirrors master volume");
        oplVolume.Right.Should().BeApproximately(0.001f, 0.001f, "OPL mirrors master volume");
    }

    [Fact]
    public void MixerRegistersExposeIrqAndDmaSelections() {
        MixerChannel pcmChannel;
        MixerChannel oplChannel;
        HardwareMixer mixer = CreateMixer(new SoundBlasterHardwareConfig(10, 3, 7, SbType.Sb16), out pcmChannel, out oplChannel);

        mixer.CurrentAddress = MixerRegisters.IRQ;
        byte irq = mixer.ReadData();
        mixer.CurrentAddress = MixerRegisters.DMA;
        byte dma = mixer.ReadData();

        irq.Should().Be(0x08, "IRQ register encodes IRQ 10 on bit 3 following DOSBox Staging");
        dma.Should().Be(0x88, "DMA register encodes low DMA 3 (0x08) and high DMA 7 (0x80) like DOSBox Staging");
    }

    [Fact]
    public void OutputStereoSelectTracksStereoAndFilterBits() {
        MixerChannel pcmChannel;
        MixerChannel oplChannel;
        HardwareMixer mixer = CreateMixer(new SoundBlasterHardwareConfig(7, 1, 5, SbType.SBPro2), out pcmChannel, out oplChannel);

        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        mixer.Write(0x12);

        mixer.StereoEnabled.Should().BeTrue();
        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        byte stereoValue = mixer.ReadData();
        stereoValue.Should().Be(0x13, "bit 1 enables stereo with filter enabled when bit 5 is clear per DOSBox Staging");

        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        mixer.Write(0x22);
        mixer.StereoEnabled.Should().BeTrue();
        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        byte stereoFilterDisabled = mixer.ReadData();
        stereoFilterDisabled.Should().Be(0x33, "bit 5 disables the filter while keeping stereo enabled per DOSBox Staging");

        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        mixer.Write(0x20);
        mixer.StereoEnabled.Should().BeFalse();
        mixer.CurrentAddress = MixerRegisters.OutputStereoSelect;
        byte monoValue = mixer.ReadData();
        monoValue.Should().Be(0x31, "bit 5 cleared stereo and leaves filter disabled state reflected in the readback");
    }

    private static HardwareMixer CreateMixer(SoundBlasterHardwareConfig config, out MixerChannel pcmChannel, out MixerChannel oplChannel) {
        ILoggerService logger = Substitute.For<ILoggerService>();
        HashSet<ChannelFeature> features = new HashSet<ChannelFeature>(_defaultChannelFeatures);
        pcmChannel = new MixerChannel(_ => { }, "pcm", features, logger);
        oplChannel = new MixerChannel(_ => { }, "opl", new HashSet<ChannelFeature>(_defaultChannelFeatures), logger);
        return new HardwareMixer(config, pcmChannel, oplChannel, logger);
    }

    private static readonly ChannelFeature[] _defaultChannelFeatures = new[] { ChannelFeature.Stereo, ChannelFeature.Sleep };
}
