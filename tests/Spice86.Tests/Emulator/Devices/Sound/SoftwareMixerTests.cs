namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System.Runtime.CompilerServices;

using Xunit;

public sealed class SoftwareMixerTests {
    /// <summary>Disposing the mixer must release its thread even when it is blocked waiting on an
    /// empty device queue: a consumer parked in <c>RWQueue.BulkDequeue</c> never observes
    /// <c>_threadShouldQuit</c>, so disposal has to stop the device queues through the registered
    /// notifiers before joining the mixer thread. A leaked thread roots the whole mixer.</summary>
    [Fact]
    public void DisposeReleasesTheMixerThreadBlockedOnAnEmptyDeviceQueue() {
        // Act: run a mixer whose thread is parked on the PC speaker queue, then dispose everything.
        WeakReference mixerReference = CreateDisposedMixer();

        // Assert: nothing keeps the disposed mixer alive.
        MachineLeakTracker.CollectUnreachable();
        mixerReference.IsAlive.Should().BeFalse(
            "the mixer thread must exit when the mixer is disposed, even while it is blocked on an empty device queue");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedMixer() {
        ILogger logger = Substitute.For<ILogger>();
        State state = new(CpuModel.ZET_86);
        IOPortDispatcher dispatcher = new(new AddressReadWriteBreakpoints(), state, logger, false);
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        DeviceScheduler scheduler = new(clock, logger, "test");
        SoftwareMixer mixer = new(Audio.Filters.AudioEngine.Dummy, new PauseHandler(logger));
        PcSpeaker speaker = new(mixer, state, dispatcher, logger, scheduler, clock, false);

        // Arrange: wake the channel so the mixer thread enters the pull callback and blocks on the
        // empty speaker queue. The callback resizes the queue away from its initial capacity.
        speaker.Channel.WakeUp();
        SpinWait.SpinUntil(() => speaker.OutputQueue.MaxCapacity != 256, TimeSpan.FromSeconds(5))
            .Should().BeTrue("the mixer thread must enter the pull callback before disposal");

        speaker.Dispose();
        mixer.Dispose();
        return new WeakReference(mixer);
    }
}
