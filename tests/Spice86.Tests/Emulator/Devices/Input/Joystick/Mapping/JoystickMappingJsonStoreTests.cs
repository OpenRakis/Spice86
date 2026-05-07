namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Mapping;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

public sealed class JoystickMappingJsonStoreTests : IDisposable {
    private readonly string _tempDir;
    private readonly ILoggerService _logger;
    private readonly JoystickMappingJsonStore _store;

    public JoystickMappingJsonStoreTests() {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "spice86-joystick-mapping-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logger = Substitute.For<ILoggerService>();
        _logger.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        _store = new JoystickMappingJsonStore(_logger);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void DefaultMapping_RoundTripsCleanly() {
        JoystickMapping original = new();
        string path = Path.Combine(_tempDir, "default.json");

        _store.Save(path, original);
        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().NotBeNull();
        loaded!.SchemaVersion.Should().Be(original.SchemaVersion);
        loaded.DefaultProfileName.Should().Be(original.DefaultProfileName);
        loaded.Profiles.Should().BeEmpty();
    }

    [Fact]
    public void FullProfile_RoundTripsAllFields() {
        JoystickProfile profile = new() {
            Name = "Xbox 360 Controller",
            DeviceGuid = "030000005e0400008e02000014010000",
            DeviceName = "X-Box",
            Type = JoystickType.FourAxis,
            DeadzonePercent = 15,
            UseCircularDeadzone = true,
            SwapStickBAxes = true,
            Axes = new List<AxisMapping> {
                new() { RawAxisIndex = 0, Target = VirtualAxis.StickAX, Invert = false, Scale = 1.0, DeadzonePercent = null },
                new() { RawAxisIndex = 1, Target = VirtualAxis.StickAY, Invert = true,  Scale = 0.85, DeadzonePercent = 8 }
            },
            Buttons = new List<ButtonMapping> {
                new() { RawButtonIndex = 0, Target = VirtualButton.StickAButton1, AutoFire = false },
                new() { RawButtonIndex = 1, Target = VirtualButton.StickAButton2, AutoFire = true }
            },
            Hat = new HatMapping { RawHatIndex = 0, TargetStickIndex = 1, Enabled = true },
            Rumble = new RumbleMapping { Enabled = false, AmplitudeScale = 0.5 },
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x300 }
        };
        JoystickMapping original = new() {
            SchemaVersion = 1,
            DefaultProfileName = "Xbox 360 Controller",
            Profiles = new List<JoystickProfile> { profile }
        };
        string path = Path.Combine(_tempDir, "full.json");

        _store.Save(path, original);
        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().NotBeNull();
        loaded!.DefaultProfileName.Should().Be("Xbox 360 Controller");
        loaded.Profiles.Should().ContainSingle();
        JoystickProfile reloaded = loaded.Profiles[0];
        reloaded.Name.Should().Be(profile.Name);
        reloaded.DeviceGuid.Should().Be(profile.DeviceGuid);
        reloaded.DeviceName.Should().Be(profile.DeviceName);
        reloaded.Type.Should().Be(JoystickType.FourAxis);
        reloaded.DeadzonePercent.Should().Be(15);
        reloaded.UseCircularDeadzone.Should().BeTrue();
        reloaded.SwapStickBAxes.Should().BeTrue();
        reloaded.Axes.Should().HaveCount(2);
        reloaded.Axes[1].Invert.Should().BeTrue();
        reloaded.Axes[1].Scale.Should().BeApproximately(0.85, 1e-9);
        reloaded.Axes[1].DeadzonePercent.Should().Be(8);
        reloaded.Buttons.Should().HaveCount(2);
        reloaded.Buttons[1].Target.Should().Be(VirtualButton.StickAButton2);
        reloaded.Buttons[1].AutoFire.Should().BeTrue();
        reloaded.Hat.TargetStickIndex.Should().Be(1);
        reloaded.Hat.Enabled.Should().BeTrue();
        reloaded.Rumble.Enabled.Should().BeFalse();
        reloaded.Rumble.AmplitudeScale.Should().BeApproximately(0.5, 1e-9);
        reloaded.MidiOnGameport.Enabled.Should().BeTrue();
        reloaded.MidiOnGameport.Mpu401BasePort.Should().Be(0x300);
    }

    [Fact]
    public void MultipleProfiles_PreserveOrder() {
        JoystickMapping original = new() {
            Profiles = new List<JoystickProfile> {
                new() { Name = "First",  Type = JoystickType.TwoAxis },
                new() { Name = "Second", Type = JoystickType.Fcs },
                new() { Name = "Third",  Type = JoystickType.Ch }
            }
        };
        string path = Path.Combine(_tempDir, "ordered.json");

        _store.Save(path, original);
        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().NotBeNull();
        loaded!.Profiles.Select(p => p.Name).Should().ContainInOrder("First", "Second", "Third");
        loaded.Profiles.Select(p => p.Type).Should()
            .ContainInOrder(JoystickType.TwoAxis, JoystickType.Fcs, JoystickType.Ch);
    }

    [Fact]
    public void EnumsAreSerializedAsStrings_NotIntegers() {
        JoystickMapping original = new() {
            Profiles = new List<JoystickProfile> {
                new() {
                    Name = "X",
                    Type = JoystickType.Fcs,
                    Axes = new List<AxisMapping> {
                        new() { Target = VirtualAxis.StickBR }
                    },
                    Buttons = new List<ButtonMapping> {
                        new() { Target = VirtualButton.StickBButton2 }
                    }
                }
            }
        };
        string path = Path.Combine(_tempDir, "enum-strings.json");

        _store.Save(path, original);
        string contents = File.ReadAllText(path);

        contents.Should().Contain("\"Fcs\"");
        contents.Should().Contain("\"StickBR\"");
        contents.Should().Contain("\"StickBButton2\"");
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull() {
        string path = Path.Combine(_tempDir, "nope.json");

        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_MalformedJson_ReturnsNull() {
        string path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{ this is { not json");

        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_FutureSchemaVersion_ReturnsNull() {
        JoystickMapping future = new() { SchemaVersion = JoystickMappingJsonStore.SupportedSchemaVersion + 1 };
        string path = Path.Combine(_tempDir, "future.json");
        _store.Save(path, future);

        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_EmptyDocument_ReturnsNull() {
        string path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "null");

        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().BeNull();
    }

    [Fact]
    public void Load_AcceptsFileWithUnknownProperties_ForwardCompatibility() {
        // Future builds may add fields. Older builds must still parse the document.
        string json = """
        {
          "SchemaVersion": 1,
          "DefaultProfileName": "Test",
          "FutureField": { "x": 1 },
          "Profiles": [
            { "Name": "P1", "Type": "TwoAxis", "FutureFlag": true }
          ]
        }
        """;
        string path = Path.Combine(_tempDir, "future-fields.json");
        File.WriteAllText(path, json);

        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().NotBeNull();
        loaded!.DefaultProfileName.Should().Be("Test");
        loaded.Profiles.Should().ContainSingle()
            .Which.Type.Should().Be(JoystickType.TwoAxis);
    }

    [Fact]
    public void Save_CreatesMissingDirectory() {
        string nested = Path.Combine(_tempDir, "a", "b", "c", "profile.json");

        _store.Save(nested, new JoystickMapping());

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void SupportedSchemaVersion_MatchesPocoDefault() {
        new JoystickMapping().SchemaVersion.Should().Be(JoystickMappingJsonStore.SupportedSchemaVersion);
    }
}
