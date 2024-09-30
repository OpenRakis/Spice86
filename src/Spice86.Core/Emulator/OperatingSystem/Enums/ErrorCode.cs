namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Defines error codes for MS-DOS operations.
/// </summary>
public enum ErrorCode : byte {
    /// <summary>
    /// No error occurred during the operation.
    /// </summary>
    NoError,

    /// <summary>
    /// The function number specified in the operation is invalid.
    /// </summary>
    FunctionNumberInvalid,

    /// <summary>
    /// The specified file could not be found on the disk.
    /// </summary>
    FileNotFound,

    /// <summary>
    /// The specified path could not be found on the disk.
    /// </summary>
    PathNotFound,

    /// <summary>
    /// The maximum number of files that can be opened at once has been exceeded.
    /// </summary>
    TooManyOpenFiles,

    /// <summary>
    /// Access to the specified file was denied due to permissions.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The specified file handle is invalid or does not exist.
    /// </summary>
    InvalidHandle,

    /// <summary>
    /// The memory control block for the specified file has been destroyed.
    /// </summary>
    MemoryControlBlockDestroyed,

    /// <summary>
    /// There is not enough memory to complete the requested operation.
    /// </summary>
    InsufficientMemory,

    /// <summary>
    /// The memory block address specified in the operation is invalid.
    /// </summary>
    MemoryBlockAddressInvalid,

    /// <summary>
    /// The environment for the current process is invalid.
    /// </summary>
    EnvironmentInvalid,

    /// <summary>
    /// The format of the specified drive is invalid or unsupported.
    /// </summary>
    FormatInvalid,

    /// <summary>
    /// The access code specified in the operation is invalid.
    /// </summary>
    AccessCodeInvalid,

    /// <summary>
    /// The data specified in the operation is invalid.
    /// </summary>
    DataInvalid,

    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved,

    /// <summary>
    /// The specified drive is invalid or does not exist.
    /// </summary>
    InvalidDrive,

    /// <summary>
    /// Attempted to remove the current directory, which is not allowed.
    /// </summary>
    AttemptedToRemoveCurrentDirectory,

    /// <summary>
    /// The source and destination directories are not on the same device.
    /// </summary>
    NotSameDevice,

    /// <summary>
    /// There are no more files in the directory to read.
    /// </summary>
    NoMoreFiles,

    /// <summary>
    /// No more files match the criteria
    /// </summary>
    NoMoreMatchingFiles,

    /// <summary>
    /// File, or a part of the file, is locked
    /// </summary>
    LockViolation = 0x33,

    /// <summary>
    /// File already exists in the directory
    /// </summary>
    FileAlreadyExists = 0x80,
}
