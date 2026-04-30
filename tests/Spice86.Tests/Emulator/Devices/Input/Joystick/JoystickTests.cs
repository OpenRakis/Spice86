namespace Spice86.Tests.Emulator.Devices.Input.Joystick;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Emulator.Joystick;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for the IBM PC gameport joystick emulation.
/// </summary>
public class JoystickTests {
    private const ushort GamePort = 0x201;

    /// <summary>
    /// Test fixture that creates the minimal dependencies for joystick testing.
    /// </summary>
    private sealed class JoystickFixture {
        public ILoggerService Logger { get; }
        public State State { get; }
        public IOPortDispatcher Dispatcher { get; }
        public TestClock Clock { get; }
        public Joystick Joystick { get; }

        public JoystickFixture(IGuiJoystickEvents? joystickEvents = null) {
            Logger = Substitute.For<ILoggerService>();
            State = new State(CpuModel.INTEL_80286);
            AddressReadWriteBreakpoints breakpoints = new();
            Dispatcher = new IOPortDispatcher(breakpoints, State, Logger, false);
            Clock = new TestClock();
            Joystick = new Joystick(State, Dispatcher, Clock, false, Logger, joystickEvents);
        }
    }

    /// <summary>
    /// A test clock that allows manual control of elapsed time.
    /// </summary>
    private sealed class TestClock : IEmulatedClock {
        public double ElapsedTimeMs { get; set; }

        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime CurrentDateTime => StartTime + TimeSpan.FromMilliseconds(ElapsedTimeMs);

        public bool IsPaused => false;

        public void OnPause() { }

        public void OnResume() { }

        public void Dispose() { }
    }

    /// <summary>
    /// Test implementation of IGuiJoystickEvents for sending joystick input.
    /// </summary>
    private sealed class TestJoystickInput : IGuiJoystickEvents {
        public event EventHandler<JoystickStateEventArgs>? JoystickAStateChanged;
        public event EventHandler<JoystickStateEventArgs>? JoystickBStateChanged;

        public void SendJoystickAState(double axisX, double axisY, bool button1, bool button2) {
            JoystickAStateChanged?.Invoke(this, new JoystickStateEventArgs(axisX, axisY, button1, button2));
        }

        public void SendJoystickBState(double axisX, double axisY, bool button1, bool button2) {
            JoystickBStateChanged?.Invoke(this, new JoystickStateEventArgs(axisX, axisY, button1, button2));
        }
    }

    [Fact]
    public void ReadWithoutTrigger_NoJoystickConnected_ReturnsAllButtonsUp() {
        // Arrange
        JoystickFixture fixture = new();

        // Act - Read without having triggered the timers
        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // Assert: All buttons not pressed = bits 4-7 set (active low), no timers triggered = bits 0-3 clear
        result.Should().Be(0xF0);
    }

    [Fact]
    public void ReadAfterTrigger_NoJoystickConnected_AxisTimersNeverExpire() {
        // Arrange
        JoystickFixture fixture = new();
        fixture.Clock.ElapsedTimeMs = 0.0;

        // Act: Trigger the one-shot timers
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Immediately read - no joystick connected, axis bits stay high
        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // Bits 0-3 should be set (no joystick means timers never expire)
        // Bits 4-7 should be set (no buttons pressed, active low)
        result.Should().Be(0xFF);
    }

    [Fact]
    public void ReadAfterTrigger_JoystickACentered_TimersExpireAfterCorrectDuration() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        // Send centered joystick state (0.5, 0.5)
        input.SendJoystickAState(0.5, 0.5, false, false);

        // Act: Trigger the one-shot timers
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Read immediately - timers should still be running
        byte resultBeforeExpiry = fixture.Dispatcher.ReadByte(GamePort);
        // Bits 0-1 should be set (joystick A timers still running)
        // Bits 2-3 should be set (joystick B not connected, timers never expire)
        (resultBeforeExpiry & 0x03).Should().Be(0x03, "Joystick A axis timers should still be running");
        (resultBeforeExpiry & 0x0C).Should().Be(0x0C, "Joystick B axis timers should never expire (not connected)");

        // Advance time well past the timer duration (centered = ~74.5 μs = ~0.0745 ms)
        fixture.Clock.ElapsedTimeMs = 0.2;

        // Read again - timers should have expired
        byte resultAfterExpiry = fixture.Dispatcher.ReadByte(GamePort);
        // Bits 0-1 should be clear (joystick A timers expired)
        (resultAfterExpiry & 0x03).Should().Be(0x00, "Joystick A axis timers should have expired");
        // Bits 2-3 should still be set (joystick B not connected)
        (resultAfterExpiry & 0x0C).Should().Be(0x0C, "Joystick B timers should remain high (not connected)");
    }

    [Fact]
    public void ReadAfterTrigger_JoystickAFullLeft_TimersExpireQuickly() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        // Full left/up position (0.0, 0.0) - minimum timer duration (~24.2 μs)
        input.SendJoystickAState(0.0, 0.0, false, false);

        // Trigger
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Advance slightly past minimum timer duration (24.2 μs = 0.0242 ms)
        fixture.Clock.ElapsedTimeMs = 0.03;

        byte result = fixture.Dispatcher.ReadByte(GamePort);
        // Joystick A timers should have expired
        (result & 0x03).Should().Be(0x00, "Joystick A axis timers should expire quickly at minimum position");
    }

    [Fact]
    public void ReadAfterTrigger_JoystickAFullRight_TimersTakeLonger() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        // Full right/down position (1.0, 1.0) - maximum timer duration (~124.8 μs)
        input.SendJoystickAState(1.0, 1.0, false, false);

        // Trigger
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Advance past the minimum duration but before the maximum
        fixture.Clock.ElapsedTimeMs = 0.05;

        byte result = fixture.Dispatcher.ReadByte(GamePort);
        // Timers should still be running at full scale
        (result & 0x03).Should().Be(0x03, "Joystick A axis timers should still be running at max position");

        // Advance well past the maximum timer duration
        fixture.Clock.ElapsedTimeMs = 0.2;

        result = fixture.Dispatcher.ReadByte(GamePort);
        (result & 0x03).Should().Be(0x00, "Joystick A axis timers should have expired");
    }

    [Fact]
    public void ButtonsReadCorrectly_ActiveLow() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);

        // Press button 1 of joystick A, leave button 2 released
        input.SendJoystickAState(0.5, 0.5, true, false);

        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // Bit 4: button A1 pressed = 0, Bit 5: button A2 not pressed = 1
        (result & 0x10).Should().Be(0x00, "Button A1 should read as 0 when pressed (active low)");
        (result & 0x20).Should().Be(0x20, "Button A2 should read as 1 when not pressed (active low)");
    }

    [Fact]
    public void AllButtonsPressed_AllButtonBitsLow() {
        // Arrange
        TestJoystickInput inputA = new();
        JoystickFixture fixture = new(inputA);

        // Press both buttons on joystick A
        inputA.SendJoystickAState(0.5, 0.5, true, true);

        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // Bits 4-5 should be clear (both joystick A buttons pressed, active low)
        (result & 0x30).Should().Be(0x00, "Both joystick A buttons should be 0 when pressed");

        // Bits 6-7 should be set (joystick B buttons not pressed)
        (result & 0xC0).Should().Be(0xC0, "Joystick B buttons should be 1 when not pressed");
    }

    [Fact]
    public void JoystickBConnected_IndependentFromJoystickA() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        // Connect joystick B with different position and buttons
        input.SendJoystickAState(0.0, 0.0, false, false);
        input.SendJoystickBState(1.0, 1.0, true, true);

        // Trigger
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Advance past joystick A timer duration (min ~24.2 μs) but before B (max ~124.8 μs)
        fixture.Clock.ElapsedTimeMs = 0.05;

        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // Joystick A timers should have expired
        (result & 0x03).Should().Be(0x00, "Joystick A timers should have expired (min position)");

        // Joystick B timers should still be running
        (result & 0x0C).Should().Be(0x0C, "Joystick B timers should still be running (max position)");

        // Joystick A buttons not pressed = bits 4-5 high
        (result & 0x30).Should().Be(0x30, "Joystick A buttons not pressed");

        // Joystick B buttons pressed = bits 6-7 low
        (result & 0xC0).Should().Be(0x00, "Joystick B buttons pressed");
    }

    [Fact]
    public void Retrigger_ResetsTimers() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        input.SendJoystickAState(0.5, 0.5, false, false);

        // First trigger
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Wait for timers to expire
        fixture.Clock.ElapsedTimeMs = 1.0;
        byte result = fixture.Dispatcher.ReadByte(GamePort);
        (result & 0x03).Should().Be(0x00, "Timers should have expired");

        // Re-trigger at a later time
        fixture.Dispatcher.WriteByte(GamePort, 0x00);
        result = fixture.Dispatcher.ReadByte(GamePort);
        // After re-trigger, timers should be running again
        (result & 0x03).Should().Be(0x03, "Timers should be running again after re-trigger");
    }

    [Fact]
    public void AxisAsymmetry_DifferentXAndY() {
        // Arrange
        TestJoystickInput input = new();
        JoystickFixture fixture = new(input);
        fixture.Clock.ElapsedTimeMs = 0.0;

        // X axis at minimum, Y axis at maximum
        input.SendJoystickAState(0.0, 1.0, false, false);

        // Trigger
        fixture.Dispatcher.WriteByte(GamePort, 0x00);

        // Advance past X axis timer but before Y axis timer
        fixture.Clock.ElapsedTimeMs = 0.03;

        byte result = fixture.Dispatcher.ReadByte(GamePort);

        // X axis (bit 0) should have expired, Y axis (bit 1) should still be running
        (result & 0x01).Should().Be(0x00, "X axis timer should have expired (min position)");
        (result & 0x02).Should().Be(0x02, "Y axis timer should still be running (max position)");
    }

    [Fact]
    public void JoystickStateEventArgs_ClampValues() {
        // Test that axis values are clamped to 0.0-1.0 range
        JoystickStateEventArgs args = new(-0.5, 1.5, false, false);
        args.AxisX.Should().Be(0.0);
        args.AxisY.Should().Be(1.0);
    }
}
