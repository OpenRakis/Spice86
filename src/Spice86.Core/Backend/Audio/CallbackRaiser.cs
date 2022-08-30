namespace Spice86.Core.Backend.Audio;

using System;

public abstract partial class AudioPlayer {
    private abstract class CallbackRaiser {
        public abstract void RaiseCallback(Span<byte> buffer, out int samplesWritten);
    }
}
