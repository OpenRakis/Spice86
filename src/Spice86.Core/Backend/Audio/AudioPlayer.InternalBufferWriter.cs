namespace Spice86.Core.Backend.Audio;

using System;
using System.Runtime.CompilerServices;

public abstract partial class AudioPlayer {
    private abstract class InternalBufferWriter {
        public abstract int WriteData<TInput>(Span<TInput> data) where TInput : unmanaged;
    }
}

public abstract partial class AudioPlayer {
    private sealed class InternalBufferWriter<TOutput> : InternalBufferWriter
        where TOutput : unmanaged {
        private readonly AudioPlayer _player;
        private TOutput[]? _conversionBuffer;

        public InternalBufferWriter(AudioPlayer player) => this._player = player;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public override int WriteData<TInput>(Span<TInput> data) {
            // if formats are the same no sample conversion is needed
            if (typeof(TInput) == typeof(TOutput)) {
                return _player.WriteDataInternal(data.AsBytes()) / Unsafe.SizeOf<TOutput>();
            }

            int minBufferSize = data.Length;
            if (_conversionBuffer == null || _conversionBuffer.Length < minBufferSize) {
                Array.Resize(ref _conversionBuffer, minBufferSize);
            }

            SampleConverter.InternalConvert<TInput, TOutput>(data, _conversionBuffer);
            return _player.WriteDataInternal(_conversionBuffer.AsSpan(0, data.Length).AsBytes()) / Unsafe.SizeOf<TOutput>();
        }
    }
}
