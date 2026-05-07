namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Replay;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Input.Joystick.Replay;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Replay;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;

public sealed class JoystickReplayHostTests {
    private readonly ILoggerService _loggerService;
    private readonly InputEventHub _hub;
    private readonly List<JoystickButtonEventArgs> _buttons = new();

    public JoystickReplayHostTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _loggerService.IsEnabled(Arg.Any<Serilog.Events.LogEventLevel>()).Returns(true);
        _hub = new InputEventHub(keyboardEvents: null, mouseEvents: null, joystickEvents: null);
        _hub.JoystickButtonChanged += (_, e) => _buttons.Add(e);
    }

    private static JoystickReplayScript Script(params JoystickReplayStep[] steps) {
        JoystickReplayScript s = new() { Name = "T" };
        s.Steps.AddRange(steps);
        return s;
    }

    private static JoystickReplayStep Btn(double delayMs, int idx, bool pressed) {
        return new JoystickReplayStep {
            DelayMs = delayMs,
            Type = JoystickReplayStepType.Button,
            ButtonIndex = idx,
            Pressed = pressed
        };
    }

    [Fact]
    public void Tick_BeforeStart_PostsNothingAndReturnsZero() {
        FakeTimeProvider clock = new(new DateTime(2024, 1, 1));
        JoystickReplayPlayer player = new(_hub, Script(Btn(0, 0, true)), _loggerService);
        JoystickReplayHost host = new(player, clock);

        int posted = host.Tick();

        posted.Should().Be(0);
        host.IsRunning.Should().BeFalse();
        player.NextStepIndex.Should().Be(0);
    }

    [Fact]
    public void Start_ThenTick_DispatchesDueStepsAccordingToElapsedTime() {
        FakeTimeProvider clock = new(new DateTime(2024, 1, 1));
        JoystickReplayPlayer player = new(_hub,
            Script(Btn(0, 0, true), Btn(50, 0, false), Btn(100, 1, true)),
            _loggerService);
        JoystickReplayHost host = new(player, clock);

        host.Start();
        host.IsRunning.Should().BeTrue();

        int p1 = host.Tick();
        clock.AdvanceMs(60);
        int p2 = host.Tick();
        _hub.ProcessAllPendingInputEvents();

        p1.Should().Be(1);
        p2.Should().Be(1);
        _buttons.Should().HaveCount(2);
        _buttons[0].ButtonIndex.Should().Be(0);
        _buttons[0].IsPressed.Should().BeTrue();
        _buttons[1].ButtonIndex.Should().Be(0);
        _buttons[1].IsPressed.Should().BeFalse();
    }

    [Fact]
    public void Tick_AfterScriptCompletes_StopsAutomatically() {
        FakeTimeProvider clock = new(new DateTime(2024, 1, 1));
        JoystickReplayPlayer player = new(_hub, Script(Btn(0, 0, true)), _loggerService);
        JoystickReplayHost host = new(player, clock);

        host.Start();
        clock.AdvanceMs(1);
        host.Tick();

        host.IsRunning.Should().BeFalse();
        player.IsFinished.Should().BeTrue();

        clock.AdvanceMs(1000);
        int extra = host.Tick();

        extra.Should().Be(0);
    }

    [Fact]
    public void Stop_DisarmsHost_LaterTicksAreNoOp() {
        FakeTimeProvider clock = new(new DateTime(2024, 1, 1));
        JoystickReplayPlayer player = new(_hub,
            Script(Btn(0, 0, true), Btn(50, 0, false)),
            _loggerService);
        JoystickReplayHost host = new(player, clock);

        host.Start();
        host.Tick();
        host.Stop();
        clock.AdvanceMs(100);
        int posted = host.Tick();

        posted.Should().Be(0);
        host.IsRunning.Should().BeFalse();
        player.NextStepIndex.Should().Be(1);
    }

    [Fact]
    public void Player_Property_ExposesUnderlyingPlayer() {
        FakeTimeProvider clock = new(new DateTime(2024, 1, 1));
        JoystickReplayPlayer player = new(_hub, Script(), _loggerService);
        JoystickReplayHost host = new(player, clock);

        host.Player.Should().BeSameAs(player);
    }
}
