namespace Spice86.Messages;

using Spice86.Shared.Emulator.Memory;

public record AddressChangedMessage(SegmentedAddress Address);