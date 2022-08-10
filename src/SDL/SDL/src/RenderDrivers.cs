using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class RenderDrivers : IReadOnlyList<RendererInfo>
    {
        internal RenderDrivers() { }

        public int Count => SDL_GetNumRenderDrivers();

        public RendererInfo this[int index]
        {
            get
            {
                SDL_RendererInfo info;
                SDL_GetRenderDriverInfo(index, out info);
                return new RendererInfo(info);
            }
        }

        IEnumerable<RendererInfo> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<RendererInfo> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
