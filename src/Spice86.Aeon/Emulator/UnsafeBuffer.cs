using System;
using System.Runtime.CompilerServices;

namespace Spice86.Aeon.Emulator
{
    internal readonly struct UnsafeBuffer<T> where T : unmanaged
    {
        private readonly T[] array;

        public UnsafeBuffer(int length) => this.array = GC.AllocateArray<T>(length, pinned: true);

        public unsafe T* ToPointer() => (T*)Unsafe.AsPointer(ref this.array[0]);
        public void Clear() => Array.Clear(this.array, 0, this.array.Length);
    }
}
