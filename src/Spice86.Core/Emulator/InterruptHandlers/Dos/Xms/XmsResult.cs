namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// Represents the result of an XMS operation as defined in the eXtended Memory Specification (XMS) version 3.0.
/// </summary>
/// <remarks>
/// XMS functions return status and additional data through CPU registers:
/// <list type="bullet">
/// <item>AX = 0001h indicates success, 0000h indicates failure</item>
/// <item>On failure, BL contains the XMS error code (values 80h-FFh)</item>
/// <item>On success, other registers may contain additional information depending on the function</item>
/// </list>
/// This struct encapsulates all the possible return values and provides a type-safe way to handle XMS function results.
/// </remarks>
public readonly struct XmsResult {
    /// <summary>
    /// Gets a value indicating whether the XMS operation was successful.
    /// </summary>
    /// <remarks>
    /// Maps to the AX register result in XMS calls, where 0001h indicates success and 0000h indicates failure.
    /// All XMS functions return this success/failure indicator as their primary result.
    /// </remarks>
    public bool Success { get; }

    /// <summary>
    /// Gets the XMS error code for failed operations (0 for success, otherwise an XMS error code).
    /// </summary>
    /// <remarks>
    /// Maps to the BL register on function failure. All XMS error codes have their high bit set (values 80h-FFh).
    /// Error codes are standardized across XMS implementations and indicate the specific reason for failure,
    /// such as invalid handle, insufficient memory, or hardware error.
    /// </remarks>
    public byte ErrorCode { get; }

    /// <summary>
    /// Gets the primary return value of a successful XMS operation.
    /// </summary>
    /// <remarks>
    /// This value is typically placed in:
    /// <list type="bullet">
    /// <item>DX for handle operations (e.g., allocated handle value)</item>
    /// <item>AX for version queries (as a BCD value, e.g., 0300h for version 3.00)</item>
    /// <item>BH for block lock counts</item>
    /// </list>
    /// The specific register and meaning depends on the XMS function being called.
    /// </remarks>
    public ushort PrimaryValue { get; }

    /// <summary>
    /// Gets the secondary return value of a successful XMS operation.
    /// </summary>
    /// <remarks>
    /// This value is typically placed in:
    /// <list type="bullet">
    /// <item>BX for driver internal revision or number of free handles</item>
    /// <item>DX for memory block size information</item>
    /// <item>BL for various status values</item>
    /// </list>
    /// The specific register and meaning depends on the XMS function being called.
    /// </remarks>
    public ushort SecondaryValue { get; }

    /// <summary>
    /// Gets the tertiary return value of a successful XMS operation.
    /// </summary>
    /// <remarks>
    /// This value is typically placed in DX for functions that return multiple values,
    /// such as memory size information. For most XMS functions this value is not used.
    /// </remarks>
    public ushort TertiaryValue { get; }

    /// <summary>
    /// Gets the 32-bit extended result for 386+ XMS functions that return 32-bit values.
    /// </summary>
    /// <remarks>
    /// Used by 32-bit extended functions introduced in XMS 3.0 such as:
    /// <list type="bullet">
    /// <item>Function 88h (Query Any Free Extended Memory) - returns size of largest block in EDX:EAX</item>
    /// <item>Function 89h (Allocate Any Extended Memory) - returns actual size in ECX:EBX</item>
    /// <item>Function 8Fh (Realloc Any Extended Memory) - returns new size in ECX:EBX</item>
    /// </list>
    /// These functions allow handling memory beyond the 64MB limit of the original 16-bit functions.
    /// </remarks>
    public uint ExtendedResult { get; }

    /// <summary>
    /// Gets a value indicating whether to set AX directly for special functions like version query.
    /// </summary>
    /// <remarks>
    /// For functions like Get XMS Version Number (00h), the AX register contains the actual return value
    /// rather than just a success indicator. When this flag is true, the PrimaryValue should be
    /// placed directly in AX instead of the usual 0001h success code.
    /// </remarks>
    public bool DirectAx { get; }

    /// <summary>
    /// Creates a successful result with no additional data.
    /// </summary>
    /// <remarks>
    /// Used for simple functions that only need to report success/failure,
    /// such as Release High Memory Area (Function 02h).
    /// </remarks>
    /// <returns>A successful XMS result object with default values.</returns>
    public static XmsResult CreateSuccess() => new(true, 0);

    /// <summary>
    /// Creates a successful result with primary and optional secondary values.
    /// </summary>
    /// <remarks>
    /// Used for functions that return one or more values on success, such as:
    /// <list type="bullet">
    /// <item>Allocate Extended Memory Block (09h) - handle value in DX</item>
    /// <item>Lock Extended Memory Block (0Ch) - 32-bit address in DX:BX</item>
    /// <item>Get Handle Information (0Eh) - various values in BH, BL, DX</item>
    /// </list>
    /// </remarks>
    /// <param name="primaryValue">Primary return value (usually placed in DX for handle operations).</param>
    /// <param name="secondaryValue">Secondary return value (usually placed in BX).</param>
    /// <param name="tertiaryValue">Third return value (often placed in another register).</param>
    /// <returns>A successful XMS result object with the specified values.</returns>
    public static XmsResult CreateSuccess(ushort primaryValue, ushort secondaryValue = 0, ushort tertiaryValue = 0) =>
        new(true, 0, primaryValue, secondaryValue, tertiaryValue);

    /// <summary>
    /// Creates a successful result with extended 32-bit data.
    /// </summary>
    /// <remarks>
    /// Used for 386+ extended functions that return 32-bit values, such as:
    /// <list type="bullet">
    /// <item>Query Any Free Extended Memory (88h) - size in bytes as 32-bit value</item>
    /// <item>Allocate Any Extended Memory (89h) - allocated size in bytes as 32-bit value</item>
    /// </list>
    /// These functions were added in XMS 3.0 to support memory beyond the 64MB limit.
    /// </remarks>
    /// <param name="extendedResult">The 32-bit extended result value.</param>
    /// <returns>A successful XMS result object with 32-bit data.</returns>
    public static XmsResult CreateSuccessExtended(uint extendedResult) =>
        new(true, 0, extendedResult: extendedResult);

    /// <summary>
    /// Creates a special direct AX result for functions like Get XMS Version Number (00h).
    /// </summary>
    /// <remarks>
    /// The XMS Get Version function returns the version in AX as a 16-bit BCD value
    /// (e.g., 0300h for version 3.00), the internal driver revision in BX,
    /// and HMA existence information in DX. This result type signals that AX should
    /// be set to the provided value rather than the success code of 0001h.
    /// </remarks>
    /// <param name="ax">Value for AX register (typically XMS version in BCD format).</param>
    /// <param name="bx">Value for BX register (typically driver revision number).</param>
    /// <param name="dx">Value for DX register (typically HMA existence flag).</param>
    /// <returns>A special XMS result object that will set AX directly.</returns>
    public static XmsResult DirectRegister(ushort ax, ushort bx = 0, ushort dx = 0) =>
        new(true, 0, ax, bx, dx, directAx: true);

    /// <summary>
    /// Creates a failure result with the specified error code.
    /// </summary>
    /// <remarks>
    /// All XMS error codes have their high bit set (values 80h-FFh) as specified in the XMS standard.
    /// Common error codes include:
    /// <list type="bullet">
    /// <item>80h - Function not implemented</item>
    /// <item>A0h - All extended memory is allocated</item>
    /// <item>A1h - All available extended memory handles are in use</item>
    /// <item>A2h - Invalid handle</item>
    /// </list>
    /// These error codes are standardized across XMS implementations.
    /// </remarks>
    /// <param name="errorCode">The standardized XMS error code to return in BL.</param>
    /// <returns>A failure XMS result object with the specified error code.</returns>
    public static XmsResult Error(XmsErrorCodes errorCode) => new(false, (byte)errorCode);

    /// <summary>
    /// Initializes a new instance of the <see cref="XmsResult"/> struct with the specified values.
    /// </summary>
    /// <remarks>
    /// This constructor allows creating custom XMS results with complete control over all fields.
    /// Most callers should use the static factory methods like <see cref="CreateSuccess"/> or
    /// <see cref="Error"/> rather than calling this constructor directly.
    /// </remarks>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errorCode">The error code (0 for success, otherwise an XMS error code).</param>
    /// <param name="primaryValue">Primary return value.</param>
    /// <param name="secondaryValue">Secondary return value.</param>
    /// <param name="tertiaryValue">Third return value.</param>
    /// <param name="extendedResult">32-bit extended result for 386+ functions.</param>
    /// <param name="directAx">Whether to set AX directly rather than to success code.</param>
    public XmsResult(bool success, byte errorCode, ushort primaryValue = 0,
        ushort secondaryValue = 0, ushort tertiaryValue = 0, uint extendedResult = 0, bool directAx = false) {
        Success = success;
        ErrorCode = errorCode;
        PrimaryValue = primaryValue;
        SecondaryValue = secondaryValue;
        TertiaryValue = tertiaryValue;
        ExtendedResult = extendedResult;
        DirectAx = directAx;
    }
}