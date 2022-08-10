﻿namespace Spice86.Core.Backend.Audio.Sdl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SDLSharp;

internal class SdlAudioPlayer : AudioPlayer {
    private bool _disposed;
    private static int _numberOfSdlPlayerInstances = 0;
    private static bool _sdlInitialized;

    public SdlAudioPlayer(Backend.Audio.AudioFormat format) : base(format) {
        if (!_sdlInitialized) {
            SDL.Init(InitFlags.Audio);
            Mixer.Open(22050, AudioDataFormat.Signed16Bit, 2, 1024);
        }
        _numberOfSdlPlayerInstances++;
    }

    protected override void Start(bool useCallback) {
        //NOP
    }

    protected override void Stop() {
        Mixer.Channels.Halt();
    }

    protected unsafe override int WriteDataInternal(ReadOnlySpan<byte> data) {
        fixed(byte* ptr = data) {
            var chunk = MixerChunk.Load(RWOps.FromHandle((IntPtr)ptr, true));
            Mixer.Channels.Play(chunk);
        }
        return data.Length;
    }

    public static SdlAudioPlayer Create() {
        return new SdlAudioPlayer(new Backend.Audio.AudioFormat(SampleRate: 22050, Channels: 2,
            SampleFormat: SampleFormat.SignedPcm16));
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                if (_numberOfSdlPlayerInstances == 1 && _sdlInitialized) {
                    SDL.QuitSubSystem(InitFlags.Audio);
                    _sdlInitialized = false;
                }
                _numberOfSdlPlayerInstances--;
                _disposed = true;
            }
        }
    }
}
