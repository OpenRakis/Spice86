namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

using System.Collections.Generic;

/// <summary>
/// Chains a set of <see cref="IAudioCodecFactory"/> instances and dispatches to the
/// first one that reports it can handle the requested file. Throws
/// <see cref="NotSupportedException"/> when no factory accepts the file.
/// </summary>
public sealed class CompositeAudioCodecFactory : IAudioCodecFactory {
    private readonly IReadOnlyList<IAudioCodecFactory> _factories;

    /// <summary>Initializes a new composite over the supplied factories (order matters).</summary>
    /// <param name="factories">Factories to try in order.</param>
    public CompositeAudioCodecFactory(params IAudioCodecFactory[] factories) {
        if (factories == null) {
            throw new ArgumentNullException(nameof(factories));
        }
        _factories = factories;
    }

    /// <inheritdoc/>
    public bool CanHandle(CueFileType fileType, string filePath) {
        foreach (IAudioCodecFactory factory in _factories) {
            if (factory.CanHandle(fileType, filePath)) {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public IAudioCodec Create() {
        throw new InvalidOperationException(
            "CompositeAudioCodecFactory.Create must be invoked via CreateFor(fileType, filePath).");
    }

    /// <summary>Picks the first matching factory and returns a codec from it.</summary>
    /// <param name="fileType">CUE file type to dispatch on.</param>
    /// <param name="filePath">File path to dispatch on.</param>
    /// <returns>A codec for the requested file.</returns>
    /// <exception cref="NotSupportedException">No factory accepts the inputs.</exception>
    public IAudioCodec CreateFor(CueFileType fileType, string filePath) {
        foreach (IAudioCodecFactory factory in _factories) {
            if (factory.CanHandle(fileType, filePath)) {
                return factory.Create();
            }
        }
        throw new NotSupportedException(
            $"No audio codec factory can handle file type '{fileType}' for path '{filePath}'.");
    }
}
