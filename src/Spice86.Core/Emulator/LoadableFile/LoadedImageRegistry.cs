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

    /// <summary>Adds <paramref name="image"/> to the registry, extending the last
    /// <see cref="LoadedImageKind.EmulatorRoutines"/> entry instead when it has the same name and
    /// first segment and the new range is contiguous with it. Keeps one registry entry per
    /// contiguous run a writer produced. Raises <see cref="ImageLoaded"/> with the resulting
    /// image; watchers that only follow loaded programs skip the routine kind.</summary>
    public void RegisterOrExtendContiguous(LoadedImage image) {
        LoadedImage registered = image;
        EventHandler<LoadedImage>? handler;
        lock (_lock) {
            if (image.Kind == LoadedImageKind.EmulatorRoutines
                && _images.Count > 0
                && _images[^1] is { Kind: LoadedImageKind.EmulatorRoutines } last
                && last.Name == image.Name
                && last.FirstSegment == image.FirstSegment
                && last.StartAddress + last.SizeInBytes == image.StartAddress) {
                registered = last with { SizeInBytes = last.SizeInBytes + image.SizeInBytes };
                _images[^1] = registered;
            } else {
                _images.Add(image);
            }
            handler = ImageLoaded;
        }
        handler?.Invoke(this, registered);
    }
}
