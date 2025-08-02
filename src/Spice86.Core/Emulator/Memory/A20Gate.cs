namespace Spice86.Core.Emulator.Memory;

using System.Diagnostics.Contracts;

/// <summary>
/// Implements the optional silencing of the 20th address line. <br/>
/// When disabled, it rollovers memory addresses beyond 1 MB for programs dependant on this legacy behavior of the 8086 CPU.
/// </summary>
public class A20Gate {
    /// <summary>
    /// Represents the segment address marking the start of the high memory area.
    /// </summary>
    public const ushort SegmentStartOfHighMemoryArea = 0xFFFF;

    /// <summary>
    /// This is the start of the HMA. <br/>
    /// This value is equal to 1 MB.
    /// </summary>
    public const uint StartOfHighMemoryArea = 0x100000;

    /// <summary>
    /// This is the end of the HMA. <br/>
    /// Real Mode cannot access memory beyond this. <br/>
    /// This value equals to 1 MB + 65 519 bytes.
    /// </summary>
    /// <remarks>
    /// For the DOS XMS device, Microsoft XMS TEST.C considers it an error to allow real mode
    /// pointers + length to extend past the end of the
    /// 8086-accessible conventional memory area.
    /// </remarks>
    public const uint EndOfHighMemoryArea = 0x10FFEF;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="enabled">Whether the 20th address line is enabled on emulator startup.</param>
    public A20Gate(bool enabled = true) {
        IsEnabled = enabled;
    }

    /// <summary>
    /// Calculates the new memory address with the 20th address line silenced. <br/>
    /// If it isn't silenced, the same address is returned.
    /// </summary>
    /// <param name="address">The memory address that is to be accessed.</param>
    /// <returns>The transformed address if the 20th address line is silenced. The same address if it isn't.</returns>
    [Pure]
    public int TransformAddress(int address) => (int) (address & AddressMask);

    /// <summary>
    /// Calculates the new memory address with the 20th address line silenced. <br/>
    /// If it isn't silenced, the same address is returned.
    /// </summary>
    /// <param name="address">The memory address that is to be accessed.</param>
    /// <returns>The transformed address if the 20th address line is silenced. The same address if it isn't.</returns>
    [Pure]
    public uint TransformAddress(uint address) => (address & AddressMask);

    /// <summary>
    /// The value for the <see cref="AddressMask"/> when <see cref="IsEnabled"/> is <c>false</c>
    /// </summary>
    public const uint DisabledAddressMask = 0xFFFFF;

    /// <summary>
    /// The value for the <see cref="AddressMask"/> when <see cref="IsEnabled"/> is <c>true</c>
    /// </summary>
    public const uint EnabledAddressMask = 0x1FFFFF;

    /// <summary>
    /// The address mask used over memory accesses.
    /// </summary>
    public uint AddressMask { get; private set; } = DisabledAddressMask;

    /// <summary>
    /// Gets and sets whether the 20th address line is enabled.
    /// When <c>false</c>, the address 'rollover' beyond the first megabyte of main memory is active.
    /// </summary>
    public bool IsEnabled {
        get => AddressMask == EnabledAddressMask;
        set => AddressMask = value ? EnabledAddressMask : DisabledAddressMask;
    }
}