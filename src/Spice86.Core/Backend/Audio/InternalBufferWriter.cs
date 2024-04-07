namespace Spice86.Core.Backend.Audio;

using System.Runtime.CompilerServices;

internal sealed class InternalBufferWriter {
    private readonly AudioPlayer _player;
    private float[]? _conversionBuffer;
    public InternalBufferWriter(AudioPlayer player) => _player = player;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int WriteData<TInput>(Span<TInput> input) where TInput : unmanaged {
        int minBufferSize = input.Length;
        if (_conversionBuffer == null || _conversionBuffer.Length < minBufferSize) {
            Array.Resize(ref _conversionBuffer, minBufferSize);
        }
        SampleConverter.InternalConvert<TInput, float>(input, _conversionBuffer);
        return _player.WriteDataInternal(_conversionBuffer);
    }
}