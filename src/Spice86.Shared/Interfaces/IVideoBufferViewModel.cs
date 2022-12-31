namespace Spice86.Shared.Interfaces;
/// <summary>
/// Graphical User Interface API of a videobuffer exposed to the Emulator. <br/>
/// This is an instance of a VideoBufferViewModel.
/// </summary>
public interface IVideoBufferViewModel {
    /// <summary>
    /// The width of the videobuffer, in pixels.
    /// </summary>
    /// <value></value>
    int Width { get; }
    /// <summary>
    /// The height of the videobuffer, in pixels.
    /// </summary>
    /// <value></value>
    int Height { get; }
    /// <summary>
    /// The start address in memory of the videobuffer.
    /// </summary>
    /// <value></value>
    uint Address { get; }

    /// <summary>
    /// The number of frames rendered since its creation
    /// </summary>
    long FramesRendered { get; }
}
