namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

/// <summary>
/// DOSBox-compatible DC bias removal for OPL2 output.
/// </summary>
internal class DcBiasRemover {
    public enum Channel { Left, Right }

    // Per-channel state for DC bias removal
    private readonly ChannelState[] _channels = new ChannelState[2];

    // DOSBox configuration
    private const int PcmPlaybackRateHz = 16000;
    private const int LowestFreqToMaintainHz = 200;
    private const int NumToAverage = PcmPlaybackRateHz / LowestFreqToMaintainHz;
    private const short BiasThreshold = 5;

    public DcBiasRemover() {
        for (int i = 0; i < _channels.Length; i++) {
            _channels[i] = new ChannelState();
        }
    }

    /// <summary>
    /// Removes DC bias from a sample (matches DOSBox template function).
    /// </summary>
    public short RemoveDcBias(short sample, Channel channel) {
        var state = _channels[(int)channel];

        // Clear the queue if the stream isn't biased
        if (sample < BiasThreshold) {
            state.Sum = 0;
            state.Samples.Clear();
            return sample;
        }

        // Keep a running sum and push the sample to the back of the queue
        state.Sum += sample;
        state.Samples.Enqueue(sample);

        short average = 0;
        short frontSample = 0;

        if (state.Samples.Count == NumToAverage) {
            // Compute the average and deduct it from the front sample
            average = (short)(state.Sum / NumToAverage);
            frontSample = state.Samples.Dequeue();
            state.Sum -= frontSample;
        }

        return (short)(frontSample - average);
    }

    private class ChannelState {
        public int Sum;
        public readonly Queue<short> Samples = new();
    }
}