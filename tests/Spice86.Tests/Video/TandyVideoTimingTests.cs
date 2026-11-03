namespace Spice86.Tests.Video;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Platform;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System.Threading;

using Xunit;

public sealed class TandyVideoTimingTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateFormatChangeRunsOnSchedulerThread(bool writeFromOtherThread) {
        // Arrange
        VideoHarness video = new();
        video.PumpAt(0);

        // Act
        if (writeFromOtherThread) {
            Thread writer = new(() => video.WriteFormat(0x10));
            writer.Start();
            writer.Join();
            video.Tandy.Scanout.BitsPerPixel.Should().Be(2);
            video.PumpAt(0);
        } else {
            video.WriteFormat(0x10);
        }

        // Assert
        video.Tandy.Scanout.BitsPerPixel.Should().Be(4);
    }

    [Fact]
    public void DelayedResizeKeepsRequestDeadlineAndCoalescesRegisterWrites() {
        // Arrange
        VideoHarness video = new();
        video.PumpAt(0);
        video.Clock.ElapsedTimeMs.Returns(10.0);
        Thread writer = new(() => video.WriteDisplayedRows(90));
        writer.Start();
        writer.Join();

        // Act
        video.PumpAt(20);
        video.Clock.ElapsedTimeMs.Returns(30.0);
        video.WriteDisplayedRows(80);
        video.PumpAt(59);
        video.Tandy.Scanout.Height.Should().Be(200);
        video.PumpAt(60);

        // Assert
        video.Tandy.Scanout.Height.Should().Be(160);
    }

    private sealed class VideoHarness {
        private readonly IOPortDispatcher _ports;
        private readonly DeviceScheduler _scheduler;

        public VideoHarness() {
            MachineProfile profile = MachineProfile.For(MachineType.Tandy1000);
            VideoState state = new(profile);
            Tandy = state.Tandy ?? throw new InvalidOperationException("Missing native video state.");
            Tandy.Crtc[0] = 56;
            Tandy.Crtc[1] = 40;
            Tandy.Crtc[4] = 130;
            Tandy.Crtc[6] = 100;
            Tandy.Crtc[7] = 112;
            Tandy.Crtc[9] = 1;
            Tandy.WriteMode(0x0B);
            State cpu = new(CpuModel.INTEL_80286);
            _ports = new(new(), cpu, NullLogger.Instance, true);
            Clock = Substitute.For<IEmulatedClock>();
            _ = new VgaIoPortHandler(cpu, _ports, NullLogger.Instance, state, Clock, true, profile);
            Memory memory = new(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
            VgaBlinkState blink = new();
            Renderer renderer = new(memory, state, blink, NullLogger.Instance, new VgaRenderer256ColorScalar(), profile);
            _scheduler = new(Clock, NullLogger.Instance, "Tandy video test");
            _ = new VgaTimingEngine(state, _scheduler, Clock, renderer, blink);
        }

        public TandyVideoState Tandy { get; }
        public IEmulatedClock Clock { get; }

        public void WriteFormat(byte value) {
            _ports.WriteByte(0x3DA, 3);
            _ports.WriteByte(0x3DE, value);
        }

        public void WriteDisplayedRows(byte value) {
            _ports.WriteByte(0x3D4, 6);
            _ports.WriteByte(0x3D5, value);
        }

        public void PumpAt(double timeMs) {
            Clock.ElapsedTimeMs.Returns(timeMs);
            do {
                _scheduler.ProcessEvents();
            } while (_scheduler.NextEventTime is double next && next <= timeMs);
        }
    }
}