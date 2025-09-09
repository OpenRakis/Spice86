namespace Spice86.Core.Emulator.OperatingSystem.Interfaces;

using Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Interface for DOS path resolution operations. 
/// This represents the existing contract between DosPathResolver and its callers.
/// </summary>
public interface IDosPathResolver {
    /// <summary>
    /// Gets the current DOS directory.
    /// </summary>
    /// <param name="driveNumber">The drive number (0=default, 1=A:, 2=B:, etc.)</param>
    /// <param name="currentDir">The current DOS directory path</param>
    /// <returns>Result of the operation</returns>
    DosFileOperationResult GetCurrentDosDirectory(byte driveNumber, out string currentDir);

    /// <summary>
    /// Sets the current DOS folder.
    /// </summary>
    /// <param name="dosPath">The new DOS path to use as the current DOS folder.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> that details the result of the operation.</returns>
    DosFileOperationResult SetCurrentDir(string dosPath);

    /// <summary>
    /// Converts the DOS path to a full host path of the parent directory.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full path to the parent directory in the host file system, or <c>null</c> if nothing was found.</returns>
    string? GetFullHostParentPathFromDosOrDefault(string dosPath);

    /// <summary>
    /// Converts the DOS path to a full host path.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the full file path in the host file system, or <c>null</c> if nothing was found.</returns>
    string? GetFullHostPathFromDosOrDefault(string dosPath);

    /// <summary>
    /// Prefixes the given DOS path by either the mapped drive folder or the current host folder depending on whether there is a root in the path.
    /// Does not convert to a case sensitive path. 
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="dosPath">The DOS path to convert.</param>
    /// <returns>A string containing the combination of the host path and the DOS path.</returns>
    string PrefixWithHostDirectory(string dosPath);

    /// <summary>
    /// Returns whether the folder or file name already exists, in DOS's case insensitive point of view.
    /// </summary>
    /// <param name="newFileOrDirectoryPath">The name of new file or folder we try to create.</param>
    /// <param name="hostFolder">The full path to the host folder to look into.</param>
    /// <returns>A boolean value indicating if there is any folder or file with the same name.</returns>
    bool AnyDosDirectoryOrFileWithTheSameName(string newFileOrDirectoryPath, DirectoryInfo hostFolder);

    /// <summary>
    /// Finds files using wildcard comparison.
    /// </summary>
    /// <param name="searchFolder">The folder to search in</param>
    /// <param name="searchPattern">The search pattern with wildcards</param>
    /// <param name="enumerationOptions">Enumeration options</param>
    /// <returns>Enumerable of matching file paths</returns>
    IEnumerable<string> FindFilesUsingWildCmp(string searchFolder, string searchPattern, EnumerationOptions enumerationOptions);
}