namespace Spice86.Core.Backend.Audio;

using Spice86.Shared.Emulator.Audio;

using System.Runtime.CompilerServices;

internal sealed class InternalBufferWriter {
    private readonly AudioPlayer _player;
    private  readonly AudioFrame<float> _conversionBuffer = new(0,0);
    public InternalBufferWriter(AudioPlayer player) => _player = player;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int WriteData<TInput>(AudioFrame<TInput> input) where TInput : unmanaged {
        SampleConverter.InternalConvert<TInput, float>(input, _conversionBuffer);
        return _player.WriteDataInternal(_conversionBuffer);
    }
}