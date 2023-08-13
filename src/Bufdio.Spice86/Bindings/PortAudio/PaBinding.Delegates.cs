namespace Bufdio.Spice86.Bindings.PortAudio;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// A static class that contains P/Invoke wrappers for the PortAudio library.
/// </summary>
/// <remarks>
/// This class is a wrapper around the PortAudio library. PortAudio is a free, cross-platform, open-source, audio I/O library.
/// </remarks>
internal static partial class PaBinding {
    /// <summary>
    /// A callback function that is used by a stream to provide or consume audio data in real time.
    /// </summary>
    /// <param name="input">A buffer containing the input samples.</param>
    /// <param name="output">A buffer where the output samples should be placed.</param>
    /// <param name="frameCount">The number of frames to be processed by the callback.</param>
    /// <param name="timeInfo">A structure containing timestamps representing the capture time of the first sample in the input buffer, and the time of the deadline for the first sample in the output buffer.</param>
    /// <param name="statusFlags">Flags indicating whether input and/or output underflow and/or overflow conditions occurred.</param>
    /// <param name="userData">A pointer to user-defined data.</param>
    /// <returns>A value indicating whether the stream should continue calling the callback function.</returns>
    public unsafe delegate PaStreamCallbackResult PaStreamCallback(
        void* input,
        void* output,
        long frameCount,
        IntPtr timeInfo,
        PaStreamCallbackFlags statusFlags,
        void* userData
    );
    
}
