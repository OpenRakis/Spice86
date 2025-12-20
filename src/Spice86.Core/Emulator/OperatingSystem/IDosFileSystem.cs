namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Contract for file operations on a specific DOS drive.
/// Each drive provides its own filesystem handler.<br/>
/// It's easier this way to handle different filesystem types (e.g., host folder, internal virtual drive for our auto-generated AUTOEXEC.BAT, disk image, etc.).
/// </summary>
public interface IDosFileSystem {
    /// <summary>
    /// Opens a file on this drive.
    /// </summary>
    /// <param name="fileName">Filename relative to drive root.</param>
    /// <param name="accessMode">Access mode.</param>
    /// <returns>Operation result with file handle or error.</returns>
    DosFileOperationResult OpenFileOrDevice(string fileName, FileAccessMode accessMode);

    /// <summary>
    /// Creates a file on this drive.
    /// </summary>
    /// <param name="fileName">Filename relative to drive root.</param>
    /// <param name="fileAttribute">File attributes.</param>
    /// <returns>Operation result with file handle or error.</returns>
    DosFileOperationResult CreateFileUsingHandle(string fileName, ushort fileAttribute);

    /// <summary>
    /// Finds the first matching file on this drive.
    /// </summary>
    /// <param name="fileSpec">File spec with wildcards.</param>
    /// <param name="searchAttributes">Attributes filter.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult FindFirstMatchingFile(string fileSpec, ushort searchAttributes);

    /// <summary>
    /// Finds the next matching file on this drive.
    /// </summary>
    /// <returns>Operation result.</returns>
    DosFileOperationResult FindNextMatchingFile();

    /// <summary>
    /// Gets the current directory on this drive.
    /// </summary>
    /// <param name="driveNumber">The DOS drive number for which we want the current DOS folder path.</param>
    /// <param name="currentDir">Returns current directory.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult GetCurrentDir(byte driveNumber, out string currentDir);

    /// <summary>
    /// Sets the current directory on this drive.
    /// </summary>
    /// <param name="newPath">New directory path.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult SetCurrentDir(string newPath);

    /// <summary>
    /// Creates a directory on this drive.
    /// </summary>
    /// <param name="dosDirectory">Directory name.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult CreateDirectory(string dosDirectory);

    /// <summary>
    /// Removes a file on this drive.
    /// </summary>
    /// <param name="dosFile">File name.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult RemoveFile(string dosFile);

    /// <summary>
    /// Removes a directory on this drive.
    /// </summary>
    /// <param name="dosDirectory">Directory name.</param>
    /// <returns>Operation result.</returns>
    DosFileOperationResult RemoveDirectory(string dosDirectory);

    /// <summary>
    /// Closes a file on this drive.
    /// </summary>
    /// <param name="fileHandle">The file to close.</param>
    DosFileOperationResult CloseFileOrDevice(ushort fileHandle);

    /// <summary>
    /// Gets the DOS path for a file on this drive.
    /// </summary>
    /// <param name="fileName">File name.</param>
    /// <returns>DOS path.</returns>
    string GetDosFilePath(string fileName);

    /// <summary>
    /// Sets the segmented address to the DTA.
    /// </summary>
    /// <param name="diskTransferAreaAddressSegment">The segment part of the segmented address to the DTA.</param>
    /// <param name="diskTransferAreaAddressOffset">The offset part of the segmented address to the DTA.</param>
    void SetDiskTransferAreaAddress(ushort diskTransferAreaAddressSegment, ushort diskTransferAreaAddressOffset);
}
