namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Utils;

/// <summary>
/// DOS Swappable Data Area List <br/>
/// This is the part of the DOS private segment containing all the variables used internally by the DOS kernel to track the state of a INT21H call in progress.
/// <remarks>
/// See INT21H functions 0x5D06 and and 0x5D08, also 0x34 ("Get InDOS flag") <br/>
/// See: http://fd.lod.bz/rbil/interrup/dos_kernel/215d06.html
/// </remarks>
/// </summary>
public class DosSwappableArea : MemoryBasedDataStructureWithBaseAddress {
    /// <inheritdoc/>
    public DosSwappableArea(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }

    /// <summary>
    /// The start segment of the DOS SDA in memory.
    /// </summary>
    public const ushort StartSegment = 0xB2;

    /// <summary>
    /// The offset in bytes where the <see cref="InDos"/> flag is located within the <see cref="DosSwappableArea"/>
    /// </summary>
    public const byte InDosOffset = 0x1;
    
    /// <summary>
    /// Count of active critical DOS interrupt calls.
    /// <remarks>
    /// A small subset of DOS functions calls do NOT increment this value. 
    /// </remarks>
    /// </summary>
    public byte InDos {
        get => GetUint8((int)MemoryUtils.ToPhysicalAddress(StartSegment, InDosOffset));
        set => SetUint8((int) MemoryUtils.ToPhysicalAddress(StartSegment, InDosOffset), value);
    }
}