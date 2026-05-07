namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Interfaces;

using System;

using Xunit;

public sealed class GameportTests {
    private readonly FakeJoystickInput _input;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Gameport _gameport;
    private readonly IOPortDispatcher _dispatcher;

    public GameportTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        _dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(),
            state, logger, false);
        _input = new FakeJoystickInput();
        _timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _gameport = new Gameport(state, _dispatcher, _input, _timeProvider,
            rumbleSink: null, failOnUnhandledPort: false, loggerService: logger);
    }

    [Fact]
    public void NoStickConnected_Port201_Reads0xFF() {
        _input.Current = VirtualJoystickState.Disconnected;

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        read.Should().Be(0xFF);
    }

    [Fact]
    public void StickAConnectedNoButtonsBeforeAnyWrite_LowNibbleAxesCleared() {
        // Before any write the timer is disarmed -> axes are inactive,
        // so their bits are cleared. Buttons not pressed -> their bits stay set.
        VirtualStickState a = new(0f, 0f, 0f, 0f, Buttons: 0,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Stick A bits: AX (0x01) and AY (0x02) cleared -> 0xFC for those.
        // Stick A buttons not pressed: 0x10, 0x20 stay set.
        // Stick B disconnected: 0x04, 0x08, 0x40, 0x80 stay set.
        read.Should().Be(0xFC);
    }

    [Fact]
    public void StickAButtonsPressed_TheirBitsCleared() {
        VirtualStickState a = new(0f, 0f, 0f, 0f,
            Buttons: 0b0011, // button 1 + button 2 pressed
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes inactive (cleared) + buttons pressed (cleared) -> 0xCC
        // 0xFF & ~0x01 & ~0x02 & ~0x10 & ~0x20 = 0xCC
        read.Should().Be(0xCC);
    }

    [Fact]
    public void WriteToPort201_ArmsTimer_AxesBecomeActive() {
        VirtualStickState a = new(0f, 0f, 0f, 0f, 0,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        // No time advance: deadlines are in the future -> axes active.
        byte readImmediate = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes active -> their bits stay set. Buttons not pressed -> stay set.
        // Stick B disconnected -> its bits stay set. Result: 0xFF.
        readImmediate.Should().Be(0xFF);
    }

    [Fact]
    public void WriteToPort201_ThenAdvancePastDeadline_AxesBecomeInactive() {
        VirtualStickState a = new(-1f, -1f, 0f, 0f, 0,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        // axis = -1 -> deadline = 0 + 0 + offset = ~0.02 ms; advance 1ms.
        _timeProvider.AdvanceMs(1.0);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes inactive (low nibble cleared for stick A), B disconnected -> 0xFC.
        read.Should().Be(0xFC);
    }

    [Fact]
    public void WriteToPort201_ArmedWindowExpires_AxesGoInactive() {
        VirtualStickState a = new(1f, 1f, 0f, 0f, 0,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        // Advance past the 10ms legacy reset window: even though axis = +1
        // would normally still be firing, the watchdog disarms the timer.
        _timeProvider.AdvanceMs(GameportConstants.LegacyResetTimeoutTicks + 1);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        read.Should().Be(0xFC);
    }

    [Fact]
    public void PeekPort201_DoesNotRearmTimer() {
        VirtualStickState a = new(-1f, -1f, 0f, 0f, 0,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(a, VirtualStickState.Disconnected);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        _timeProvider.AdvanceMs(1.0);
        byte beforePeek = _dispatcher.ReadByte(GameportConstants.Port201);
        byte peek = _gameport.PeekPort201();
        byte afterPeek = _dispatcher.ReadByte(GameportConstants.Port201);

        peek.Should().Be(beforePeek);
        afterPeek.Should().Be(beforePeek);
    }

    [Fact]
    public void StickBConnectedAndButtons_RoutedToHighNibble() {
        VirtualStickState b = new(0f, 0f, 0f, 0f,
            Buttons: 0b0011,
            JoystickHatDirection.Centered, IsConnected: true);
        _input.Current = new VirtualJoystickState(VirtualStickState.Disconnected, b);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Stick A disconnected: 0x01, 0x02, 0x10, 0x20 stay set.
        // Stick B axes inactive (0x04, 0x08 cleared); buttons pressed (0x40, 0x80 cleared).
        // 0xFF & ~0x04 & ~0x08 & ~0x40 & ~0x80 = 0x33
        read.Should().Be(0x33);
    }

    [Fact]
    public void Gameport_ExposesInputSource_AndTimer() {
        _gameport.InputSource.Should().BeSameAs(_input);
        _gameport.Timer.Should().NotBeNull();
        _gameport.RumbleSink.Should().BeNull();
    }
}
