namespace Spice86.Core.Backend.Audio;

using System;
using System.Runtime.CompilerServices;

public abstract partial class AudioPlayer {
    private sealed class CallbackRaiser<TInput, TOutput> : CallbackRaiser
        where TInput : unmanaged
        where TOutput : unmanaged {
        private readonly BufferNeededCallback<TInput> callback;
        private TInput[]? conversionBuffer;

        public CallbackRaiser(BufferNeededCallback<TInput> callback) {
            this.callback = callback;
        }

        public override void RaiseCallback(Span<byte> buffer, out int samplesWritten) {
            // if formats are the same no sample conversion is needed
            if (typeof(TInput) == typeof(TOutput)) {
                callback(buffer.Cast<byte, TInput>(), out samplesWritten);
            } else {
                int minBufferSize = buffer.Length / Unsafe.SizeOf<TOutput>();
                if (conversionBuffer == null || conversionBuffer.Length < minBufferSize) {
                    Array.Resize(ref conversionBuffer, minBufferSize);
                }

                callback(conversionBuffer.AsSpan(0, minBufferSize), out samplesWritten);
                SampleConverter.InternalConvert(conversionBuffer.AsSpan(0, minBufferSize), buffer.Cast<byte, TOutput>());
            }
        }
    }
}
