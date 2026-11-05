namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.LoadableFile;

using System.Linq;

/// <summary>
/// The configured program entry segment (the value the override supplier receives as
/// <c>programStartSegment</c>) and every DOS-loaded image the loaders wrote when the snapshot was
/// taken (<see cref="LoadedImageKind.DosProgram"/>, <see cref="LoadedImageKind.InternalBatchProgram"/>
/// and <see cref="LoadedImageKind.DosOverlay"/>; a BIOS binary or boot sector alone keeps observed
/// values).
/// </summary>
internal sealed record ProgramImages(ushort ConfiguredEntrySegment, IReadOnlyList<LoadedImage> Images) {
    /// <summary>Keeps only the images DOS loads.</summary>
    public static ProgramImages Create(ushort configuredEntrySegment, IReadOnlyList<LoadedImage> images) {
        return new ProgramImages(configuredEntrySegment, [.. images.Where(IsDosLoaded)]);
    }

    private static bool IsDosLoaded(LoadedImage image) {
        return image.Kind is LoadedImageKind.DosProgram
            or LoadedImageKind.InternalBatchProgram
            or LoadedImageKind.DosOverlay;
    }
}
