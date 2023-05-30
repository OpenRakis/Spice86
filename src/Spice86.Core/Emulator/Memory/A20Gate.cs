namespace Spice86.Core.Emulator.Memory;

using System.Diagnostics.Contracts;

/// <summary>
/// Implements the optional silencing of the 20th address line. <br/>
/// When disabled, it rollovers memory addresses beyond 1 MB for programs dependant on this legacy behavior of the 8086 CPU.
/// </summary>
public class A20Gate {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="enabled">Whether the 20th address line is enabled on emulator startup.</param>
    public A20Gate(bool enabled) {
        IsA20GateEnabled = enabled;
    }
    
    /// <summary>
    /// Calculates the new memory address with the 20th address line silenced. <br/>
    /// If it isn't silenced, the same address is returned.
    /// </summary>
    /// <param name="address">The memory address that is to be accessed.</param>
    /// <returns>The transformed address if the 20th address line is silenced. The same address if it isn't.</returns>
    [Pure]
    public int TransformAddress(int address) => (int) ((address & A20AddressMask) % Memory.EndOfHighMemoryArea);

    /// <summary>
    /// Calculates the new memory address with the 20th address line silenced. <br/>
    /// If it isn't silenced, the same address is returned.
    /// </summary>
    /// <param name="address">The memory address that is to be accessed.</param>
    /// <returns>The transformed address if the 20th address line is silenced. The same address if it isn't.</returns>
    [Pure]
    public uint TransformAddress(uint address) => (address & A20AddressMask) % Memory.EndOfHighMemoryArea;

    /// <summary>
    /// The value for the <see cref="A20AddressMask"/> when <see cref="IsA20GateEnabled"/> is <c>false</c>
    /// </summary>
    public const uint A20DisabledAddressMask = 0x000FFFFFu;

    /// <summary>
    /// The value for the <see cref="A20AddressMask"/> when <see cref="IsA20GateEnabled"/> is <c>true</c>
    /// </summary>
    public const uint A20EnabledAddressMask = uint.MaxValue;

    /// <summary>
    /// The address mask used over memory accesses.
    /// </summary>
    public uint A20AddressMask { get; private set; } = A20DisabledAddressMask;

    /// <summary>
    /// Gets and sets whether we use the <see cref="A20AddressMask"/> value of 0 or not. <br/>
    /// When <c>false</c>, the address 'rollover' beyond the first megabyte of main memory is active.
    /// </summary>
    public bool IsA20GateEnabled {
        get => A20AddressMask == A20EnabledAddressMask;
        set => A20AddressMask = value ? A20EnabledAddressMask : A20DisabledAddressMask;
    }
}