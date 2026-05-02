namespace Spice86.Tests.Debugger;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class EmulatorProvidedCodeRegistryTests {
    [Fact]
    public void IsEmulatorProvided_ReturnsFalseForUnregisteredAddress() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();

        bool result = registry.IsEmulatorProvided(new SegmentedAddress(0x1000, 0x0000));

        result.Should().BeFalse();
    }

    [Fact]
    public void IsEmulatorProvided_ReturnsTrueForAddressInsideRoutine() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        registry.Register(new ProvidedRoutineInfo(
            new SegmentedAddress(0xF000, 0x1000), 16, "provided_interrupt_handler_21", "Interrupt 21h"));

        registry.IsEmulatorProvided(new SegmentedAddress(0xF000, 0x1000)).Should().BeTrue();
        registry.IsEmulatorProvided(new SegmentedAddress(0xF000, 0x1008)).Should().BeTrue();
        registry.IsEmulatorProvided(new SegmentedAddress(0xF000, 0x100F)).Should().BeTrue();
    }

    [Fact]
    public void IsEmulatorProvided_EndIsExclusive() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        registry.Register(new ProvidedRoutineInfo(
            new SegmentedAddress(0xF000, 0x1000), 16, "name", "subsystem"));

        // First byte after the routine should not match.
        registry.IsEmulatorProvided(new SegmentedAddress(0xF000, 0x1010)).Should().BeFalse();
    }

    [Fact]
    public void IsEmulatorProvided_HandlesNormalizedSegmentedAddresses() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        // Routine starts at F000:1000 (= 0xF1000 physical), is 32 bytes long.
        registry.Register(new ProvidedRoutineInfo(
            new SegmentedAddress(0xF000, 0x1000), 32, "name", "subsystem"));

        // F100:0000 is the same physical address (0xF1000); F100:0010 is at 0xF1010.
        registry.IsEmulatorProvided(new SegmentedAddress(0xF100, 0x0000)).Should().BeTrue();
        registry.IsEmulatorProvided(new SegmentedAddress(0xF100, 0x0010)).Should().BeTrue();
        registry.IsEmulatorProvided(new SegmentedAddress(0xF100, 0x0020)).Should().BeFalse();
    }

    [Fact]
    public void TryGet_ReturnsRoutineInfoWhenAddressMatches() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        ProvidedRoutineInfo info = new ProvidedRoutineInfo(
            new SegmentedAddress(0xF000, 0x1000), 16, "provided_mouse_driver", "Mouse driver");
        registry.Register(info);

        bool found = registry.TryGet(new SegmentedAddress(0xF000, 0x1004), out ProvidedRoutineInfo? matched);

        found.Should().BeTrue();
        matched.Should().BeSameAs(info);
    }

    [Fact]
    public void TryGet_ReturnsFalseForMissAddress() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        registry.Register(new ProvidedRoutineInfo(
            new SegmentedAddress(0xF000, 0x1000), 16, "name", "subsystem"));

        bool found = registry.TryGet(new SegmentedAddress(0x1000, 0x0000), out ProvidedRoutineInfo? matched);

        found.Should().BeFalse();
        matched.Should().BeNull();
    }

    [Fact]
    public void Routines_ReturnsAllRegisteredInOrder() {
        EmulatorProvidedCodeRegistry registry = new EmulatorProvidedCodeRegistry();
        ProvidedRoutineInfo a = new ProvidedRoutineInfo(new SegmentedAddress(0xF000, 0x0000), 4, "a", "Sub A");
        ProvidedRoutineInfo b = new ProvidedRoutineInfo(new SegmentedAddress(0xF000, 0x0010), 4, "b", "Sub B");
        registry.Register(a);
        registry.Register(b);

        registry.Routines.Should().ContainInOrder(a, b);
    }
}
