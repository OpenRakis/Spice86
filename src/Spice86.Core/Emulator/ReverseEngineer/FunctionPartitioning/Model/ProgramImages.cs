namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Core.Emulator.LoadableFile;

using System.Linq;

/// <summary>
/// The configured program entry segment (the value the override supplier receives as
/// <c>programStartSegment</c>) and every image the loaders wrote when the snapshot was taken:
/// <see cref="Images"/> is the DOS-loaded subset (<see cref="LoadedImageKind.DosProgram"/>,
/// <see cref="LoadedImageKind.InternalBatchProgram"/> and <see cref="LoadedImageKind.DosOverlay"/>)
/// that drives image-relative names, while <see cref="AllImages"/> keeps the whole registry in load
/// order - including <see cref="LoadedImageKind.EmulatorRoutines"/> ranges - for byte-origin
/// classification.
/// </summary>
internal sealed record ProgramImages(ushort ConfiguredEntrySegment, IReadOnlyList<LoadedImage> Images,
    IReadOnlyList<LoadedImage> AllImages) {
    /// <summary>Keeps only the DOS-loaded images for <see cref="Images"/>; <see cref="AllImages"/>
    /// keeps the registry snapshot untouched.</summary>
    public static ProgramImages Create(ushort configuredEntrySegment, IReadOnlyList<LoadedImage> images) {
        return new ProgramImages(configuredEntrySegment, [.. images.Where(IsDosLoaded)], images);
    }

    private static bool IsDosLoaded(LoadedImage image) {
        return image.Kind is LoadedImageKind.DosProgram
            or LoadedImageKind.InternalBatchProgram
            or LoadedImageKind.DosOverlay;
    }

    /// <summary>True when the last image covering <paramref name="linearAddress"/> is
    /// emulator-written code: a program image loaded over the same bytes later takes precedence,
    /// so the byte's origin is whatever was registered last.</summary>
    /// <param name="linearAddress">Linear address of an instruction byte.</param>
    public bool IsEmulatorRoutine(uint linearAddress) {
        for (int i = AllImages.Count - 1; i >= 0; i--) {
            LoadedImage image = AllImages[i];
            if (linearAddress - image.StartAddress < image.SizeInBytes) {
                return image.Kind == LoadedImageKind.EmulatorRoutines;
            }
        }
        return false;
    }

    /// <summary>True when every instruction address is emulator-written code: a method is support
    /// only when all of its bytes have an emulator origin, so a method mixing program bytes (the
    /// INT 21h stub holding EXEC child code inline) stays program.</summary>
    /// <param name="instructionAddresses">Linear address of every instruction of the method.</param>
    public bool IsSupportCode(IReadOnlyList<uint> instructionAddresses) {
        return instructionAddresses.Count > 0 && instructionAddresses.All(IsEmulatorRoutine);
    }
}
