namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Replay;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Input.Joystick.Replay;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Replay;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Xunit;

public sealed class JoystickReplayPlayerTests {
    private readonly ILoggerService _loggerService;
    private readonly InputEventHub _hub;

    private readonly List<JoystickAxisEventArgs> _axisEvents = new();
    private readonly List<JoystickButtonEventArgs> _buttonEvents = new();
    private readonly List<JoystickHatEventArgs> _hatEvents = new();
    private readonly List<JoystickConnectionEventArgs> _connectionEvents = new();

    public JoystickReplayPlayerTests() {
        _loggerService = Substitute.For<ILoggerService>();
        _loggerService.IsEnabled(Arg.Any<Serilog.Events.LogEventLevel>()).Returns(true);
        _hub = new InputEventHub(keyboardEvents: null, mouseEvents: null, joystickEvents: null);
        _hub.JoystickAxisChanged += (_, e) => _axisEvents.Add(e);
        _hub.JoystickButtonChanged += (_, e) => _buttonEvents.Add(e);
        _hub.JoystickHatChanged += (_, e) => _hatEvents.Add(e);
        _hub.JoystickConnectionChanged += (_, e) => _connectionEvents.Add(e);
    }

    private static JoystickReplayScript ScriptOf(params JoystickReplayStep[] steps) {
        JoystickReplayScript script = new() { Name = "Test" };
        script.Steps.AddRange(steps);
        return script;
    }

    private void Pump() {
        _hub.ProcessAllPendingInputEvents();
    }

    [Fact]
    public void EmptyScript_IsFinishedFromStart() {
        JoystickReplayPlayer player = new(_hub, new JoystickReplayScript(), _loggerService);

        player.IsFinished.Should().BeTrue();
        player.StepCount.Should().Be(0);
        player.TotalDurationMs.Should().Be(0);
        player.AdvanceTo(1000).Should().Be(0);
    }

    [Fact]
    public void AdvanceTo_BeforeFirstDeadline_PostsNothing() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 100,
                Type = JoystickReplayStepType.Connect,
                StickIndex = 0,
                DeviceName = "Pad",
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        int posted = player.AdvanceTo(50);

        posted.Should().Be(0);
        player.IsFinished.Should().BeFalse();
        player.NextStepIndex.Should().Be(0);
        Pump();
        _connectionEvents.Should().BeEmpty();
    }

    [Fact]
    public void AdvanceTo_AtDeadline_PostsExactlyOneStep() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 100,
                Type = JoystickReplayStepType.Connect,
                StickIndex = 0,
                DeviceName = "Pad",
            },
            new JoystickReplayStep {
                DelayMs = 100,
                Type = JoystickReplayStepType.Disconnect,
                StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        int posted = player.AdvanceTo(100);

        posted.Should().Be(1);
        player.NextStepIndex.Should().Be(1);
        player.IsFinished.Should().BeFalse();
        Pump();
        _connectionEvents.Should().ContainSingle();
        _connectionEvents[0].IsConnected.Should().BeTrue();
        _connectionEvents[0].DeviceName.Should().Be("Pad");
    }

    [Fact]
    public void AdvanceTo_PastEverything_DrainsTheWholeScript() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 0, Type = JoystickReplayStepType.Connect,
                StickIndex = 0, DeviceName = "Pad",
            },
            new JoystickReplayStep {
                DelayMs = 100, Type = JoystickReplayStepType.Axis,
                StickIndex = 0, Axis = JoystickAxis.X, Value = 1f,
            },
            new JoystickReplayStep {
                DelayMs = 50, Type = JoystickReplayStepType.Button,
                StickIndex = 0, ButtonIndex = 0, Pressed = true,
            },
            new JoystickReplayStep {
                DelayMs = 25, Type = JoystickReplayStepType.Hat,
                StickIndex = 0, Direction = JoystickHatDirection.Up,
            },
            new JoystickReplayStep {
                DelayMs = 100, Type = JoystickReplayStepType.Disconnect,
                StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        int posted = player.AdvanceTo(10_000);

        posted.Should().Be(5);
        player.IsFinished.Should().BeTrue();
        player.NextStepIndex.Should().Be(5);
        player.TotalDurationMs.Should().Be(275);

        Pump();
        _connectionEvents.Should().HaveCount(2);
        _connectionEvents[0].IsConnected.Should().BeTrue();
        _connectionEvents[1].IsConnected.Should().BeFalse();
        _axisEvents.Should().ContainSingle();
        _axisEvents[0].Axis.Should().Be(JoystickAxis.X);
        _axisEvents[0].Value.Should().Be(1f);
        _buttonEvents.Should().ContainSingle();
        _buttonEvents[0].ButtonIndex.Should().Be(0);
        _buttonEvents[0].IsPressed.Should().BeTrue();
        _hatEvents.Should().ContainSingle();
        _hatEvents[0].Direction.Should().Be(JoystickHatDirection.Up);
    }

    [Fact]
    public void AdvanceTo_IsIdempotentAfterFinish() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 0, Type = JoystickReplayStepType.Connect,
                StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.AdvanceTo(100).Should().Be(1);
        player.AdvanceTo(200).Should().Be(0);
        player.AdvanceTo(300).Should().Be(0);
        Pump();
        _connectionEvents.Should().ContainSingle();
    }

    [Fact]
    public void NegativeDelay_TreatedAsZero() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = -1000, Type = JoystickReplayStepType.Connect,
                StickIndex = 0,
            },
            new JoystickReplayStep {
                DelayMs = 50, Type = JoystickReplayStepType.Disconnect,
                StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.AdvanceTo(0).Should().Be(1);
        player.AdvanceTo(50).Should().Be(1);
        player.IsFinished.Should().BeTrue();
        player.TotalDurationMs.Should().Be(50);
    }

    [Fact]
    public void OutOfRangeButtonIndex_StepIsSkipped() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 0, Type = JoystickReplayStepType.Button,
                StickIndex = 0, ButtonIndex = 7, Pressed = true,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        int posted = player.AdvanceTo(100);

        posted.Should().Be(0);
        player.IsFinished.Should().BeTrue();
        Pump();
        _buttonEvents.Should().BeEmpty();
    }

    [Fact]
    public void StepDeadlines_AreCumulative() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 100, Type = JoystickReplayStepType.Connect,
                StickIndex = 0,
            },
            new JoystickReplayStep {
                DelayMs = 200, Type = JoystickReplayStepType.Disconnect,
                StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.AdvanceTo(150).Should().Be(1);
        player.AdvanceTo(299).Should().Be(0);
        player.AdvanceTo(300).Should().Be(1);
        player.IsFinished.Should().BeTrue();
        player.TotalDurationMs.Should().Be(300);
    }

    [Fact]
    public void Name_AndStepCount_ExposedForDiagnostics() {
        JoystickReplayScript script = new() { Name = "Boss Fight" };
        script.Steps.Add(new JoystickReplayStep { Type = JoystickReplayStepType.Connect });
        script.Steps.Add(new JoystickReplayStep { Type = JoystickReplayStepType.Disconnect });

        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.Name.Should().Be("Boss Fight");
        player.StepCount.Should().Be(2);
    }

    [Fact]
    public void EventsArePostedThroughHubQueue_NotImmediately() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 0, Type = JoystickReplayStepType.Connect, StickIndex = 0,
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.AdvanceTo(0);

        // Before pumping, no listener has been notified.
        _connectionEvents.Should().BeEmpty();

        Pump();

        _connectionEvents.Should().ContainSingle();
    }

    [Fact]
    public void ConnectStep_ForwardsDeviceGuidToConnectionEvent() {
        JoystickReplayScript script = ScriptOf(
            new JoystickReplayStep {
                DelayMs = 0,
                Type = JoystickReplayStepType.Connect,
                StickIndex = 1,
                DeviceName = "Xbox 360 Controller",
                DeviceGuid = "030000005e040000130b000017050000",
            });
        JoystickReplayPlayer player = new(_hub, script, _loggerService);

        player.AdvanceTo(0);
        Pump();

        _connectionEvents.Should().ContainSingle();
        _connectionEvents[0].StickIndex.Should().Be(1);
        _connectionEvents[0].DeviceName.Should().Be("Xbox 360 Controller");
        _connectionEvents[0].DeviceGuid.Should().Be("030000005e040000130b000017050000");
        _connectionEvents[0].IsConnected.Should().BeTrue();
    }
}
