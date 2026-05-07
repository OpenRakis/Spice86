namespace Spice86.Tests.ViewModels;

using Avalonia.Platform.Storage;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using System;
using System.IO;

using Xunit;

public sealed class JoystickMapperViewModelTests : IDisposable {
    private readonly string _tempDir;
    private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
    private readonly IHostStorageProvider _storage = Substitute.For<IHostStorageProvider>();
    private readonly JoystickMappingJsonStore _store;

    public JoystickMapperViewModelTests() {
        _tempDir = Path.Combine(Path.GetTempPath(),
            $"spice86-joystick-mapper-vm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new JoystickMappingJsonStore(_logger);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Initial_state_has_empty_mapping_and_no_selected_profile() {
        JoystickMapperViewModel vm = new(_store, _storage, _logger);

        vm.Profiles.Should().BeEmpty();
        vm.SelectedProfile.Should().BeNull();
        vm.SchemaVersion.Should().Be(1);
        vm.CurrentFilePath.Should().BeEmpty();
    }

    [Fact]
    public void AddProfile_then_RemoveProfile_round_trip() {
        JoystickMapperViewModel vm = new(_store, _storage, _logger);

        vm.AddProfile();
        vm.AddProfile();

        vm.Profiles.Should().HaveCount(2);
        vm.SelectedProfile.Should().NotBeNull();

        vm.RemoveProfile();

        vm.Profiles.Should().HaveCount(1);
    }

    [Fact]
    public void ToMapping_round_trips_an_edited_profile_through_disk() {
        JoystickMapperViewModel vm = new(_store, _storage, _logger);
        vm.DefaultProfileName = "Pad";
        vm.AddProfile();
        JoystickProfileEditorViewModel profile = vm.SelectedProfile!;
        profile.Name = "Pad";
        profile.DeviceGuid = "0123456789abcdef0123456789abcdef";
        profile.DeviceName = "TestPad";
        profile.DeadzonePercent = 25;
        profile.UseCircularDeadzone = true;
        profile.SwapStickBAxes = true;
        profile.AddAxis();
        profile.SelectedAxis!.RawAxisIndex = 1;
        profile.SelectedAxis.Target = VirtualAxis.StickAY;
        profile.SelectedAxis.Invert = true;
        profile.SelectedAxis.Scale = 1.5;
        profile.SelectedAxis.DeadzonePercent = 30;
        profile.AddButton();
        profile.SelectedButton!.RawButtonIndex = 2;
        profile.SelectedButton.Target = VirtualButton.StickBButton1;
        profile.SelectedButton.AutoFire = true;
        profile.HatRawIndex = 0;
        profile.HatTargetStickIndex = 1;
        profile.HatEnabled = false;
        profile.RumbleEnabled = false;
        profile.RumbleAmplitudeScale = 0.25;
        profile.MidiOnGameportEnabled = true;
        profile.Mpu401BasePort = 0x300;

        string path = Path.Combine(_tempDir, "mapping.json");
        _store.Save(path, vm.ToMapping());
        JoystickMapping? loaded = _store.Load(path);

        loaded.Should().NotBeNull();
        loaded!.DefaultProfileName.Should().Be("Pad");
        loaded.Profiles.Should().ContainSingle();
        JoystickProfile p = loaded.Profiles[0];
        p.Name.Should().Be("Pad");
        p.DeviceGuid.Should().Be("0123456789abcdef0123456789abcdef");
        p.DeviceName.Should().Be("TestPad");
        p.DeadzonePercent.Should().Be(25);
        p.UseCircularDeadzone.Should().BeTrue();
        p.SwapStickBAxes.Should().BeTrue();
        p.Axes.Should().ContainSingle();
        p.Axes[0].RawAxisIndex.Should().Be(1);
        p.Axes[0].Target.Should().Be(VirtualAxis.StickAY);
        p.Axes[0].Invert.Should().BeTrue();
        p.Axes[0].Scale.Should().Be(1.5);
        p.Axes[0].DeadzonePercent.Should().Be(30);
        p.Buttons.Should().ContainSingle();
        p.Buttons[0].RawButtonIndex.Should().Be(2);
        p.Buttons[0].Target.Should().Be(VirtualButton.StickBButton1);
        p.Buttons[0].AutoFire.Should().BeTrue();
        p.Hat.TargetStickIndex.Should().Be(1);
        p.Hat.Enabled.Should().BeFalse();
        p.Rumble.Enabled.Should().BeFalse();
        p.Rumble.AmplitudeScale.Should().Be(0.25);
        p.MidiOnGameport.Enabled.Should().BeTrue();
        p.MidiOnGameport.Mpu401BasePort.Should().Be(0x300);
    }

    [Fact]
    public async Task LoadAsync_replaces_current_mapping_when_picker_returns_a_file() {
        string path = Path.Combine(_tempDir, "mapping.json");
        JoystickMapping mapping = new() {
            DefaultProfileName = "FromDisk",
            Profiles = {
                new JoystickProfile { Name = "FromDisk" }
            },
        };
        _store.Save(path, mapping);

        IStorageFile file = Substitute.For<IStorageFile>();
        file.Path.Returns(new Uri(path));
        _storage.CanOpen.Returns(true);
        _storage.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>())
            .Returns(new[] { file });

        JoystickMapperViewModel vm = new(_store, _storage, _logger);

        await vm.LoadAsync();

        vm.CurrentFilePath.Should().Be(path);
        vm.DefaultProfileName.Should().Be("FromDisk");
        vm.Profiles.Should().ContainSingle();
        vm.Profiles[0].Name.Should().Be("FromDisk");
    }

    [Fact]
    public async Task SaveAsync_with_known_path_writes_to_disk() {
        JoystickMapperViewModel vm = new(_store, _storage, _logger);
        vm.AddProfile();
        vm.SelectedProfile!.Name = "Saved";
        vm.DefaultProfileName = "Saved";
        string path = Path.Combine(_tempDir, "out.json");
        vm.CurrentFilePath = path;

        await vm.SaveAsync();

        File.Exists(path).Should().BeTrue();
        JoystickMapping? loaded = _store.Load(path);
        loaded.Should().NotBeNull();
        loaded!.DefaultProfileName.Should().Be("Saved");
        loaded.Profiles.Should().ContainSingle();
    }
}
