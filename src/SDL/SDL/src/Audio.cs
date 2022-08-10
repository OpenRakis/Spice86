using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Audio
    {
        public static AudioDrivers Drivers { get; } = new AudioDrivers();
        public static AudioDevices InputDevices { get; } = new AudioDevices(1);
        public static AudioDevices OutputDevices { get; } = new AudioDevices(0);

        public static unsafe void Init(string? driverName = null)
        {
            bool disableDrop = SDL.ShouldDisableDropAfterInit(InitFlags.Audio);
            if (driverName != null)
            {
                Span<byte> buf = stackalloc byte[SL(driverName)];
                StringToUTF8(driverName, buf);
                fixed (byte* ptr = &MemoryMarshal.GetReference(buf))
                    ErrorIfNegative(SDL_AudioInit(ptr));
            }
            else
            {
                ErrorIfNegative(SDL_VideoInit(null));
            }
            if (disableDrop) SDL.DisableDropEvents();
        }

        public static void Quit()
        {
            SDL_AudioQuit();
        }

        public static AudioInputDevice OpenInput(
          string device,
          AllowedAudioStreamChange allowedChanges,
          AudioStreamFormat requestedFormat,
          out AudioStreamFormat obtainedFormat
        )
        {
            return new AudioInputDevice(InternalOpen(device, 1, allowedChanges, requestedFormat, out obtainedFormat), true);
        }

        public static AudioOutputDevice OpenOutput(
          string device,
          AllowedAudioStreamChange allowedChanges,
          AudioStreamFormat requestedFormat,
          out AudioStreamFormat obtainedFormat
        )
        {
            return new AudioOutputDevice(InternalOpen(device, 0, allowedChanges, requestedFormat, out obtainedFormat), true);
        }

        private static unsafe uint InternalOpen(
          string device,
          int iscapture,
          AllowedAudioStreamChange allowedChanges,
          AudioStreamFormat requestedFormat,
          out AudioStreamFormat obtainedFormat
        )
        {
            obtainedFormat = new AudioStreamFormat(default);

            Span<byte> utf8 = stackalloc byte[SL(device)];
            StringToUTF8(device, utf8);
            fixed (byte* p = &MemoryMarshal.GetReference(utf8))
                return ErrorIfZero(SDL_OpenAudioDevice(p, iscapture, requestedFormat.spec, out obtainedFormat.spec, allowedChanges));
        }

        public static unsafe void Mix(
          ReadOnlySpan<byte> source,
          Span<byte> destination,
          AudioDataFormat format,
          int volume
        )
        {
            uint len = (uint)Math.Min(source.Length, destination.Length);

            fixed (byte* src = &MemoryMarshal.GetReference(source))
            fixed (byte* dst = &MemoryMarshal.GetReference(destination))
                SDL_MixAudioFormat(dst, src, format, len, volume);
        }

        public static unsafe AudioStream LoadWAV(RWOps ops)
        {
            var fmt = new AudioStreamFormat(default);
            byte* buf;
            uint len;
            ErrorIfNull((IntPtr)SDL_LoadWAV_RW(ops, 0, out fmt.spec, out buf, out len));
            return new AudioStream(fmt, (IntPtr)buf, len);
        }

        public static unsafe AudioStream LoadWAV(string file)
        {
            var fmt = new AudioStreamFormat(default);
            byte* buf;
            uint len;
            ErrorIfNull((IntPtr)SDL_LoadWAV_RW(RWOps.FromFile(file, "rb"), 1, out fmt.spec, out buf, out len));
            return new AudioStream(fmt, (IntPtr)buf, len);
        }
    }
}
