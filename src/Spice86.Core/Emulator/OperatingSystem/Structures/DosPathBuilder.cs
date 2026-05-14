namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

using System;
using System.Buffers;
using System.Diagnostics;

/// <summary>A performance driven DOS full path builder.</summary>
/// <remarks>
/// It is absolutely essential that callers call <see cref="Dispose()"/> or <see cref="ToStringWithDispose()"/> when
/// finished with the path builder instance. If not, then memory leaks may occur (especially if using
/// <see cref="DosPathBuilder()"/>, <see cref="DosPathBuilder(int, int)"/>, or
/// <see cref="DosPathBuilder(Span{char}, Span{int})"/> and the internal data buffers need to grow beyond the initial
/// user supplied scratch buffer capacities).
/// </remarks>
internal ref struct DosPathBuilder {
    internal const char VolumeSeparatorChar = DosPathResolver.VolumeSeparatorChar;
    internal const char DirectorySeparatorChar = DosPathResolver.DirectorySeparatorChar;
    internal const char AltDirectorySeparatorChar = DosPathResolver.AltDirectorySeparatorChar;

    /// <summary>The initial number of elements the caller should stack allocate for the path stack buffer.</summary>
    public const int DefaultStackLength = 16;

    /// <summary>The file name characters that are not allowed.</summary>
    /// <remarks>
    /// The list of disallowed characters is partially derived from:
    /// <a href="https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file"/>. Note that this contains
    /// <see cref="VolumeSeparatorChar"/>, <see cref="DirectorySeparatorChar"/>, and
    /// <see cref="AltDirectorySeparatorChar"/>. The list has also been expanded to include all currently defined
    /// Unicode control characters (including 0x7F), to prevent potential problems with host operating system file
    /// systems and user interfaces.
    /// </remarks>
    internal static ReadOnlySpan<char> InvalidFileNameChars => [
        '\x00', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
        '\x08', '\x09', '\x0A', '\x0B', '\x0C', '\x0D', '\x0E', '\x0F',
        '\x10', '\x11', '\x12', '\x13', '\x14', '\x15', '\x16', '\x17',
        '\x18', '\x19', '\x1A', '\x1B', '\x1C', '\x1D', '\x1E', '\x1F',
        '"', '*', '\\', ':', '<', '>', '?', '|', '/',
        // Other control characters. These might be allowed by DOS and Windows internally, but they may result in the
        // inability for the Windows shell or other applications or operating systems to interact with or render files
        // with these characters.
        '\x7F',
        '\x80', '\x81', '\x82', '\x83', '\x84', '\x85', '\x86', '\x87',
        '\x88', '\x89', '\x8A', '\x8B', '\x8C', '\x8D', '\x8E', '\x8F',
        '\x90', '\x91', '\x92', '\x93', '\x94', '\x95', '\x96', '\x97',
        '\x98', '\x99', '\x9A', '\x9B', '\x9C', '\x9D', '\x9E', '\x9F',
    ];

    /// <summary>The file name characters that are not allowed, as a search values object.</summary>
    /// <remarks>See <see cref="InvalidFileNameChars"/>.</remarks>
    internal static readonly SearchValues<char> InvalidFileNameSearchValues = SearchValues.Create(InvalidFileNameChars);

    private ValueListBuilder<char> _pathBuilder;
    private ValueListStack<int> _pathStack;
    private DosSpecialFileNameSettings _specialFileNameSettings;
    private bool _isFrozen;

    /// <summary>Initializes an empty DOS path builder with no preallocated scratch buffers.</summary>
    /// <remarks>
    /// This is useful if caller is using this to call <see cref="ParseSpecialFileName(ReadOnlySpan{char})"/> without
    /// building a DOS path.
    /// </remarks>
    public DosPathBuilder() { }

    /// <summary>Initializes a DOS path builder with user supplied scratch buffers.</summary>
    /// <param name="scratchPathBuffer">The initial buffer to use for path characters.</param>
    /// <param name="scratchStackBuffer">The initial buffer to use for path element stack entries.</param>
    /// <remarks>
    /// The intended use for this constructor is for the caller to supply <see langword="stackalloc"/> allocated
    /// buffers to avoid using heap/pooled memory. If memory allocations are required, then use the other constructor
    /// overload which accepts initial integer capacities (and will thus use pooled memory).
    /// 
    /// Do not modify these scratch buffers while using this path builder! Outside modifications to these buffers may
    /// result in data corruption or other undefined behavior.
    /// </remarks>
    public DosPathBuilder(Span<char> scratchPathBuffer, Span<int> scratchStackBuffer) {
        _pathBuilder = new(scratchPathBuffer);
        _pathStack = new(scratchStackBuffer);
    }

    /// <summary>Initializes a DOS path builder with array pool allocated buffers.</summary>
    /// <param name="charsCapacity">The default minimum number of path characters to allocate.</param>
    /// <param name="stackCapacity">The default minimum number of path element stack entries to allocate.</param>
    public DosPathBuilder(int charsCapacity, int stackCapacity = DefaultStackLength) {
        _pathBuilder = new(charsCapacity);
        _pathStack = new(stackCapacity);
    }

    /// <summary>Gets a value indicating whether the path has a valid drive specification.</summary>
    public readonly bool HasDriveSpecification => _pathBuilder.Length >= 2;

    /// <summary>Gets the current length of the path.</summary>
    public readonly int Length => _pathBuilder.Length;

    /// <summary>Gets the character at the given index within the path.</summary>
    /// <param name="index">The index within the path character buffer. Must be a valid index.</param>
    /// <returns>The character at the given index.</returns>
    public readonly char this[int index] => _pathBuilder[index];

    /// <summary>Gets or sets the settings to use for special file name parsing and handling.</summary>
    public DosSpecialFileNameSettings SpecialFileNameSettings {
        readonly get => _specialFileNameSettings;
        set => _specialFileNameSettings = value;
    }

    /// <summary>Gets a value indicating whether the path builder has been frozen (immutable).</summary>
    /// <remarks>This property will be reset to <see langword="false"/> (mutable) when the builder disposed.</remarks>
    public readonly bool IsFrozen => _isFrozen;

    /// <summary>Releases internal memory associated with the path builder.</summary>
    /// <remarks>
    /// <para><strong>Do not access memory previously retrieved via <see cref="AsSpan()"/> after calling this method!</strong></para>
    /// <para>This will also reset the frozen state and make the path builder mutable again.</para>
    /// </remarks>
    public void Dispose() {
        // These dispose methods will clear the internal value list instances, release the internal memory used, and
        // set their lengths to zero. This allows more data to be appended (though any stack allocated scratch buffer
        // will be lost--so it will only use pooled memory). Because of this, the path builder will become mutable
        // again (so path builder can be reused).
        _pathBuilder.Dispose();
        _pathStack.Dispose();
        _isFrozen = false;
    }

    /// <summary>Gets a read only span representing the current path.</summary>
    /// <returns>A read only span with path characters.</returns>
    /// <remarks>
    /// This may not be valid if the drive letter has not been set or it has not been successfully frozen.
    /// </remarks>
    public readonly ReadOnlySpan<char> AsSpan() => _pathBuilder.AsSpan();

    /// <summary>Creates a string representing the current path.</summary>
    /// <returns>A string representing the current path.</returns>
    /// <remarks>
    /// This may not be valid if the drive letter has not been set or it has not been successfully frozen.
    /// </remarks>
    public override readonly string ToString() => _pathBuilder.AsSpan().ToString();

    /// <summary>
    /// Creates a string representing the current path and releases internal memory associated with the path builder.
    /// </summary>
    /// <returns>A string representing the current path.</returns>
    /// <remarks>
    /// <para><strong>Do not access memory previously retrieved via <see cref="AsSpan()"/> after calling this method!</strong></para>
    /// <para>This may not be valid if the drive letter has not been set or it has not been successfully frozen.</para>
    /// </remarks>
    public string ToStringWithDispose() {
        string result = _pathBuilder.AsSpan().ToString();
        Dispose();
        return result;
    }

    /// <summary>Attempts to freeze the path instance to make the path builder immutable.</summary>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>
    /// A directory separator character will be automatically appended if the current path only contains a drive
    /// specification and no valid path elements have been appended (thus the minimum number of characters in the path
    /// builder after calling this with a successful result will always be three: drive letter, volume separator, and
    /// directory separator). This method can be called multiple times and can also be called after calling
    /// <see cref="AppendFinalDirectorySeparator()"/>. The drive letter must be assigned prior to freezing the path
    /// builder (<see cref="SetDriveIndex(int)"/> or <see cref="SetDriveLetter(char)"/> must be called at least once
    /// for the path to be valid).
    /// </remarks>
    public DosPathBuilderResult Freeze() {
        // Do nothing if the path builder has already been frozen.
        if (_isFrozen) {
            return DosPathBuilderResult.Success;
        }

        // Make sure a drive specification has been set.
        if (!HasDriveSpecification) {
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        DebugValidateState();

        // If the path is only a drive specification, then append a directory separator before freezing.
        if (_pathBuilder.Length <= 2) {
            _pathBuilder.Append(DirectorySeparatorChar);
        }

        _isFrozen = true;
        _pathStack.Dispose(); // path element stack is no longer necessary
        return DosPathBuilderResult.Success;
    }

    /// <summary>Attempts to set the drive index.</summary>
    /// <param name="driveIndex">A valid zero-based DOS drive index that will be converted into a drive letter.</param>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>This can be called multiple times and also after appending other path elements.</remarks>
    public DosPathBuilderResult SetDriveIndex(int driveIndex) {
        // Cannot set if drive index is invalid.
        if (driveIndex is < 0 or >= DosDriveManager.MaxDriveCount) {
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Cannot set if path builder is frozen.
        if (_isFrozen) {
            return DosPathBuilderResult.PathBuilderFrozen;
        }

        // Does the path have a drive specification that needs to be replaced or does a new one need to be appended?
        char driveLetter = DosDriveManager.GetDriveLetterFromIndexFast(driveIndex);
        if (!HasDriveSpecification) {
            Debug.Assert(_pathBuilder.Length == 0);
            _pathBuilder.Append(driveLetter);
            _pathBuilder.Append(':');
        } else {
            DebugValidateState();
            _pathBuilder[0] = driveLetter;
        }

        return DosPathBuilderResult.Success;
    }

    /// <summary>Attempts to set the drive letter.</summary>
    /// <param name="driveLetter">A valid DOS drive letter.</param>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>This can be called multiple times and also after appending other path elements.</remarks>
    public DosPathBuilderResult SetDriveLetter(char driveLetter) =>
        SetDriveIndex(DosDriveManager.GetDriveIndex(driveLetter));

    /// <summary>Appends the final directory separator character to the path and freezes the path builder.</summary>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>
    /// This can only be called once (as the path builder will be frozen and become immutable). This cannot be called
    /// if the path builder has already been frozen or a drive letter has not been assigned
    /// (<see cref="SetDriveIndex(int)"/> or <see cref="SetDriveLetter(char)"/> must be called at least once for the
    /// path to be valid).
    /// </remarks>
    public DosPathBuilderResult AppendFinalDirectorySeparator() {
        // Make sure a drive specification has been set.
        if (!HasDriveSpecification) {
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Cannot append if path builder is frozen.
        if (_isFrozen) {
            return DosPathBuilderResult.PathBuilderFrozen;
        }

        DebugValidateState();

        // A valid unfrozen path builder instance should never end with a directory separator character. So it should
        // be safe to append the final directory separator here.
        _pathBuilder.Append(DirectorySeparatorChar);

        _isFrozen = true;
        _pathStack.Dispose(); // path element stack is no longer necessary
        return DosPathBuilderResult.Success;
    }

    /// <summary>Appends a single file or directory name to the path builder.</summary>
    /// <param name="fileName">A valid file or directory name.</param>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>
    /// Any leading white space is always ignored. The file name cannot contain any invalid file name characters,
    /// cannot be empty, an invalid or reserved name, directory traversal (e.g., "." or ".."), or end with white space
    /// or a period. This cannot be called if the path builder has already been frozen or a drive letter has not been
    /// assigned (<see cref="SetDriveIndex(int)"/> or <see cref="SetDriveLetter(char)"/> must be called at least once
    /// for the path to be valid).
    /// </remarks>
    public DosPathBuilderResult AppendFileName(ReadOnlySpan<char> fileName) {
        // Make sure a drive specification has been set.
        if (!HasDriveSpecification) {
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Cannot append if path builder is frozen.
        if (_isFrozen) {
            return DosPathBuilderResult.PathBuilderFrozen;
        }

        DebugValidateState();

        // Trim leading white space from file name. Leading white space characters are always ignored.
        fileName = fileName.TrimStart();

        // Check for any invalid file name characters.
        if (fileName.ContainsAny(InvalidFileNameSearchValues)) {
            // Do not allow invalid file name characters in path element.
            return DosPathBuilderResult.InvalidFileNameCharacters;
        }

        // Check if file name is valid. Cannot be empty, invalid, reserved, or a directory traversal file name.
        DosSpecialFileName specialFileName = ParseSpecialFileName(fileName);
        if (specialFileName != DosSpecialFileName.None) {
            return DosPathBuilderResult.InvalidReservedFileName;
        }

        // DOS empty-extension marker: "NAME." canonicalizes to "NAME" (FreeDOS truename behavior).
        // ParseSpecialFileName has already validated that no more than one trailing dot is present.
        fileName = TrimTrailingEmptyExtensionDot(fileName);

        // Append directory separator and file name.
        Debug.Assert(_pathBuilder.Length >= 2);
        _pathStack.Push(_pathBuilder.Length);
        _pathBuilder.Append(DirectorySeparatorChar);
        _pathBuilder.Append(fileName);
        return DosPathBuilderResult.Success;
    }

    /// <summary>Appends a relative path to the path builder.</summary>
    /// <param name="relativePath">A relative file or directory path.</param>
    /// <param name="endsWithDirectorySeparator">Indicates if the input path ended with a directory separator.</param>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>
    /// All empty path elements are ignored. This will always append to the existing path, even if the path starts with
    /// a directory separator. Any leading white space in individual path elements are always ignored. The individual
    /// path elements cannot contain any invalid file name characters, invalid or reserved names, or end with white
    /// space or a period. This cannot be called if the path builder has already been frozen or a drive letter has not
    /// been assigned (<see cref="SetDriveIndex(int)"/> or <see cref="SetDriveLetter(char)"/> must be called at least
    /// once for the path to be valid).
    /// </remarks>
    public DosPathBuilderResult AppendRelativePath(ReadOnlySpan<char> relativePath,
            out bool endsWithDirectorySeparator) {
        // Make sure a drive specification has been set.
        if (!HasDriveSpecification) {
            endsWithDirectorySeparator = default;
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Cannot append if path builder is frozen.
        if (_isFrozen) {
            endsWithDirectorySeparator = default;
            return DosPathBuilderResult.PathBuilderFrozen;
        }

        DebugValidateState();

        // If the input path is empty or white space, then it does not end with a directory separator.
        if (relativePath.IsWhiteSpace()) {
            endsWithDirectorySeparator = false;
            return DosPathBuilderResult.Success;
        }

        ReadOnlySpan<char> lastPathElement = default;
        while (!relativePath.IsEmpty) {
            // Get next path element.
            int slashIndex = relativePath.IndexOfAny(DirectorySeparatorChar, AltDirectorySeparatorChar);
            if (slashIndex < 0) {
                lastPathElement = relativePath;
                relativePath = [];
            } else {
                lastPathElement = relativePath[..slashIndex];
                relativePath = relativePath[(slashIndex + 1)..];
            }

            // Trim leading white space and check for any invalid path characters.
            lastPathElement = lastPathElement.TrimStart();
            if (lastPathElement.ContainsAny(InvalidFileNameSearchValues)) {
                // Do not allow invalid file name characters in path element.
                endsWithDirectorySeparator = default;
                return DosPathBuilderResult.InvalidFileNameCharacters;
            }

            // Parse and handle current path element.
            DosSpecialFileName specialFileName = ParseSpecialFileName(lastPathElement);
            switch (specialFileName) {
                case DosSpecialFileName.None: {
                        // DOS empty-extension marker: "NAME." canonicalizes to "NAME" (FreeDOS truename).
                        ReadOnlySpan<char> appendElement = TrimTrailingEmptyExtensionDot(lastPathElement);
                        // Append normal file name element.
                        Debug.Assert(_pathBuilder.Length >= 2);
                        _pathStack.Push(_pathBuilder.Length);
                        _pathBuilder.Append(DirectorySeparatorChar);
                        _pathBuilder.Append(appendElement);
                        break;
                    }

                case DosSpecialFileName.Empty:
                case DosSpecialFileName.CurrentDirectory: {
                        // Skip special name "." and empty names.
                        break;
                    }

                case DosSpecialFileName.ParentDirectory: {
                        // Remove last appended directory element if possible.
                        if (!_pathStack.TryPop(out int lastPathLength)) {
                            // This may occur if the path is trying to traverse to the root or beyond the root. This is
                            // allowed in both cases, but the drive letter and volume separator will always be
                            // preserved.
                            lastPathLength = 2;
                        }

                        Debug.Assert(lastPathLength >= 2);
                        _pathBuilder.Length = lastPathLength;
                        break;
                    }

                default: {
                        // TODO: Are directory separators, drive specifications, and other path elements allowed for
                        // reserved device names?

                        // Do not allow reserved file names in the path. The path builder would need to be extended to
                        // allow full paths as well as special device names (which is currently not implemented).
                        endsWithDirectorySeparator = default;
                        return DosPathBuilderResult.InvalidReservedFileName;
                    }
            }
        }

        // At least one non-white space character was detected. If the last path element is empty, then there was a
        // directory separator before the last (empty) path element.
        endsWithDirectorySeparator = lastPathElement.IsEmpty;
        return DosPathBuilderResult.Success;
    }

    /// <summary>Appends a relative path to the path builder.</summary>
    /// <param name="relativePath">A relative file or directory path.</param>
    /// <param name="endsWithDirectorySeparator">Indicates if the input path ended with a directory separator.</param>
    /// <returns>A result indicating success or an error.</returns>
    /// <remarks>
    /// All empty path elements are ignored. This will always remove all path characters after the drive letter and
    /// volume separator, even if the path is empty or does not start with a directory separator. Any leading white
    /// space in individual path elements are always ignored. The individual path elements cannot contain any invalid
    /// file name characters, invalid or reserved names, or end with white space or a period. This cannot be called if
    /// the path builder has already been frozen or a drive letter has not been assigned
    /// (<see cref="SetDriveIndex(int)"/> or <see cref="SetDriveLetter(char)"/> must be called at least once for the
    /// path to be valid).
    /// </remarks>
    public DosPathBuilderResult AppendRootedPath(ReadOnlySpan<char> rootedPath, out bool endsWithDirectorySeparator) {
        // Make sure a drive specification has been set.
        if (!HasDriveSpecification) {
            endsWithDirectorySeparator = default;
            return DosPathBuilderResult.InvalidDriveSpecification;
        }

        // Cannot append if path builder is frozen.
        if (_isFrozen) {
            endsWithDirectorySeparator = default;
            return DosPathBuilderResult.PathBuilderFrozen;
        }

        // A rooted path will remove everything except the drive letter and volume separator.
        _pathStack.Clear();
        _pathBuilder.Length = 2;

        DebugValidateState();

        return AppendRelativePath(rootedPath, out endsWithDirectorySeparator);
    }

    /// <summary>
    /// Strips a single trailing dot from a path segment. Matches FreeDOS <c>truename</c> behavior:
    /// the trailing dot is the DOS empty-extension marker and is dropped during canonicalization.
    /// </summary>
    /// <remarks>
    /// Callers must have already validated the segment via <see cref="ParseSpecialFileName"/>, so any
    /// double-trailing-dot case has been rejected before reaching this method.
    /// </remarks>
    private static ReadOnlySpan<char> TrimTrailingEmptyExtensionDot(ReadOnlySpan<char> fileName) {
        if (fileName.Length >= 1 && fileName[^1] == '.') {
            return fileName[..^1];
        }
        return fileName;
    }

    /// <summary>Attempts to parse special file names and performs basic sanity checks.</summary>
    /// <param name="fileName">File name to parse.</param>
    /// <returns>
    /// One of the defined special file names, <see cref="DosSpecialFileName.Invalid"/> for an invalid/reserved file
    /// name, or <see cref="DosSpecialFileName.None"/> if the file name is a regular file or directory name.
    /// </returns>
    /// <remarks>
    /// This method does not check for any individual invalid file name characters. The caller must trim leading white
    /// space characters from the start of the file name prior to calling this method.
    /// </remarks>
    public readonly DosSpecialFileName ParseSpecialFileName(ReadOnlySpan<char> fileName) {
        // The algorithm/checks are derived from:
        // https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file#naming-conventions
        //
        // This might be more strict than the standard DOS file naming conventions, but it uses the Windows naming
        // conventions to prevent issues with potentially malicious or problematic DOS programs from creating file
        // names that are reserved on Windows (or file names that are difficult to remove/rename in the shell).
        //
        // NOTE: DO NOT THROW EXCEPTIONS IN THIS METHOD!

        if (fileName.IsEmpty) {
            return DosSpecialFileName.Empty;
        }

        Debug.Assert(!char.IsWhiteSpace(fileName[0]));

        // Do not allow file names that end with white space.
        if (char.IsWhiteSpace(fileName[^1])) {
            return DosSpecialFileName.Invalid;
        }

        // Ignore file extension if defined. Reserved names include file extensions. Also handle special/reserved file
        // names that start and/or end with period.
        int dotIndex = fileName.IndexOf('.');
        if (dotIndex >= 0) {
            // Check for "." and ".." directory traversal file names.
            if (dotIndex == 0) {
                if (fileName.Length == 1) {
                    return DosSpecialFileName.CurrentDirectory;
                }
                if (fileName.Length == 2 && fileName[1] == '.') {
                    return DosSpecialFileName.ParentDirectory;
                }
            }

            // Do not allow file names that end with a period unless that period is the empty-extension marker.
            // DOS semantics: a single trailing dot means "no extension" (FreeDOS truename strips it; see
            // kernel/kernel/newstuff.c "strip trailing dot"). Multiple trailing dots remain ill-formed
            // (FreeDOS PNE_DOT multi-dot rejection). Special file names "." and ".." are handled above.
            if (fileName[^1] == '.') {
                if (fileName.Length >= 2 && fileName[^2] == '.') {
                    return DosSpecialFileName.Invalid;
                }
                // Single trailing dot: strip it and re-evaluate. If an inner dot remains, the prefix
                // before it is the name segment (for reserved device-name detection). Otherwise the
                // whole stripped value is the name segment.
                fileName = fileName[..^1];
                dotIndex = fileName.IndexOf('.');
            }

            if (dotIndex >= 0) {
                // Trim white space to be more pedantic by not allowing any reserved names that have
                // white space at the end (before the file extension).
                fileName = fileName[..dotIndex].TrimEnd();
            }
        }

        // Note that "COM¹", "COM²", "COM³", "LPT¹", "LPT²", and "LPT³" (using ISO/IEC 8859-1 superscript numbers) are
        // always handled as special names (pedantic Windows naming convention). Parser settings are used to determine
        // whether they are treated as the associated devices or generic invalid reserved file names.

        // For improved lookup performance: this implementation first uses the device file name length to
        // differentiate, then it checks the first letter of the device names before checking the remaining characters.
        DosSpecialFileName deviceName = DosSpecialFileName.None;
        if (fileName.Length == 3) {
            switch (fileName[0]) {
                case 'C' or 'c':
                    // "CON"
                    if (fileName[1] is 'O' or 'o' && fileName[2] is 'N' or 'n') {
                        deviceName = DosSpecialFileName.Console;
                    }
                    break;

                case 'P' or 'p':
                    // "PRN"
                    if (fileName[1] is 'R' or 'r' && fileName[2] is 'N' or 'n') {
                        deviceName = DosSpecialFileName.Printer;
                    }
                    break;

                case 'A' or 'a':
                    // "AUX"
                    if (fileName[1] is 'U' or 'u' && fileName[2] is 'X' or 'x') {
                        deviceName = DosSpecialFileName.Auxiliary;
                    }
                    break;

                case 'N' or 'n':
                    // "NUL"
                    if (fileName[1] is 'U' or 'u' && fileName[2] is 'L' or 'l') {
                        deviceName = DosSpecialFileName.Null;
                    }
                    break;
            }
        } else if (fileName.Length == 4) {
            switch (fileName[0]) {
                case 'C' or 'c':
                    // "COM1" thru "COM9", "COM¹", "COM²", "COM³"
                    if (fileName[1] is 'O' or 'o' && fileName[2] is 'M' or 'm') {
                        switch (fileName[3]) {
                            case '1': // ASCII digit '1'
                                deviceName = DosSpecialFileName.SerialPort1;
                                break;
                            case '2': // ASCII digit '2'
                                deviceName = DosSpecialFileName.SerialPort2;
                                break;
                            case '3': // ASCII digit '3'
                                deviceName = DosSpecialFileName.SerialPort3;
                                break;
                            case '4': // ASCII digit '4'
                                deviceName = DosSpecialFileName.SerialPort4;
                                break;
                            case '5': // ASCII digit '5'
                                deviceName = DosSpecialFileName.SerialPort5;
                                break;
                            case '6': // ASCII digit '6'
                                deviceName = DosSpecialFileName.SerialPort6;
                                break;
                            case '7': // ASCII digit '7'
                                deviceName = DosSpecialFileName.SerialPort7;
                                break;
                            case '8': // ASCII digit '8'
                                deviceName = DosSpecialFileName.SerialPort8;
                                break;
                            case '9': // ASCII digit '9'
                                deviceName = DosSpecialFileName.SerialPort9;
                                break;

                            case '¹': // Superscript '1'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.SerialPort1;
                                break;
                            case '²': // Superscript '2'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.SerialPort2;
                                break;
                            case '³': // Superscript '3'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.SerialPort3;
                                break;
                        }
                    }
                    break;

                case 'L' or 'l':
                    // "LPT1" thru "LPT9", "LPT¹", "LPT²", "LPT³"
                    if (fileName[1] is 'P' or 'p' && fileName[2] is 'T' or 't') {
                        switch (fileName[3]) {
                            case '1': // ASCII digit '1'
                                deviceName = DosSpecialFileName.ParallelPort1;
                                break;
                            case '2': // ASCII digit '2'
                                deviceName = DosSpecialFileName.ParallelPort2;
                                break;
                            case '3': // ASCII digit '3'
                                deviceName = DosSpecialFileName.ParallelPort3;
                                break;
                            case '4': // ASCII digit '4'
                                deviceName = DosSpecialFileName.ParallelPort4;
                                break;
                            case '5': // ASCII digit '5'
                                deviceName = DosSpecialFileName.ParallelPort5;
                                break;
                            case '6': // ASCII digit '6'
                                deviceName = DosSpecialFileName.ParallelPort6;
                                break;
                            case '7': // ASCII digit '7'
                                deviceName = DosSpecialFileName.ParallelPort7;
                                break;
                            case '8': // ASCII digit '8'
                                deviceName = DosSpecialFileName.ParallelPort8;
                                break;
                            case '9': // ASCII digit '9'
                                deviceName = DosSpecialFileName.ParallelPort9;
                                break;

                            case '¹': // Superscript '1'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.ParallelPort1;
                                break;
                            case '²': // Superscript '2'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.ParallelPort2;
                                break;
                            case '³': // Superscript '3'
                                deviceName = _specialFileNameSettings.HasFlag(DosSpecialFileNameSettings.NoDeviceSuperscriptDigits)
                                    ? DosSpecialFileName.Invalid // always block (pedantic Windows naming rule)
                                    : DosSpecialFileName.ParallelPort3;
                                break;
                        }
                    }
                    break;
            }
        }

        return deviceName;
    }

    /// <summary>Performs debug assertions to validate the state of the path builder.</summary>
    /// <remarks>
    /// This is only executed when using debug builds. Passing these state checks does not necessarily mean that that
    /// path builder contains a valid DOS full path specification.
    /// </remarks>
    [Conditional("DEBUG")]
    internal readonly void DebugValidateState() {
        ReadOnlySpan<char> path = _pathBuilder.AsSpan();
        ReadOnlySpan<int> pathElementIndexes = _pathStack.AsSpan();

        // An path builder without a drive specification is only in a valid state if it is empty, there are no path
        // elements, and it is not frozen. (A valid state does not mean that it is a valid path though.)
        if (!HasDriveSpecification) {
            Debug.Assert(path.Length == 0);
            Debug.Assert(pathElementIndexes.Length == 0);
            Debug.Assert(!IsFrozen);
            return;
        }

        // A valid path should always start with an uppercase drive letter and volume separator.
        Debug.Assert(path.Length >= 2 && char.IsAsciiLetterUpper(path[0]) && path[1] == VolumeSeparatorChar);
        Debug.Assert(HasDriveSpecification); // pedantic check

        // Frozen paths can break some of the path rules. Frozen paths can end with a directory separator and they can
        // have an empty path element stack.
        if (IsFrozen) {
            // A valid frozen path should always be at least 3 characters and have a directory separator immediately
            // following the drive specification.
            Debug.Assert(path.Length >= 3 && path[2] == DirectorySeparatorChar);
            return;
        }

        // The last character in a mutable path builder cannot be a directory separator.
        Debug.Assert(path[^1] != DirectorySeparatorChar);

        // There should be no path elements if there is only a drive specification.
        Debug.Assert(path.Length > 2 || pathElementIndexes.IsEmpty);

        // Validate the path element stack.
        int lastPathIndex = -1;
        for (int i = 0; i < pathElementIndexes.Length; i++) {
            int pathIndex = pathElementIndexes[i];

            // The first path element must immediately follow the drive specification.
            Debug.Assert(i != 0 || pathIndex == 2);

            // All path elements must be a valid index specified after the drive specification.
            Debug.Assert(pathIndex >= 2);
            Debug.Assert(pathIndex < path.Length);

            // All path elements must point to a directory separator character.
            Debug.Assert(path[pathIndex] == DirectorySeparatorChar);

            // All path elements must be in order with no duplicates.
            Debug.Assert(pathIndex > lastPathIndex);
            lastPathIndex = pathIndex;
        }
    }

    /// <summary>Gets a default error message from a result to use for logging or throwing exceptions.</summary>
    /// <param name="result">The DOS path builder result.</param>
    /// <returns>
    /// <see langword="null"/> if the result is <see cref="DosPathBuilderResult.Success"/>; otherwise, a string
    /// containing the interpreted error message.
    /// </returns>
    public static string? GetResultErrorMessage(DosPathBuilderResult result) => result switch {
        DosPathBuilderResult.Success => null,
        DosPathBuilderResult.PathBuilderFrozen => "DOS path builder is frozen and cannot be modified.",
        DosPathBuilderResult.InvalidDriveSpecification => "DOS drive specification is invalid.",
        DosPathBuilderResult.InvalidFileNameCharacters => "DOS path contains invalid file name characters.",
        DosPathBuilderResult.InvalidReservedFileName => "DOS path contains a reserved file name.",
        _ => $"DOS path builder unspecified error: {result}"
    };
}
