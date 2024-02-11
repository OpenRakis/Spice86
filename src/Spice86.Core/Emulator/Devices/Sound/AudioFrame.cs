namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Represents a piece of audio data
/// </summary>
public struct AudioFrame {
    /// <summary>
    /// Initializes a new Audio Frame
    /// </summary>
    /// <param name="left">The data for the left channel</param>
    /// <param name="right">The data for the right channel</param>
    public AudioFrame(float left, float right) {
        Left = left;
        Right = right;
    }

    /// <summary>
    /// Gets or sets the data of the left channel.
    /// </summary>
    public float Left { get; set; }

    /// <summary>
    /// Gets or sets the data of the right channel.
    /// </summary>
    public float Right { get; set; }

    /// <summary>
    /// Indexed access to either the left or right channel.
    /// </summary>
    /// <param name="i">The channel index.</param>
    public float this[int i] {
        get { return int.IsEvenInteger(i) ? Left : Right; }
        set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
    }
}