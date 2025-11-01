namespace Spice86.ViewModels.Services;

using Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Factory for creating MemoryBitmapViewModel instances with proper dependency injection.
/// </summary>
public class MemoryBitmapViewModelFactory : IMemoryBitmapViewModelFactory {
    private readonly IVideoState _videoState;
    private readonly IHostStorageProvider _storage;

    public MemoryBitmapViewModelFactory(IVideoState videoState, IHostStorageProvider storage) {
        _videoState = videoState;
        _storage = storage;
    }

    /// <inheritdoc />
    public MemoryBitmapViewModel CreateNew() {
        return new MemoryBitmapViewModel(_videoState, _storage);
    }
}
