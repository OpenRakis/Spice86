namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;

using Xunit;

[Trait("Category", "Sound")]
public sealed class EnvelopeTests {
    [Fact]
    public void Update_WithZeroInputs_DoesNotActivate() {
        Envelope envelope = new Envelope("test");

        envelope.Update(0, 100, 10, 1);

        AudioFrame frame = new AudioFrame(50.0f, 50.0f);
        envelope.Process(true, ref frame);

        frame.Left.Should().Be(50.0f);
        frame.Right.Should().Be(50.0f);
    }

    [Fact]
    public void Process_ClampsAndAdvancesEdge_ForMono() {
        Envelope envelope = new Envelope("mono");
        envelope.Update(1000, 100, 10, 1);

        AudioFrame frame1 = new AudioFrame(25.0f, 0.0f);
        envelope.Process(false, ref frame1);
        frame1.Left.Should().Be(10.0f);

        AudioFrame frame2 = new AudioFrame(25.0f, 0.0f);
        envelope.Process(false, ref frame2);
        frame2.Left.Should().Be(20.0f);
    }

    [Fact]
    public void Process_ClampsRightChannel_ForStereo() {
        Envelope envelope = new Envelope("stereo");
        envelope.Update(1000, 100, 10, 1);

        AudioFrame firstFrame = new AudioFrame(1.0f, 0.0f);
        envelope.Process(true, ref firstFrame);

        AudioFrame secondFrame = new AudioFrame(0.0f, 50.0f);
        envelope.Process(true, ref secondFrame);

        secondFrame.Left.Should().Be(0.0f);
        secondFrame.Right.Should().Be(20.0f);
    }

    [Fact]
    public void Process_DeactivatesAfterExpiration() {
        Envelope envelope = new Envelope("expire");
        envelope.Update(10, 100, 10, 1);

        AudioFrame startFrame = new AudioFrame(1.0f, 0.0f);
        envelope.Process(false, ref startFrame);

        for (int i = 0; i < 10; i++) {
            AudioFrame idleFrame = new AudioFrame(0.0f, 0.0f);
            envelope.Process(false, ref idleFrame);
        }

        AudioFrame afterExpire = new AudioFrame(50.0f, 0.0f);
        envelope.Process(false, ref afterExpire);

        afterExpire.Left.Should().Be(50.0f);
    }

    [Fact]
    public void Reactivate_ResetsEnvelopeState() {
        Envelope envelope = new Envelope("reactivate");
        envelope.Update(1000, 10, 1, 1);

        AudioFrame frame1 = new AudioFrame(20.0f, 0.0f);
        envelope.Process(false, ref frame1);
        frame1.Left.Should().Be(10.0f);

        AudioFrame frame2 = new AudioFrame(20.0f, 0.0f);
        envelope.Process(false, ref frame2);
        frame2.Left.Should().Be(20.0f);

        envelope.Reactivate();

        AudioFrame frame3 = new AudioFrame(20.0f, 0.0f);
        envelope.Process(false, ref frame3);
        frame3.Left.Should().Be(10.0f);
    }
}




