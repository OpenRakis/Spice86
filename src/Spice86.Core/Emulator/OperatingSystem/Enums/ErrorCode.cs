namespace Spice86.Core.Emulator.OperatingSystem.Enums;

public enum ErrorCode : byte
{
    NoError,
    FunctionNumberInvalid,
    FileNotFound,
    PathNotFound,
    TooManyOpenFiles,
    AccessDenied,
    InvalidHandle,
    MemoryControlBlockDestroyed,
    InsufficientMemory,
    MemoryBlockAddressInvalid,
    EnvironmentInvalid,
    FormatInvalid,
    AccessCodeInvalid,
    DataInvalid,
    Reserved,
    InvalidDrive,
    AttemptedToRemoveCurrentDirectory,
    NotSameDevice,
    NoMoreFiles
}