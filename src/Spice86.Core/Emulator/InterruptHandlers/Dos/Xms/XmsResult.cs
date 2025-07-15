namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Represents the result of an XMS operation.
/// </summary>
public readonly struct XmsResult {
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the error code (0 for success, otherwise an XMS error code).
    /// </summary>
    public byte ErrorCode { get; }

    /// <summary>
    /// Gets the primary return value (usually placed in AX for version queries or in DX for handle operations).
    /// </summary>
    public ushort PrimaryValue { get; }

    /// <summary>
    /// Gets the secondary return value (usually placed in BX, DX, or other registers).
    /// </summary>
    public ushort SecondaryValue { get; }

    /// <summary>
    /// Gets the third return value (often placed in DX for memory sizes).
    /// </summary>
    public ushort TertiaryValue { get; }

    /// <summary>
    /// Gets the 32-bit extended result (for 32-bit XMS functions).
    /// </summary>
    public uint ExtendedResult { get; }

    /// <summary>
    /// Whether to set AX directly (for special functions like version query)
    /// </summary>
    public bool DirectAx { get; }

    /// <summary>
    /// Creates a successful result with no additional data.
    /// </summary>
    public static XmsResult CreateSuccess() => new(true, 0);

    /// <summary>
    /// Creates a successful result with primary and optional secondary values.
    /// </summary>
    /// <param name="primaryValue">Primary return value (usually placed in DX).</param>
    /// <param name="secondaryValue">Secondary return value (usually placed in BX).</param>
    /// <param name="tertiaryValue">Third return value (often placed in another register).</param>
    /// <returns>A successful XMS result object.</returns>
    public static XmsResult CreateSuccess(ushort primaryValue, ushort secondaryValue = 0, ushort tertiaryValue = 0) =>
        new(true, 0, primaryValue, secondaryValue, tertiaryValue);

    /// <summary>
    /// Creates a successful result with extended 32-bit data.
    /// </summary>
    /// <param name="extendedResult">The 32-bit extended result value.</param>
    /// <returns>A successful XMS result object with 32-bit data.</returns>
    public static XmsResult CreateSuccessExtended(uint extendedResult) =>
        new(true, 0, extendedResult: extendedResult);

    /// <summary>
    /// Creates a special direct AX result (for functions like Get Version).
    /// </summary>
    /// <param name="ax">Value for AX register.</param>
    /// <param name="bx">Value for BX register.</param>
    /// <param name="dx">Value for DX register.</param>
    /// <returns>A special XMS result object that will set AX directly.</returns>
    public static XmsResult DirectRegister(ushort ax, ushort bx = 0, ushort dx = 0) =>
        new(true, 0, ax, bx, dx, directAx: true);

    /// <summary>
    /// Creates a failure result with the specified error code.
    /// </summary>
    /// <param name="errorCode">The XMS error code.</param>
    /// <returns>A failure XMS result object.</returns>
    public static XmsResult Error(XmsErrorCodes errorCode) => new(false, (byte)errorCode);

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