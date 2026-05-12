namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Creates an <see cref="ICdRomImage"/> from an image file path.</summary>
public static class CdRomImageFactory {
    /// <summary>
    /// Opens the disc image at <paramref name="imagePath"/>, choosing the implementation based on file extension.
    /// </summary>
    /// <param name="imagePath">Path to a <c>.iso</c> or <c>.cue</c> file.</param>
    /// <returns>An <see cref="ICdRomImage"/> ready for reading.</returns>
    /// <exception cref="ArgumentException">Thrown when the file extension is not recognised.</exception>
    public static ICdRomImage Open(string imagePath) {
        string extension = Path.GetExtension(imagePath);
        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)) {
            return new IsoImage(imagePath);
        }
        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)) {
            return new CueBinImage(imagePath);
        }
        throw new ArgumentException($"Unsupported disc image format '{extension}'. Supported formats: .iso, .cue", nameof(imagePath));
    }
}
