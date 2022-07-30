namespace Spice86.Shared.Interfaces;
public interface IVideoBufferViewModel {
    int Width { get; }
    int Height { get; }
    uint Address { get; }
}
