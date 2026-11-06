namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Bios;

/// <summary>
/// Typed helpers for the INT 14h services.
/// </summary>
public sealed class SerialServices : InterruptServiceGroup<SystemBiosInt14Handler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="SerialServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public SerialServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 14h AH=00h: initializes serial port DX with the parameters in AL; returns the status in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) InitializePort(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        (ushort? ax, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(InitializePort), () => Handler.InitializePort(al, dx), result => result.carryFlag);
        return (ax ?? (ushort)((EntryOf(nameof(InitializePort)).SelectorValue << 8) | al), carryFlag);
    }

    /// <summary>
    /// INT 14h AH=01h: writes the character in AL to serial port DX; returns the status in AH.
    /// </summary>
    public (byte ah, bool carryFlag) TransmitCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        (byte? ah, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(TransmitCharacter), () => Handler.TransmitCharacter(al, dx), result => result.carryFlag);
        return (ah ?? (byte)EntryOf(nameof(TransmitCharacter)).SelectorValue, carryFlag);
    }

    /// <summary>
    /// INT 14h AH=02h: reads a character from serial port DX into AL; returns the status in AH.
    /// </summary>
    public (ushort ax, bool carryFlag) ReceiveCharacter(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        (ushort? ax, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(ReceiveCharacter), () => Handler.ReceiveCharacter(al, dx), result => result.carryFlag);
        return (ax ?? (ushort)((EntryOf(nameof(ReceiveCharacter)).SelectorValue << 8) | al), carryFlag);
    }

    /// <summary>
    /// INT 14h AH=03h: returns the status of serial port DX in AX.
    /// </summary>
    public (ushort ax, bool carryFlag) GetPortStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        (ushort? ax, bool carryFlag) = CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(GetPortStatus), () => Handler.GetPortStatus(dx), result => result.carryFlag);
        return (ax ?? (ushort)((EntryOf(nameof(GetPortStatus)).SelectorValue << 8) | al), carryFlag);
    }
}
