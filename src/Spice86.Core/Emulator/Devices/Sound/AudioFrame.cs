namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// A frame of audio data containing two channels.
/// </summary>
public ref struct AudioFrame
{
    private Span<float> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioFrame"/> struct.
    /// </summary>
    /// <param name="data">The source of audio values</param>
    public AudioFrame(Span<float> data)
    {
        _data = data;
    }

    /// <summary>
    /// Gives the underlying Span
    /// </summary>
    /// <returns>The underlying Span of floats</returns>
    public Span<float> AsSpan() => _data;

    /// <summary>
    /// The left channel value.
    /// </summary>
    public float Left
    {
        get => _data[0];
        set => _data[0] = value;
    }

    /// <summary>
    /// The right channel value.
    /// </summary>
    public float Right {
        get => _data.Length > 0 ? _data[1] : _data[0];
        set {
            int index = _data.Length > 0 ? 1 : 0;
            _data[index] = value;
        }
    }

    /// <summary>
    /// Provides access to the left and right channel values using an index.
    /// </summary>
    public float this[int i] {
        get { return int.IsEvenInteger(i) ? Left : Right; }
        set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
    }
}
