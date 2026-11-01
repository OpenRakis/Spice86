namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Runtime.CompilerServices;

/// <summary>
/// One bit per address in [0, <see cref="CoveredAddresses"/>): a lazy bitmap used by
/// <see cref="BreakPointHolder"/> to skip the dictionary lookup when no breakpoint is registered
/// at an address. Addresses at or above the limit are not tracked; callers treat them as
/// "maybe present" and always take the dictionary path.
/// </summary>
internal sealed class AddressBitmap {
    /// <summary>One past the last address the bitmap covers: 2 MB, the real-mode address space
    /// plus the high memory area, with margin.</summary>
    private const long CoveredAddresses = 1L << 21;
    private const int AddressesPerWord = 64;
    private const int WordIndexShift = 6;
    private const long BitInWordMask = AddressesPerWord - 1;

    private ulong[] _words = [];

    private static long WordIndex(long address) => address >> WordIndexShift;

    private static ulong BitMask(long address) => 1UL << (int)(address & BitInWordMask);

    /// <summary>Whether the bit of <paramref name="address"/> is set. Always
    /// <see langword="true"/> at or above <see cref="CoveredAddresses"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(long address) {
        if ((ulong)address >= CoveredAddresses) {
            return true;
        }

        long word = WordIndex(address);
        return word < _words.Length && (_words[word] & BitMask(address)) != 0;
    }

    /// <summary>Sets the bit of <paramref name="address"/>; a no-op at or above
    /// <see cref="CoveredAddresses"/>. Allocates the bitmap on first use.</summary>
    public void Set(long address) {
        if ((ulong)address >= CoveredAddresses) {
            return;
        }

        if (_words.Length == 0) {
            _words = new ulong[CoveredAddresses / AddressesPerWord];
        }

        _words[WordIndex(address)] |= BitMask(address);
    }

    /// <summary>Clears the bit of <paramref name="address"/>; a no-op at or above
    /// <see cref="CoveredAddresses"/>.</summary>
    public void Clear(long address) {
        if ((ulong)address >= CoveredAddresses) {
            return;
        }

        _words[WordIndex(address)] &= ~BitMask(address);
    }
}
