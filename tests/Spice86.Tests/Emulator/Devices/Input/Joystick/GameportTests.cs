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
    private readonly FakeJoystickEventSource _events;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Gameport _gameport;
    private readonly IOPortDispatcher _dispatcher;

    public GameportTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        _dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(),
            state, logger, false);
        _events = new FakeJoystickEventSource();
        _timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _gameport = new Gameport(state, _dispatcher, _events, _timeProvider,
            rumbleSink: null, failOnUnhandledPort: false, loggerService: logger);
    }

    [Fact]
    public void NoConnectionEventEverRaised_Port201_Reads0xFF() {
        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        read.Should().Be(0xFF);
    }

    [Fact]
    public void StickAConnectedNoButtonsBeforeAnyWrite_LowNibbleAxesCleared() {
        _events.RaiseConnect(0);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Stick A axes inactive (0x01, 0x02 cleared); buttons not pressed (0x10, 0x20 set);
        // stick B disconnected (its bits stay set). 0xFF & ~0x01 & ~0x02 = 0xFC.
        read.Should().Be(0xFC);
    }

    [Fact]
    public void StickAButtonEvents_FlipTheirBits() {
        _events.RaiseConnect(0);
        _events.RaiseButton(0, 0, true);
        _events.RaiseButton(0, 1, true);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes inactive + buttons pressed -> 0xFF & ~0x01 & ~0x02 & ~0x10 & ~0x20 = 0xCC.
        read.Should().Be(0xCC);

        // Releasing button 0 brings 0x10 back.
        _events.RaiseButton(0, 0, false);
        _dispatcher.ReadByte(GameportConstants.Port201).Should().Be(0xDC);
    }

    [Fact]
    public void WriteToPort201_ArmsTimer_AxesBecomeActive() {
        _events.RaiseConnect(0);
        // Stick A centred (X=0, Y=0).

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);

        // No time advance: deadlines are in the future -> axes active = bits set.
        byte readImmediate = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes active, buttons not pressed, B disconnected -> 0xFF.
        readImmediate.Should().Be(0xFF);
    }

    [Fact]
    public void WriteToPort201_ThenAdvancePastDeadline_AxesBecomeInactive() {
        _events.RaiseConnect(0);
        _events.RaiseAxis(0, JoystickAxis.X, -1f);
        _events.RaiseAxis(0, JoystickAxis.Y, -1f);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        // axis = -1 -> deadline = 0 + 0 + offset (~0.02 ms); advance 1 ms.
        _timeProvider.AdvanceMs(1.0);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Axes inactive, B disconnected -> 0xFC.
        read.Should().Be(0xFC);
    }

    [Fact]
    public void ArmedWindowExpires_AxesGoInactive() {
        _events.RaiseConnect(0);
        _events.RaiseAxis(0, JoystickAxis.X, 1f);
        _events.RaiseAxis(0, JoystickAxis.Y, 1f);

        _dispatcher.WriteByte(GameportConstants.Port201, 0x00);
        // Even though axis = +1 would normally still be firing, the 10 ms watchdog disarms.
        _timeProvider.AdvanceMs(GameportConstants.LegacyResetTimeoutTicks + 1);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        read.Should().Be(0xFC);
    }

    [Fact]
    public void PeekPort201_DoesNotRearmTimer() {
        _events.RaiseConnect(0);
        _events.RaiseAxis(0, JoystickAxis.X, -1f);
        _events.RaiseAxis(0, JoystickAxis.Y, -1f);

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
        _events.RaiseConnect(1);
        _events.RaiseButton(1, 0, true);
        _events.RaiseButton(1, 1, true);

        byte read = _dispatcher.ReadByte(GameportConstants.Port201);

        // Stick A disconnected: 0x01, 0x02, 0x10, 0x20 stay set.
        // Stick B axes inactive (0x04, 0x08 cleared); buttons pressed (0x40, 0x80 cleared).
        read.Should().Be(0x33);
    }

    [Fact]
    public void HatEvent_UpdatesSnapshot() {
        _events.RaiseConnect(0);
        _events.RaiseHat(0, JoystickHatDirection.Up);

        _gameport.GetCurrentState().StickA.Hat.Should().Be(JoystickHatDirection.Up);
    }

    [Fact]
    public void DisconnectEvent_FullyResetsStickState() {
        _events.RaiseConnect(0);
        _events.RaiseAxis(0, JoystickAxis.X, 0.5f);
        _events.RaiseButton(0, 0, true);

        _events.RaiseDisconnect(0);

        VirtualJoystickState snapshot = _gameport.GetCurrentState();
        snapshot.StickA.Should().Be(VirtualStickState.Disconnected);
    }

    [Fact]
    public void EventForOutOfRangeStickIndex_IsIgnored() {
        _events.RaiseConnect(0);
        _events.RaiseAxis(stickIndex: 5, JoystickAxis.X, 1f);
        _events.RaiseButton(stickIndex: -1, buttonIndex: 0, pressed: true);

        VirtualJoystickState snapshot = _gameport.GetCurrentState();
        snapshot.StickA.X.Should().Be(0f);
        snapshot.StickA.Buttons.Should().Be(0);
    }

    [Fact]
    public void OutOfRangeButtonIndex_IsIgnored() {
        _events.RaiseConnect(0);
        _events.RaiseButton(0, 7, true);

        _gameport.GetCurrentState().StickA.Buttons.Should().Be(0);
    }

    [Fact]
    public void Dispose_StopsConsumingEvents() {
        _events.RaiseConnect(0);

        _gameport.Dispose();
        // After dispose, further events must not change observable state.
        _events.RaiseAxis(0, JoystickAxis.X, 1f);

        _gameport.GetCurrentState().StickA.X.Should().Be(0f);
    }

    [Fact]
    public void Gameport_ExposesTimerAndOptionalRumbleSink() {
        _gameport.Timer.Should().NotBeNull();
        _gameport.RumbleSink.Should().BeNull();
    }
}

