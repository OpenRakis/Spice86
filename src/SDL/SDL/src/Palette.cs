using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public unsafe class Palette : SafeHandle, IReadOnlyList<Color>
    {

        private SDL_Palette* ptr
        {
            get
            {
                if (IsInvalid) throw new ObjectDisposedException(nameof(Palette));
                return (SDL_Palette*)handle;
            }
        }

        private Palette() : base(IntPtr.Zero, true)
        {
        }

        internal Palette(IntPtr ptr, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(ptr);
        }

        public Palette(int count) : this()
        {
            Palette? palette = ErrorIfInvalid(SDL_AllocPalette(count));
            SetHandle(palette.handle);
            palette.SetHandle(IntPtr.Zero);
        }

        public unsafe int Count
        {
            get
            {
                return ptr->ncolors;
            }
        }

        public Color this[int index]
        {
            get
            {
                if (index < 0 || index > ptr->ncolors)
                    throw new IndexOutOfRangeException();
                return ptr->colors[index];
            }
            set
            {
                if (index < 0 || index > ptr->ncolors)
                    throw new IndexOutOfRangeException();
                ptr->colors[index] = value;
            }
        }

        public void CopyFrom(ReadOnlySpan<Color> colors, int start, int length)
        {
            if (start + length > colors.Length)
                throw new IndexOutOfRangeException();

            fixed (Color* ptr = &MemoryMarshal.GetReference(colors))
            {
                SDL_SetPaletteColors(this, ptr, start, length);
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_FreePalette(this.handle);
            return true;
        }

        IEnumerable<Color> Enumerate()
        {
            int idx = 0;
            while (true)
            {
                if (idx++ < Count)
                    yield return this[idx];
                else
                    break;
            }
        }

        public IEnumerator<Color> GetEnumerator()
        {
            return Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
