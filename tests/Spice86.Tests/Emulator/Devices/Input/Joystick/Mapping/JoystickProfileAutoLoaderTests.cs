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

public sealed class JoystickProfileAutoLoaderTests : IDisposable {
    private readonly string _tempDir;
    private readonly ILoggerService _logger;
    private readonly JoystickMappingJsonStore _store;
    private readonly JoystickProfileAutoLoader _loader;

    public JoystickProfileAutoLoaderTests() {
        _tempDir = Path.Combine(Path.GetTempPath(),
            $"spice86-joystick-autoloader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = Substitute.For<ILoggerService>();
        _logger.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        _store = new JoystickMappingJsonStore(_logger);
        _loader = new JoystickProfileAutoLoader(_store, _logger);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void Write(string fileName, JoystickMapping mapping) {
        _store.Save(Path.Combine(_tempDir, fileName), mapping);
    }

    [Fact]
    public void LoadAll_NullDirectory_ReturnsEmpty() {
        LoadedProfiles loaded = _loader.LoadAll(null);

        loaded.Profiles.Should().BeEmpty();
        loaded.DefaultProfileName.Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_MissingDirectory_ReturnsEmpty() {
        LoadedProfiles loaded = _loader.LoadAll(Path.Combine(_tempDir, "does-not-exist"));

        loaded.Profiles.Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmpty() {
        LoadedProfiles loaded = _loader.LoadAll(_tempDir);

        loaded.Profiles.Should().BeEmpty();
        loaded.DefaultProfileName.Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_MergesProfilesFromAllFiles_InDeterministicOrder() {
        Write("b.json", new JoystickMapping {
            Profiles = new List<JoystickProfile> {
                new() { Name = "B1" }, new() { Name = "B2" }
            }
        });
        Write("a.json", new JoystickMapping {
            Profiles = new List<JoystickProfile> {
                new() { Name = "A1" }
            }
        });
        Write("c.json", new JoystickMapping {
            Profiles = new List<JoystickProfile> {
                new() { Name = "C1" }
            }
        });

        LoadedProfiles loaded = _loader.LoadAll(_tempDir);

        loaded.Profiles.Select(p => p.Name).Should().ContainInOrder("A1", "B1", "B2", "C1");
    }

    [Fact]
    public void LoadAll_PicksFirstNonEmptyDefaultProfileName() {
        Write("a.json", new JoystickMapping { DefaultProfileName = string.Empty });
        Write("b.json", new JoystickMapping { DefaultProfileName = "PreferredB" });
        Write("c.json", new JoystickMapping { DefaultProfileName = "AlsoC" });

        LoadedProfiles loaded = _loader.LoadAll(_tempDir);

        loaded.DefaultProfileName.Should().Be("PreferredB");
    }

    [Fact]
    public void LoadAll_SkipsBrokenFiles() {
        Write("good.json", new JoystickMapping {
            Profiles = new List<JoystickProfile> { new() { Name = "Good" } }
        });
        File.WriteAllText(Path.Combine(_tempDir, "bad.json"), "{ broken");

        LoadedProfiles loaded = _loader.LoadAll(_tempDir);

        loaded.Profiles.Should().ContainSingle().Which.Name.Should().Be("Good");
    }

    [Fact]
    public void Resolve_GuidMatch_BeatsNameMatch() {
        JoystickProfile guidProfile = new() {
            Name = "ByGuid",
            DeviceGuid = "030000005e0400008e02000014010000"
        };
        JoystickProfile nameProfile = new() {
            Name = "ByName",
            DeviceName = "Xbox"
        };
        LoadedProfiles loaded = new(
            new List<JoystickProfile> { nameProfile, guidProfile },
            string.Empty);

        JoystickProfile resolved = _loader.Resolve(loaded,
            "030000005E0400008E02000014010000",  // mixed-case GUID still matches
            "Xbox 360 Controller");

        resolved.Name.Should().Be("ByGuid");
    }

    [Fact]
    public void Resolve_NameMatch_IsCaseInsensitiveSubstring() {
        JoystickProfile profile = new() { Name = "P", DeviceName = "xbox" };
        LoadedProfiles loaded = new(new List<JoystickProfile> { profile }, string.Empty);

        JoystickProfile resolved = _loader.Resolve(loaded, string.Empty, "Microsoft XBOX 360 Controller");

        resolved.Name.Should().Be("P");
    }

    [Fact]
    public void Resolve_FallsBackToDefaultProfileName() {
        JoystickProfile fallback = new() { Name = "MyFallback" };
        JoystickProfile other = new() { Name = "Other", DeviceName = "Logitech" };
        LoadedProfiles loaded = new(
            new List<JoystickProfile> { other, fallback },
            "MyFallback");

        JoystickProfile resolved = _loader.Resolve(loaded, "ffff", "Unknown");

        resolved.Name.Should().Be("MyFallback");
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsEmbeddedDefault() {
        LoadedProfiles loaded = new(Array.Empty<JoystickProfile>(), string.Empty);

        JoystickProfile resolved = _loader.Resolve(loaded, "ffff", "Unknown");

        resolved.Name.Should().Be(JoystickProfileAutoLoader.EmbeddedDefaultProfileName);
        resolved.Type.Should().Be(JoystickType.TwoAxis);
        resolved.Axes.Should().HaveCount(4);
        resolved.Buttons.Should().HaveCount(4);
        resolved.Hat.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Resolve_EmptyDeviceGuid_DoesNotMatchEmptyProfileGuid() {
        JoystickProfile p = new() { Name = "X", DeviceGuid = string.Empty, DeviceName = string.Empty };
        LoadedProfiles loaded = new(new List<JoystickProfile> { p }, string.Empty);

        JoystickProfile resolved = _loader.Resolve(loaded, string.Empty, string.Empty);

        // No GUID, no name, no default name -> embedded fallback
        resolved.Name.Should().Be(JoystickProfileAutoLoader.EmbeddedDefaultProfileName);
    }

    [Fact]
    public void EmbeddedDefault_ExposesValidFourAxisFourButtonMapping() {
        JoystickProfile profile = JoystickProfileAutoLoader.BuildEmbeddedDefaultProfile();

        profile.Axes.Select(a => a.Target).Should().BeEquivalentTo(new[] {
            VirtualAxis.StickAX, VirtualAxis.StickAY,
            VirtualAxis.StickBX, VirtualAxis.StickBY
        });
        profile.Buttons.Select(b => b.Target).Should().BeEquivalentTo(new[] {
            VirtualButton.StickAButton1, VirtualButton.StickAButton2,
            VirtualButton.StickBButton1, VirtualButton.StickBButton2
        });
        profile.UseCircularDeadzone.Should().BeTrue();
        profile.DeadzonePercent.Should().Be(10);
    }

    [Fact]
    public void Resolve_ProfileWithEmptyGuid_DoesNotInterceptGuidMatch() {
        JoystickProfile noGuid = new() { Name = "NoGuid", DeviceGuid = string.Empty };
        JoystickProfile withGuid = new() { Name = "WithGuid", DeviceGuid = "abc123" };
        LoadedProfiles loaded = new(
            new List<JoystickProfile> { noGuid, withGuid },
            string.Empty);

        JoystickProfile resolved = _loader.Resolve(loaded, "abc123", string.Empty);

        resolved.Name.Should().Be("WithGuid");
    }
}
