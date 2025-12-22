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
        mutedVolume.Left.Should().Be(0.0f, "attenuation below 4 maps to mute in DOSBox Staging");
        mutedVolume.Right.Should().Be(0.0f, "attenuation below 4 maps to mute in DOSBox Staging");

        mixer.CurrentAddress = MixerRegisters.DacVolume;
        mixer.Write(0xFF);

        AudioFrame maxVolume = pcmChannel.GetAppVolume();
        maxVolume.Left.Should().Be(1.0f, "0xFF maps to 31 which DOSBox Staging treats as unity gain");
        maxVolume.Right.Should().Be(1.0f, "0xFF maps to 31 which DOSBox Staging treats as unity gain");

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
        pcmVolume.Left.Should().Be(1.0f, "0xE0 maps to five-bit 28 which DOSBox treats as unity");
        pcmVolume.Right.Should().Be(0.0f, "0x08 maps to five-bit value 1 which DOSBox treats as muted");

        AudioFrame oplVolume = oplChannel.GetAppVolume();
        oplVolume.Left.Should().Be(1.0f);
        oplVolume.Right.Should().Be(0.0f);
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
