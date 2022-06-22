namespace TinyAudio;

using Silk.NET.OpenAL;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public sealed unsafe class OpenAlAudioPlayer : AudioPlayer {
    private static TimeSpan _bufferLength;
    
    readonly AL al = null;
    readonly ALContext alContext = null;
    readonly Device* device = null;
    readonly Context* context = null;
    readonly uint source = 0;
    bool disposed = false;
    float volume = 1.0f;
    bool enabled = true;

    private OpenAlAudioPlayer(AudioFormat format) : base(format) {
        
    }

    protected override unsafe void Start(bool useCallback) {
    }

    protected override void Stop() {
    }

    protected override unsafe int WriteDataInternal(ReadOnlySpan<byte> data) {
    }
    
    public static OpenAlAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        _bufferLength = bufferLength;
        return new(new AudioFormat(Channels: 2, SampleFormat: SampleFormat.SignedPcm16, SampleRate: 22050));
    }
}