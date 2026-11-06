namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;

/// <summary>
/// Typed helpers for the INT 2Fh services.
/// </summary>
public sealed class DosMultiplexServices : InterruptServiceGroup<DosInt2fHandler> {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosMultiplexServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosMultiplexServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 2Fh AH=10h: SHARE.EXE multiplex services (AL selects).
    /// </summary>
    public byte ShareExeServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ShareExeServices), () => Handler.ShareExeServices(al));
    }

    /// <summary>
    /// INT 2Fh AH=16h: DOS virtual machine multiplex services (AL selects).
    /// </summary>
    public void DosVirtualMachineServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(DosVirtualMachineServices), () => Handler.ReportDosVirtualMachineService(
                (ushort)((EntryOf(nameof(DosVirtualMachineServices)).SelectorValue << 8) | al)));
    }

    /// <summary>
    /// INT 2Fh AH=1Ah: ANSI.SYS console multiplex services (AL selects).
    /// </summary>
    public byte AnsiConsoleServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(AnsiConsoleServices), () => Handler.AnsiConsoleServices(al));
    }

    /// <summary>
    /// INT 2Fh AH=43h: XMS driver multiplex services (AL selects).
    /// </summary>
    public (byte al, ushort bx, ushort es, bool carryFlag) XmsServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirectWithCarry(expectedReturnCs, expectedReturnIp,
            nameof(XmsServices), () => Handler.XmsServices(al, State.BX, State.ES), result => result.carryFlag);
    }

    /// <summary>
    /// INT 2Fh AH=46h: Windows virtual machine multiplex services (AL selects).
    /// </summary>
    public ushort WindowsVirtualMachineServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        ushort? ax = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(WindowsVirtualMachineServices), () => Handler.WindowsVirtualMachineServices(al));
        return ax ?? (ushort)((EntryOf(nameof(WindowsVirtualMachineServices)).SelectorValue << 8) | al);
    }

    /// <summary>
    /// INT 2Fh AH=4Ah: high memory area (HMA) multiplex services (AL selects).
    /// </summary>
    public (ushort bx, ushort di, ushort es) HighMemoryAreaServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(HighMemoryAreaServices), () => Handler.HighMemoryAreaServices(al, State.BX, State.DI, State.ES));
    }
}
