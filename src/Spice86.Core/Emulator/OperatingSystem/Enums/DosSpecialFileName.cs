namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Special DOS file names (including file names for directory traversal or host operating system reserved file names).
/// </summary>
internal enum DosSpecialFileName {
    /// <summary>
    /// Not a device file name.
    /// </summary>
    None = 0,

    /// <summary>
    /// An empty file name. Typically skipped or handled as an error.
    /// </summary>
    Empty,

    /// <summary>
    /// The current directory specifier: <c>.</c>
    /// </summary>
    CurrentDirectory,

    /// <summary>
    /// The parent directory specifier: <c>..</c>
    /// </summary>
    ParentDirectory,

    /// <summary>
    /// An unknown or invalid device or file name. Should always be handled as an error (to prevent bad host-mapped
    /// file names).
    /// </summary>
    Invalid,

    /// <summary>
    /// The null device, <c>NUL</c> (typically a "black hole").
    /// </summary>
    Null,

    /// <summary>
    /// The console device, <c>CON</c> (keyboard input, display output).
    /// </summary>
    Console,

    /// <summary>
    /// The auxiliary device, <c>AUX</c> (typically the first connected COM port).
    /// </summary>
    Auxiliary,

    /// <summary>
    /// The printer device, <c>PRN</c> (typically the first connected LPT port).
    /// </summary>
    Printer,

    /// <summary>
    /// COM port #1, <c>COM1</c> or <c>COM¹</c>.
    /// </summary>
    SerialPort1,

    /// <summary>
    /// COM port #2, <c>COM2</c> or <c>COM²</c>.
    /// </summary>
    SerialPort2,

    /// <summary>
    /// COM port #3, <c>COM3</c> or <c>COM³</c>.
    /// </summary>
    SerialPort3,

    /// <summary>
    /// COM port #4, <c>COM4</c>.
    /// </summary>
    SerialPort4,

    /// <summary>
    /// COM port #5, <c>COM5</c>.
    /// </summary>
    SerialPort5,

    /// <summary>
    /// COM port #6, <c>COM6</c>.
    /// </summary>
    SerialPort6,

    /// <summary>
    /// COM port #7, <c>COM7</c>.
    /// </summary>
    SerialPort7,

    /// <summary>
    /// COM port #8, <c>COM8</c>.
    /// </summary>
    SerialPort8,

    /// <summary>
    /// COM port #9, <c>COM9</c>.
    /// </summary>
    SerialPort9,

    /// <summary>
    /// Parallel port #1, <c>LPT1</c> or <c>LPT¹</c>.
    /// </summary>
    ParallelPort1,

    /// <summary>
    /// Parallel port #2, <c>LPT2</c> or <c>LPT²</c>.
    /// </summary>
    ParallelPort2,

    /// <summary>
    /// Parallel port #3, <c>LPT3</c> or <c>LPT³</c>.
    /// </summary>
    ParallelPort3,

    /// <summary>
    /// Parallel port #4, <c>LPT4</c>.
    /// </summary>
    ParallelPort4,

    /// <summary>
    /// Parallel port #5, <c>LPT5</c>.
    /// </summary>
    ParallelPort5,

    /// <summary>
    /// Parallel port #6, <c>LPT6</c>.
    /// </summary>
    ParallelPort6,

    /// <summary>
    /// Parallel port #7, <c>LPT7</c>.
    /// </summary>
    ParallelPort7,

    /// <summary>
    /// Parallel port #8, <c>LPT8</c>.
    /// </summary>
    ParallelPort8,

    /// <summary>
    /// Parallel port #9, <c>LPT9</c>.
    /// </summary>
    ParallelPort9,
}
