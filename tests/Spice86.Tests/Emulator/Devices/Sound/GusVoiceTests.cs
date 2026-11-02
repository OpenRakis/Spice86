namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Audio.Common;

using Xunit;

public sealed class GusVoiceTests {
    private static GusVoice CreateVoice(byte volumeState, int volumeStart, int volumeEnd, int volumePosition,
        int volumeIncrement) {
        GusVoice voice = new(0, new GusVoiceIrq()) {
            WaveState = 0x01,
            VolState = volumeState,
            VolStart = volumeStart,
            VolEnd = volumeEnd,
            VolPos = volumePosition,
            VolInc = volumeIncrement
        };
        return voice;
    }

    [Theory]
    [InlineData((byte)0x08)]
    [InlineData((byte)0x18)]
    public void VolumeLoop_NormalizesLargeIncrements(byte state) {
        // Arrange
        int start = GravisUltraSound.VolumeIncScalar * (0xFE << 4);
        int end = GravisUltraSound.VolumeIncScalar * (0xFF << 4);
        GusVoice voice = CreateVoice(state, start, end, start, GravisUltraSound.VolumeIncScalar * 63);
        byte[] dram = new byte[GravisUltraSound.DramSizeBytes];
        float[] volumeScalars = new float[GravisUltraSound.VolLevels];
        AudioFrame[] panScalars = new AudioFrame[GravisUltraSound.PanPositions];
        AudioFrame[] frames = new AudioFrame[2];

        // Act
        voice.RenderFrames(dram, volumeScalars, panScalars, frames, frames.Length);

        // Assert
        voice.VolPos.Should().BeInRange(start, end);
    }

    [Fact]
    public void VolumeLoop_ZeroLengthLoopKeepsPositionAtTheEndpoint() {
        // Arrange
        int endpoint = GravisUltraSound.VolumeIncScalar * (0xFE << 4);
        GusVoice voice = CreateVoice(0x08, endpoint, endpoint, endpoint, GravisUltraSound.VolumeIncScalar * 63);
        byte[] dram = new byte[GravisUltraSound.DramSizeBytes];
        float[] volumeScalars = new float[GravisUltraSound.VolLevels];
        AudioFrame[] panScalars = new AudioFrame[GravisUltraSound.PanPositions];
        AudioFrame[] frames = new AudioFrame[2];

        // Act
        voice.RenderFrames(dram, volumeScalars, panScalars, frames, frames.Length);

        // Assert
        voice.VolPos.Should().Be(endpoint);
    }
}
