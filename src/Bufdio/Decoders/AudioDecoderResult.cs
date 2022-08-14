namespace Bufdio.Decoders;

/// <summary>
/// Represents result structure returned by audio decoder while reading audio frame.
/// </summary>
public readonly struct AudioDecoderResult
{
    /// <summary>
    /// Initializes <see cref="AudioDecoderResult"/> structure.
    /// </summary>
    /// <param name="frame">Decoded audio frame if successfully reads.</param>
    /// <param name="succeeded">Whether or not the frame is successfully reads.</param>
    /// <param name="eof">Whether or not the decoder reaches end-of-file.</param>
    /// <param name="errorMessage">An error message while reading audio frame.</param>
    public AudioDecoderResult(AudioFrame frame, bool succeeded, bool eof, string errorMessage = default)
    {
        Frame = frame;
        IsSucceeded = succeeded;
        IsEOF = eof;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets decoded audio frame if successfully reads.
    /// This should returns <c>null</c> if <see cref="IsSucceeded"/> is <c>false</c>.
    /// </summary>
    public AudioFrame Frame { get; }

    /// <summary>
    /// Gets whether or not the decoder is succesfully reading audio frame.
    /// </summary>
    public bool IsSucceeded { get; }

    /// <summary>
    /// Gets whether or not the decoder reaches end-of-file (cannot be continued) while reading audio frame. 
    /// </summary>
    public bool IsEOF { get; }

    /// <summary>
    /// Gets error message from the decoder while reading audio frame.
    /// </summary>
    public string ErrorMessage { get; }
}
