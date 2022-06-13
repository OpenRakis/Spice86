namespace TinyAudio;

using System;
using System.Runtime.Versioning;
using System.Threading;
using SDL_Sharp;

using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class SdlAudioPlayer : AudioPlayer {
    private uint _sdlDeviceId = 0;
    private AudioSpec _obtained;
    private static TimeSpan _bufferLength;

    private SdlAudioPlayer(AudioFormat format) : base(format) {
        
    }

    //Mixer_init in DOSBox source code (mixer.cpp)
    protected override unsafe void Start(bool useCallback) {
        AudioSpec desired = new()
        {
            Channels =  (byte) base.Format.Channels,
            Frequency = base.Format.SampleRate,
            Samples = 2048,
            Format = unchecked((short)AudioFormatFlags.S16SYS),
            UserData = null,
            Callback = IntPtr.Zero //SDLCALL MIXER_CallBack in mixer.cpp
        };
        AudioSpec obtained = new();
        int rawSize = Marshal.SizeOf(typeof(AudioSpec));
        var desiredPtr = Marshal.AllocHGlobal(rawSize);
        Marshal.StructureToPtr(desired, desiredPtr, false);
        var obtainedPtr = Marshal.AllocHGlobal(rawSize);
        Marshal.StructureToPtr(obtained, obtainedPtr, false);
        try {
            _sdlDeviceId = SDL_Sharp.SDL.OpenAudioDevice(null, 0, (AudioSpec*)desiredPtr, (AudioSpec*)obtainedPtr, 0);
            _obtained = Marshal.PtrToStructure<AudioSpec>(obtainedPtr);
        } finally {
            Marshal.FreeHGlobal(desiredPtr);
            Marshal.FreeHGlobal(obtainedPtr);
        }
    }

    protected override void Stop() {
        SDL.CloseAudioDevice(_sdlDeviceId);
    }

    //AddSamples in DOSBox source code (mixer.cpp)
    //Mixer_MixData
    //Mixer_Mix
    //Simply use SDL_QueueAudio instead of a callback ?
    // https://wiki.libsdl.org/SDL_QueueAudio
    protected override unsafe int WriteDataInternal(ReadOnlySpan<byte> data) {
        var value = 0;
        fixed (byte* p = data) {
            var intPtr = (IntPtr)p;
            value = SDL.QueueAudio(_sdlDeviceId, intPtr, data.Length);
        }
        var queuedAudio = SDL.GetQueuedAudioSize(_sdlDeviceId);
        System.Diagnostics.Debug.WriteLine(queuedAudio);
        //success
        if (value == 0) {
            if(SDL.PauseAudioDevice(_sdlDeviceId, false) == 0)
            {
                System.Diagnostics.Debug.WriteLine("device unpaused");
            }
            return data.Length;
        }
        return value;
    }
    
    public static SdlAudioPlayer? Create(TimeSpan bufferLength, bool useCallback = false) {
        _bufferLength = bufferLength;
        if (SDL.Init(SdlInitFlags.Audio) < 0) {
            return null;
        }

        return new(new AudioFormat(Channels: 2, SampleFormat: SampleFormat.SignedPcm16, SampleRate: 22050));
    }
}