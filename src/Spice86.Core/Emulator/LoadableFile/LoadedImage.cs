namespace Spice86.Core.Emulator.LoadableFile;

/// <summary>
/// One program image a loader wrote into emulated memory: <see cref="StartAddress"/> and
/// <see cref="SizeInBytes"/> are the bytes the loader wrote (the image, without the PSP),
/// <see cref="FirstSegment"/> is the first segment counted as inside the image (the PSP segment
/// for a DOS program, the load segment otherwise), <see cref="Name"/> is the path the loader read
/// and <see cref="Kind"/> says which loader produced it.
/// </summary>
public sealed record LoadedImage(LoadedImageKind Kind, string Name, ushort FirstSegment, uint StartAddress, uint SizeInBytes);
