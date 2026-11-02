namespace Spice86.Tests.Emulator.Devices.Keyboard;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Xunit;

/// <summary>
/// Device-level tests for the PS/2 keyboard's held-key bookkeeping and its ability to reconcile
/// (release) keys whose key-up was lost. A lost BREAK scancode otherwise latches a modifier in the
/// guest forever, so releasing all held keys is the recovery path used on pause, window deactivation,
/// and buffer overflow.
/// </summary>
public sealed class PS2KeyboardReleaseAllKeysTests {
    private const ushort DataPort = 0x60;
    private const ushort StatusPort = 0x64;

    /// <summary>
    /// A fully controllable emulated clock: the test advances time explicitly instead of the real
    /// stopwatch, so the 8042's 0.3ms inter-byte delay fires deterministically.
    /// </summary>
    private sealed class ControllableClock : ClockBase {
        private readonly Stopwatch _stopwatch = new();
        private double _offsetMs;

        public ControllableClock() : base(ClockJitter.Create(null), DateTimeOffset.UtcNow) {
            _stopwatch.Start();
        }

        public override double ElapsedTimeMs => _offsetMs + _stopwatch.Elapsed.TotalMilliseconds;
        protected override void OnPauseCore() => _stopwatch.Stop();
        protected override void OnResumeCore() => _stopwatch.Start();
        protected override void OnDisposeCore() => _stopwatch.Stop();

        public void Advance(double ms) => _offsetMs += ms;
    }

    private sealed class KeyboardFixture {
        private readonly ControllableClock _clock = new();
        private readonly DeviceScheduler _scheduler;

        public KeyboardFixture() {
            Logger = Substitute.For<ILogger>();
            CpuState = new State(CpuModel.ZET_86);
            IOPortDispatcher dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), CpuState, Logger, false);
            DualPic = new DualPic(dispatcher, CpuState, Logger, false);
            _scheduler = new DeviceScheduler(_clock, Logger, "test");
            Controller = new Intel8042Controller(CpuState, dispatcher, new A20Gate(true), DualPic,
                _scheduler, failOnUnhandledPort: false, Logger);
        }

        public ILogger Logger { get; }
        public State CpuState { get; }
        public DualPic DualPic { get; }
        public Intel8042Controller Controller { get; }
        public PS2Keyboard Keyboard => Controller.KeyboardDevice;

        /// <summary>
        /// Advances the clock enough for the 8042's inter-byte delay to elapse, then pumps the scheduler
        /// so the pending transfer fires.
        /// </summary>
        public void Pump() {
            _clock.Advance(1);
            _scheduler.ProcessEvents();
        }

        /// <summary>
        /// Reads all keyboard bytes currently delivered by the controller (what the guest does in INT9).
        /// Each read clears the pending-data flag and lets the controller hand over the next buffered
        /// byte, so this drains the entire pending stream.
        /// </summary>
        public byte[] DrainKeyboard() {
            List<byte> result = new();
            int guard = 4096;
            while (guard-- > 0 && (Controller.ReadByte(StatusPort) & 0x01) != 0) {
                result.Add(Controller.ReadByte(DataPort));
                Pump();
            }

            return result.ToArray();
        }
    }

    [Fact]
    public void ReleaseAllKeys_EmitsBreakFrameForHeldKey() {
        KeyboardFixture fixture = new();
        PS2Keyboard keyboard = fixture.Keyboard;

        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftShift, isPressed: true);
        fixture.Pump();
        keyboard.ReleaseAllKeys();

        byte[] bytes = fixture.DrainKeyboard();

        // The make frame (0x2A) plus a BREAK (0xAA) are both delivered; a release must not re-emit a make.
        bytes.Should().Contain((byte)0x2A);
        bytes.Should().Contain((byte)0xAA);
    }

    [Fact]
    public void ReleaseAllKeys_EmitsBreakFramesInDeterministicEnumOrder() {
        KeyboardFixture fixture = new();
        PS2Keyboard keyboard = fixture.Keyboard;

        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftShift, isPressed: true);
        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftCtrl, isPressed: true);
        keyboard.EnqueueKeyEvent(PcKeyboardKey.A, isPressed: true);
        fixture.Pump();
        keyboard.ReleaseAllKeys();

        byte[] bytes = fixture.DrainKeyboard();

        int a = Array.IndexOf(bytes, (byte)0x9E);   // A break
        int ctrl = Array.IndexOf(bytes, (byte)0x9D); // LeftCtrl break
        int shift = Array.IndexOf(bytes, (byte)0xAA); // LeftShift break

        a.Should().BeGreaterThanOrEqualTo(0);
        ctrl.Should().BeGreaterThanOrEqualTo(0);
        shift.Should().BeGreaterThanOrEqualTo(0);
        // Release order follows the PcKeyboardKey enum: A < LeftCtrl < LeftShift.
        a.Should().BeLessThan(ctrl);
        ctrl.Should().BeLessThan(shift);
    }

    [Fact]
    public void ReleaseAllKeys_WhenNothingHeld_EmitsNothing() {
        KeyboardFixture fixture = new();

        fixture.Keyboard.ReleaseAllKeys();

        fixture.DrainKeyboard().Should().BeEmpty();
    }

    [Fact]
    public void ReleaseAllKeys_IsIdempotent() {
        KeyboardFixture fixture = new();
        PS2Keyboard keyboard = fixture.Keyboard;

        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftAlt, isPressed: true);
        fixture.Pump();
        keyboard.ReleaseAllKeys();
        byte[] first = fixture.DrainKeyboard();

        keyboard.ReleaseAllKeys();
        byte[] second = fixture.DrainKeyboard();

        first.Should().Contain((byte)0xB8); // LeftAlt break
        second.Should().BeEmpty();          // no held keys remain, so nothing more is released
    }

    [Fact]
    public void NormalKeyRelease_StopsBeingTrackedAsHeld() {
        KeyboardFixture fixture = new();
        PS2Keyboard keyboard = fixture.Keyboard;

        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftShift, isPressed: true);
        keyboard.EnqueueKeyEvent(PcKeyboardKey.LeftShift, isPressed: false);
        fixture.Pump();
        byte[] released = fixture.DrainKeyboard();

        keyboard.ReleaseAllKeys();
        byte[] afterRelease = fixture.DrainKeyboard();

        released.Should().Contain((byte)0x2A); // normal make
        released.Should().Contain((byte)0xAA); // normal key-up delivered the break
        afterRelease.Should().BeEmpty();       // the key is no longer tracked as held
    }

    [Fact]
    public void MomentaryKeys_AreNotTrackedForRelease() {
        KeyboardFixture fixture = new();
        PS2Keyboard keyboard = fixture.Keyboard;

        // Pause has no BREAK frame, so it must not be tracked as a held key.
        keyboard.EnqueueKeyEvent(PcKeyboardKey.Pause, isPressed: true);
        fixture.Pump();
        byte[] press = fixture.DrainKeyboard();

        keyboard.ReleaseAllKeys();
        byte[] release = fixture.DrainKeyboard();

        press.Should().NotBeEmpty(); // the pause make-chord is still delivered as usual
        release.Should().BeEmpty();  // releasing all emits no break for the momentary pause key
    }
}
