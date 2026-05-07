namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Replay;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Input.Joystick.Replay;
using Spice86.Shared.Emulator.Input.Joystick.Replay;
using Spice86.Shared.Interfaces;

using System.IO;
using System.Text.Json;

using Xunit;

public sealed class JoystickReplayJsonStoreTests : System.IDisposable {
    private readonly string _tempFile;
    private readonly ILoggerService _loggerService;
    private readonly JoystickReplayJsonStore _store;

    public JoystickReplayJsonStoreTests() {
        _tempFile = Path.Combine(Path.GetTempPath(),
            "spice86-replay-tests-" + Path.GetRandomFileName() + ".json");
        _loggerService = Substitute.For<ILoggerService>();
        _loggerService.IsEnabled(Arg.Any<Serilog.Events.LogEventLevel>()).Returns(true);
        _store = new JoystickReplayJsonStore(_loggerService);
    }

    public void Dispose() {
        if (File.Exists(_tempFile)) {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull() {
        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields() {
        JoystickReplayScript script = new() {
            Name = "Demo",
            Steps = {
                new JoystickReplayStep {
                    DelayMs = 0,
                    Type = JoystickReplayStepType.Connect,
                    StickIndex = 0,
                    DeviceName = "Replay",
                },
                new JoystickReplayStep {
                    DelayMs = 100,
                    Type = JoystickReplayStepType.Axis,
                    StickIndex = 0,
                    Axis = Spice86.Shared.Emulator.Input.Joystick.JoystickAxis.X,
                    Value = 0.75f,
                },
                new JoystickReplayStep {
                    DelayMs = 50,
                    Type = JoystickReplayStepType.Button,
                    StickIndex = 0,
                    ButtonIndex = 1,
                    Pressed = true,
                },
                new JoystickReplayStep {
                    DelayMs = 30,
                    Type = JoystickReplayStepType.Hat,
                    StickIndex = 0,
                    Direction =
                        Spice86.Shared.Emulator.Input.Joystick.JoystickHatDirection.Up,
                },
                new JoystickReplayStep {
                    DelayMs = 200,
                    Type = JoystickReplayStepType.Disconnect,
                    StickIndex = 0,
                },
            },
        };

        _store.Save(_tempFile, script);
        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Demo");
        loaded.SchemaVersion.Should().Be(1);
        loaded.Steps.Should().HaveCount(5);
        loaded.Steps[0].Type.Should().Be(JoystickReplayStepType.Connect);
        loaded.Steps[0].DeviceName.Should().Be("Replay");
        loaded.Steps[1].Value.Should().BeApproximately(0.75f, 1e-6f);
        loaded.Steps[2].ButtonIndex.Should().Be(1);
        loaded.Steps[2].Pressed.Should().BeTrue();
        loaded.Steps[3].Direction.Should().Be(
            Spice86.Shared.Emulator.Input.Joystick.JoystickHatDirection.Up);
        loaded.Steps[4].Type.Should().Be(JoystickReplayStepType.Disconnect);
    }

    [Fact]
    public void Save_WritesEnumsAsStringsForHandEditing() {
        JoystickReplayScript script = new() {
            Steps = {
                new JoystickReplayStep {
                    Type = JoystickReplayStepType.Axis,
                    Axis = Spice86.Shared.Emulator.Input.Joystick.JoystickAxis.Y,
                },
            },
        };

        _store.Save(_tempFile, script);

        string json = File.ReadAllText(_tempFile);
        json.Should().Contain("\"Axis\"");
        json.Should().Contain("\"Y\"");
    }

    [Fact]
    public void Load_FutureSchemaVersion_ReturnsNull() {
        File.WriteAllText(_tempFile,
            "{ \"schemaVersion\": 999, \"steps\": [] }");

        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_MalformedJson_ReturnsNull() {
        File.WriteAllText(_tempFile, "{ this is not json");

        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_AcceptsTrailingCommasAndComments() {
        File.WriteAllText(_tempFile,
            "// header comment\n{ \"name\": \"x\", \"steps\": [], }");

        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("x");
    }

    [Fact]
    public void Save_EmitsIndentedJson() {
        JoystickReplayScript script = new() { Name = "Indent" };

        _store.Save(_tempFile, script);

        string json = File.ReadAllText(_tempFile);
        json.Should().Contain("\n");
    }

    [Fact]
    public void Load_DocumentEqualsLiteralNull_ReturnsNull() {
        File.WriteAllText(_tempFile, "null");

        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_UnknownPropertiesAreIgnoredForForwardsCompat() {
        File.WriteAllText(_tempFile,
            "{ \"schemaVersion\": 1, \"name\": \"x\", \"futureField\": 42, \"steps\": [] }");

        JoystickReplayScript? loaded = _store.Load(_tempFile);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("x");
    }

    [Fact]
    public void Save_ToNestedDirectory_CreatesIt() {
        string nested = Path.Combine(Path.GetTempPath(),
            "spice86-replay-nested-" + Path.GetRandomFileName(), "x.json");
        try {
            _store.Save(nested, new JoystickReplayScript { Name = "n" });
            File.Exists(nested).Should().BeTrue();
        } finally {
            if (File.Exists(nested)) {
                File.Delete(nested);
            }
            string? dir = Path.GetDirectoryName(nested);
            if (dir is not null && Directory.Exists(dir)) {
                Directory.Delete(dir);
            }
        }
    }

    [Fact]
    public void SchemaVersionConstantMatchesScriptDefault() {
        // The supported schema version must match the value the
        // POCO emits by default so freshly-saved scripts round-trip.
        new JoystickReplayScript().SchemaVersion
            .Should().Be(JoystickReplayJsonStore.SupportedSchemaVersion);
    }
}
