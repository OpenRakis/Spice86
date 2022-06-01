namespace TinyAudio;

using System;
using System.Runtime.Versioning;
using System.Threading;
using SDL_Sharp;

public sealed class SdlAudioPlayer : AudioPlayer {


    public SdlAudioPlayer(AudioFormat format) : base(format) {
    }

    protected override void Start(bool useCallback) {
        throw new NotImplementedException();
    }

    protected override void Stop() {
        throw new NotImplementedException();
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        throw new NotImplementedException();
    }
}