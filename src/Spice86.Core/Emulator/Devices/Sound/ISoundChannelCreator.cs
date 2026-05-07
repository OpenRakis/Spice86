namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;

/// <summary>
/// Provides the ability to create named sound channels.
/// Implemented by <see cref="SoftwareMixer"/>; consumed by components that need
/// to register audio callbacks without depending on the full mixer class.
/// </summary>
public interface ISoundChannelCreator {
    /// <summary>
    /// Creates and registers a new sound channel.
    /// </summary>
    /// <param name="handler">The audio callback that fills sample buffers.</param>
    /// <param name="sampleRateHz">The native sample rate of the source, or 0 to use the mixer rate.</param>
    /// <param name="name">A unique display name for the channel.</param>
    /// <param name="features">The set of channel features to enable.</param>
    /// <returns>The newly registered <see cref="SoundChannel"/>.</returns>
    SoundChannel AddChannel(Action<int> handler, int sampleRateHz, string name, HashSet<ChannelFeature> features);
}
