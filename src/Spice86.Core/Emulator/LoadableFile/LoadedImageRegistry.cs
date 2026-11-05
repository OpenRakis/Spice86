namespace Spice86.Core.Emulator.LoadableFile;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Records every program image the loaders write, in load order. One instance per machine; the
/// loaders register an image right after its last write, only when the load succeeded.
/// </summary>
public sealed class LoadedImageRegistry {
    private readonly object _lock = new();
    private readonly List<LoadedImage> _images = [];

    /// <summary>The entry point the loader left in CS:IP at the end of a successful program
    /// load. <see langword="null"/> before a program was loaded.</summary>
    public SegmentedAddress? ProgramEntryPoint { get; set; }

    /// <summary>Raised by <see cref="Register"/> after the image has been added.</summary>
    public event EventHandler<LoadedImage>? ImageLoaded;

    /// <summary>A snapshot of the registered images, in load order.</summary>
    public IReadOnlyList<LoadedImage> Images {
        get {
            lock (_lock) {
                return _images.ToArray();
            }
        }
    }

    /// <summary>Adds <paramref name="image"/> to the registry and raises <see cref="ImageLoaded"/>.</summary>
    public void Register(LoadedImage image) {
        EventHandler<LoadedImage>? handler;
        lock (_lock) {
            _images.Add(image);
            handler = ImageLoaded;
        }
        handler?.Invoke(this, image);
    }
}
