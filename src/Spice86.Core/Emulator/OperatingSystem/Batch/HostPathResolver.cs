namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.IO;

/// <summary>
/// Resolves raw path tokens from MOUNT / IMGMOUNT command lines to absolute host file-system paths.
/// </summary>
/// <remarks>
/// Resolution priority (mirrors DOSBox Staging behaviour):
/// <list type="number">
///   <item>Path starts with a letter that matches a mounted DOS drive (e.g. <c>C:\games\disc.iso</c>)
///         → strip the DOS drive prefix and combine with the drive's host directory.</item>
///   <item>Path is already an absolute host path (starts with <c>/</c> on Unix, or is rooted on
///         Windows but did not match a DOS drive) → resolve via <see cref="Path.GetFullPath(string)"/>.</item>
///   <item>Relative path → resolve against the current DOS working directory's host equivalent
///         (current drive's <c>MountedHostDirectory</c> + <c>CurrentDosDirectory</c>).</item>
///   <item>Fallback → resolve against the process working directory.</item>
/// </list>
/// </remarks>
internal static class HostPathResolver {
    /// <summary>
    /// Resolves <paramref name="path"/> to an absolute host file-system path.
    /// </summary>
    /// <param name="path">The raw path token from the command line.</param>
    /// <param name="driveManager">The DOS drive manager used to translate DOS drive letters to host directories.</param>
    /// <returns>An absolute host path.</returns>
    /// <exception cref="ArgumentException">Thrown when the resolved path contains invalid characters.</exception>
    /// <exception cref="PathTooLongException">Thrown when the resolved path exceeds the system limit.</exception>
    /// <exception cref="NotSupportedException">Thrown when the path format is not supported.</exception>
    internal static string Resolve(string path, DosDriveManager driveManager) {
        if (string.IsNullOrEmpty(path)) {
            return path;
        }

        // Explicit DOS drive letter (e.g. C:\games\disc.iso or C:disc.iso)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') {
            char driveLetter = char.ToUpperInvariant(path[0]);
            if (driveManager.TryGetValue(driveLetter, out VirtualDrive? drive)
                && !string.IsNullOrEmpty(drive.MountedHostDirectory)) {
                // Strip "X:" and an optional leading backslash/forward-slash
                string dosRelative = path.Length >= 3 && (path[2] == '\\' || path[2] == '/')
                    ? path.Substring(3)
                    : (path.Length > 2 ? path.Substring(2) : string.Empty);
                dosRelative = dosRelative.Replace('\\', Path.DirectorySeparatorChar)
                                         .Replace('/', Path.DirectorySeparatorChar);
                string hostBase = drive.MountedHostDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, '/', '\\');
                string combined = string.IsNullOrEmpty(dosRelative)
                    ? hostBase
                    : Path.Combine(hostBase, dosRelative);
                return Path.GetFullPath(combined);
            }
        }

        // Absolute host path (no matching DOS drive found above, or no drive prefix)
        if (Path.IsPathRooted(path)) {
            return Path.GetFullPath(path);
        }

        // Relative path — resolve against the current DOS directory's host equivalent
        VirtualDrive currentDrive = driveManager.CurrentDrive;
        if (!string.IsNullOrEmpty(currentDrive.MountedHostDirectory)) {
            string hostBase = currentDrive.MountedHostDirectory.TrimEnd(
                Path.DirectorySeparatorChar, '/', '\\');
            string dosCurrentDir = currentDrive.CurrentDosDirectory ?? string.Empty;
            if (!string.IsNullOrEmpty(dosCurrentDir)) {
                string relDir = dosCurrentDir.TrimStart('/', '\\')
                                             .Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);
                hostBase = Path.Combine(hostBase, relDir);
            }
            string relPath = path.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(hostBase, relPath));
        }

        // Fallback: process working directory
        return Path.GetFullPath(path);
    }
}
