namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 14h services.
/// </summary>
public sealed class SerialServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="SerialServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public SerialServices(CSharpOverrideHelper helper) : base(helper, 0x14, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 14h AH=00h: initializes serial port DX with the parameters in AL; returns the status in AX.
    /// </summary>
    public void InitializePort(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x00);
    }

    /// <summary>
    /// INT 14h AH=01h: writes the character in AL to serial port DX; returns the status in AH.
    /// </summary>
    public void TransmitCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x01);
    }

    /// <summary>
    /// INT 14h AH=02h: reads a character from serial port DX into AL; returns the status in AH.
    /// </summary>
    public void ReceiveCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x02);
    }

    /// <summary>
    /// INT 14h AH=03h: returns the status of serial port DX in AX.
    /// </summary>
    public void GetPortStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x03);
    }
}
