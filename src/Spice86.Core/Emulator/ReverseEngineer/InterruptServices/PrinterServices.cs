namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 17h services.
/// </summary>
public sealed class PrinterServices : InterruptServiceGroup<SystemBiosInt17Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public PrinterServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 17h AH=00h: writes the character in AL to printer DX; returns the status in AH.
    /// </summary>
    public (byte ah, bool carryFlag) WriteCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(WriteCharacter),
            () => Handler.WriteCharacter(al, dx), result => result.carryFlag);

    /// <summary>
    /// INT 17h AH=01h: initializes printer port DX; returns the status in AH.
    /// </summary>
    public (byte ah, bool carryFlag) InitializePrinterPort(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(InitializePrinterPort),
            () => Handler.InitializePrinterPort(dx), result => result.carryFlag);

    /// <summary>
    /// INT 17h AH=02h: returns the status of printer DX in AH.
    /// </summary>
    public (byte ah, bool carryFlag) GetPrinterStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) =>
        CallDirectWithCarry(expectedReturnCs, expectedReturnIp, nameof(GetPrinterStatus),
            () => Handler.GetPrinterStatus(dx), result => result.carryFlag);
}
