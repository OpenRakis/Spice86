namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 17h services.
/// </summary>
public sealed class PrinterServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public PrinterServices(CSharpOverrideHelper helper) : base(helper, 0x17, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 17h AH=00h: writes the character in AL to printer DX; returns the status in AH.
    /// </summary>
    public void WriteCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 17h AH=01h: initializes printer port DX; returns the status in AH.
    /// </summary>
    public void InitializePrinterPort(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 17h AH=02h: returns the status of printer DX in AH.
    /// </summary>
    public void GetPrinterStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }
}
