namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Collections.Generic;

/// <summary>
/// Translates DOS filepaths to host file paths, and vice-versa.
/// </summary>
public interface IDosPathResolver {
    /// <summary>
    /// Gets the map between DOS drive letters and <see cref="MountedFolder"/> structures
    /// </summary>
    IDictionary<char, MountedFolder> DriveMap { get; }

    /// <summary>
    /// The current DOS drive in use.
    /// </summary>
    char CurrentDrive { get; }

    /// <summary>
    /// The full host path to the folder used by DOS as the current folder.
    /// </summary>
    string CurrentHostDirectory { get; }

    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir);

    /// <summary>
    /// Create a relative path from the current host directory to another. Paths will be resolved before calculating the difference.
    /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
    /// </summary>
    /// <param name="hostPath">The destination path.</param>
    /// <returns>A string containing the relative host path, or <paramref name="hostPath"/> if the paths don't share the same root.</returns>
    string GetRelativeHostPathToCurrentDirectory(string hostPath);

    /// <summary>
    /// Converts the DOS path to a full host path.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    string? TryGetFullHostPathFromDos(string dosPath);

    /// <summary>
    /// Converts the DOS path to a full host path of the parent directory.<br/>
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    string? TryGetFullHostParentPathFromDos(string dosPath);

    /// <summary>
    /// Prefixes the given DOS path by either the mapped drive folder or the current host folder depending on whether there is a root in the path.<br/>
    /// Does not convert to a case sensitive path. <br/>
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the combination of the host path and the DOS path.</returns>
    string PrefixWithHostDirectory(string dosPath);

    /// <summary>
    /// Returns whether the folder or file name already exists, in DOS's case insensitive point of view.
    /// </summary>
    /// <param name="newFileOrFolderName">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrFolderName, DirectoryInfo hostFolder);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="newCurrentDir">The new host folder to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    DosFileOperationResult SetCurrentDir(string newCurrentDir);
}