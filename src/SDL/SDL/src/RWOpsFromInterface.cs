using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class RWOpsFromInterface : RWOps
    {
        private ArraySegment<byte> partial;

        public IRWOps? Implementation { get; }

        readonly SDL_RWopsSize _size;
        readonly SDL_RWopsSeek _seek;
        readonly SDL_RWopsRead _read;
        readonly SDL_RWopsWrite _write;
        readonly SDL_RWopsClose _close;


        protected unsafe RWOpsFromInterface() : base()
        {
            _size = NativeSize;
            _seek = NativeSeek;
            _read = NativeRead;
            _write = NativeWrite;
            _close = NativeClose;
            if (!IsInvalid)
                Register();
        }

        public RWOpsFromInterface(IRWOps stream, int bufSize) : this()
        {
            partial = new ArraySegment<byte>(new byte[bufSize]).Slice(0, 0);
            Implementation = stream;
            RWOpsFromInterface? reg = ErrorIfInvalid(SDL_AllocRW());
            SetHandle(reg.handle);
            reg.SetHandle(IntPtr.Zero);
            Register();
        }

        unsafe void Register()
        {
            var ptr = (SDL_RWops*)handle;

            ptr->type = RWOpsType.Unknown;
            ptr->size = Marshal.GetFunctionPointerForDelegate<SDL_RWopsSize>(_size);
            ptr->seek = Marshal.GetFunctionPointerForDelegate<SDL_RWopsSeek>(_seek);
            ptr->read = Marshal.GetFunctionPointerForDelegate<SDL_RWopsRead>(_read);
            ptr->write = Marshal.GetFunctionPointerForDelegate<SDL_RWopsWrite>(_write);
            ptr->close = Marshal.GetFunctionPointerForDelegate<SDL_RWopsClose>(_close);
        }

        unsafe long NativeSize(SDL_RWops* io)
        {
            if (Implementation == null)
            {
                SetError($"{nameof(RWOpsFromInterface)} is missing an implementation to call");
                return -1;
            }
            try
            {
                return Implementation.Size();
            }
            catch (Exception ex)
            {
                SetError(ex);
                return -1;
            }
        }

        unsafe long NativeSeek(SDL_RWops* io, long offset, int whence)
        {
            if (Implementation == null)
            {
                SetError($"{nameof(RWOpsFromInterface)} is missing an implementation to call");
                return -1;
            }
            try
            {
                long ret;
                if (whence == 1) {
                    ret = Implementation.Seek(offset - partial.Count, whence);
                } else {
                    ret = Implementation.Seek(offset, whence);
                }
                if (ret >= 0)
                    partial = partial.Slice(0, 0);
                return ret;
            }
            catch (Exception ex)
            {
                SetError(ex);
                return -1;
            }
        }

        unsafe UIntPtr NativeRead(SDL_RWops* io, byte* ptr, UIntPtr size, UIntPtr maxnum)
        {
            if (Implementation == null)
            {
                SetError($"{nameof(RWOpsFromInterface)} is missing an implementation to call");
                return UIntPtr.Zero;
            }
            try
            {
                for (ulong i = 0; i < (ulong)maxnum; ++i)
                {
                    if (partial.Count < (int)size)
                    {
                        if (partial.Count > 0 && partial.Array is not null)
                        {
                            byte[]? arr = partial.Array;
                            System.Diagnostics.Debug.Assert(arr.Length > 0);
                            int l = partial.Count;
                            partial.CopyTo(arr);
                            partial = arr;
                            partial = partial.Slice(l);
                        }
                        else if(partial.Array is not null)
                        {
                            partial = partial.Array;
                        }

                        Span<byte> buffer = partial;
                        try
                        {
                            int read = Implementation.Read(buffer);
                            if (read == 0)
                                return (UIntPtr)i - 1;
                            if (read < partial.Count)
                                partial = partial.Slice(0, read);
                        }
                        catch (Exception ex)
                        {
                            SetError(ex);
                            return (UIntPtr)i - 1;
                        }

                        if (partial.Count < (int)size)
                        {
                            return (UIntPtr)i - 1;
                        }
                    }

                    var target = new Span<byte>(ptr + i * (ulong)size, (int)size);
                    Span<byte> src = partial.Slice(0, (int)size);
                    partial = partial.Slice((int)size);
                    src.CopyTo(target);
                }
                return maxnum;
            }
            catch (Exception ex)
            {
                SetError(ex);
                return UIntPtr.Zero;
            }
        }

        unsafe UIntPtr NativeWrite(SDL_RWops* io, byte* ptr, UIntPtr size, UIntPtr num)
        {
            if (Implementation == null)
            {
                SetError($"{nameof(RWOpsFromInterface)} is missing an implementation to call");
                return UIntPtr.Zero;
            }
            try
            {
                for (ulong i = 0; i < (ulong)num; ++i)
                {
                    var span = new Span<byte>(ptr + i * (ulong)size, (int)size);
                    try
                    {
                        int written = Implementation.Write(span);
                        if (written < (int)size)
                            return (UIntPtr)i - 1;
                    }
                    catch (Exception ex)
                    {
                        SetError(ex);
                        return (UIntPtr)i - 1;
                    }
                }
                return num;
            }
            catch (Exception ex)
            {
                SetError(ex);
                return UIntPtr.Zero;
            }
        }

        unsafe int NativeClose(SDL_RWops* io)
        {
            try
            {
                this.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                SetError(ex);
                return -1;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Implementation is IDisposable d)
                    d.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_FreeRW(this.handle);
            return true;
        }
    }
}
