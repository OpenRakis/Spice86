namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Collections.Generic;

/// <summary>
/// Translates DOS filepaths to host file paths, and vice-versa.
/// </summary>
public interface IDosPathResolver {
    /// <summary>
    /// The map between host paths and the root of DOS drives.
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
    /// Create a relative path from the current host directory to another. Paths will be resolved before calculating the difference.
    /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
    /// </summary>
    /// <param name="hostPath">The destination path.</param>
    /// <returns>The relative path or <paramref name="hostPath"/> if the paths don't share the same root.</returns>
    string GetHostRelativePathToCurrentDirectory(string hostPath);

    /// <summary>
    /// Converts the DOS path to a full host path.<br/>
    /// </summary>
    /// <param name="dosPath">The file name to convert.</param>
    /// <param name="convertParentOnly">if true, it will try to find the case sensitive match for only the parent of the path</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    string? ToHostCaseSensitiveFullName(string dosPath, bool convertParentOnly);

    /// <summary>
    /// Returns the full host file path, including casing.
    /// </summary>
    /// <param name="dosFilePath">The DOS file path.</param>
    /// <returns>A string containing the host file path, or <c>null</c> if not found.</returns>
    string? TryGetFullHostFileName(string dosFilePath);

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a root in the filename or not.<br/>
    /// Does not convert to case sensitive filename. <br/>
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the host directory, combined with the DOS file name.</returns>
    string PrefixWithHostDirectory(string dosPath);

    /// <summary>
    /// Returns the full path to the parent directory.
    /// </summary>
    /// <param name="path">The starting path.</param>
    /// <returns>A string containing the full path to the parent directory, or the original value if not found.</returns>
    string GetFullNameForParentDirectory(string path);

    /// <summary>
    /// Returns whether the folder or file name already exists.
    /// </summary>
    /// <param name="newFileOrFolderName">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    bool IsThereAnyDirectoryOrFileWithTheSameName(string newFileOrFolderName, DirectoryInfo hostFolder);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="newCurrentDir">The new host folder to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    DosFileOperationResult SetCurrentDir(string newCurrentDir);

    /// <summary>
    /// Sets the current DOS folder, and the map between DOS drive letters and host folders paths.
    /// </summary>
    /// <param name="currentDrive">The current DOS drive letter.</param>
    /// <param name="newCurrentDir">The new host folder to use as the current DOS folder.</param>
    /// <param name="driveMap">The map between DOS drive letters and host folders paths.</param>
    void SetDiskParameters(char currentDrive, string newCurrentDir, Dictionary<char, MountedFolder> driveMap);
}