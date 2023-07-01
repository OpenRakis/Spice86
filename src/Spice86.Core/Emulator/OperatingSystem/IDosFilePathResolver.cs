namespace Spice86.Core.Emulator.OperatingSystem; 

/// <summary>
/// Translates DOS filepaths to host file paths and vice-versa.
/// </summary>
public interface IDosFilePathResolver {
    /// <summary>
    /// Converts dosFileName to a host file name.<br/>
    /// For this, this needs to:
    /// <ul>
    /// <li>Prefix either the current folder or the drive folder.</li>
    /// <li>Replace backslashes with slashes</li>
    /// <li>Find case sensitive matches for every path item (since DOS is case insensitive but some OS are not)</li>
    /// </ul>
    /// </summary>
    /// <param name="driveMap">THe map between DOS drive letters and host folder paths.</param>
    /// <param name="hostDirectory">The host directory path to use for path resolution.</param>
    /// <param name="dosFileName">The file name to convert.</param>
    /// <param name="forCreation">if true will try to find case sensitive match for only the parent of the file</param>
    /// <returns>the file name in the host file system, or null if nothing was found.</returns>
    string? ToHostCaseSensitiveFileName(IDictionary<char, string> driveMap, string hostDirectory,  string dosFileName, bool forCreation);

    /// <summary>
    /// Returns the host file path, including casing.
    /// </summary>
    /// <param name="caseInsensitivePath">The DOS file path.</param>
    /// <returns>The host file path.</returns>
    string? GetActualCaseForFileName(string caseInsensitivePath);

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a Drive in the filename or not.<br/>
    /// Does not convert to case sensitive filename. <br/>
    /// Does not search for the file or folder on disk.
    /// </summary>
    /// <param name="hostDirectory">The host directory to use as the current directory.</param>
    /// <param name="dosFileName">The file name to convert.</param>
    /// <param name="driveMap">The map between DOS drive letters and host folder paths.</param>
    /// <returns>The converted file name.</returns>
    string ToHostFilePath(IDictionary<char, string> driveMap, string hostDirectory, string dosFileName);

    /// <summary>
    /// Returns whether the DOS path is absolute.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>Whether the DOS path is absolute.</returns>
    bool IsDosPathRooted(string path);

    /// <summary>
    /// Returns the host path to the parent directory, or <c>null</c> if not found.
    /// </summary>
    /// <param name="path">The starting path.</param>
    /// <returns>The path to the parent directory, or <c>null</c> if not found.</returns>
    public string? GetParentDirectoryFullPath(string path) => Directory.GetParent(path)?.FullName ?? path;

}