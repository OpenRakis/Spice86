namespace Spice86.Models;

public class AddressChangedMessage(ulong address) {
    public ulong Address { get; } = address;
}