namespace Spice86.Core.Emulator.Memory;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class MemoryDeviceUtils {
    public static bool TryGetSpan(byte[] memory, out uint startAddress, out Span<byte> span) {
        startAddress = 0;
        span = memory;
        return true;
    }

    public static bool TryGetSpan(byte[] memory, out uint startAddress, out ReadOnlySpan<byte> span) {
        startAddress = 0;
        span = memory;
        return true;
    }

    public static bool TryGetSpan(byte[] memory, uint startAddress, out Span<byte> span) {
        long lengthRemaining = memory.Length - startAddress;
        if (lengthRemaining >= 0) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length and guaranteed
            // that adding the start address to it will not go out of bounds of array).
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(memory), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }

    public static bool TryGetSpan(byte[] memory, uint startAddress, out ReadOnlySpan<byte> span) {
        long lengthRemaining = memory.Length - startAddress;
        if (lengthRemaining >= 0) {
            // Cast from long to int is safe because length remaining is in the range 0..array.Length and guaranteed
            // that adding the start address to it will not go out of bounds of array).
            span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(memory), startAddress), (int)lengthRemaining);
            return true;
        }

        span = [];
        return false;
    }

    public static bool TryGetSpan(byte[] memory, uint startAddress, int length, out Span<byte> span) {
        long lengthRemaining = memory.Length - startAddress;
        if (lengthRemaining >= (uint)length) {
            // CreateSpan is safe because of above length check (length will always be in the range 0..memory.Length
            // and guaranteed that adding the start address to it will not go out of bounds of array).
            Debug.Assert(length >= 0);
            span = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(memory), startAddress), length);
            return true;
        }

        span = [];
        return false;
    }

    public static bool TryGetSpan(byte[] memory, uint startAddress, int length, out ReadOnlySpan<byte> span) {
        long lengthRemaining = memory.Length - startAddress;
        if (lengthRemaining >= (uint)length) {
            // CreateSpan is safe because of above length check (length will always be in the range 0..memory.Length
            // and guaranteed that adding the start address to it will not go out of bounds of array).
            Debug.Assert(length >= 0);
            span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(memory), startAddress), length);
            return true;
        }

        span = [];
        return false;
    }
}
