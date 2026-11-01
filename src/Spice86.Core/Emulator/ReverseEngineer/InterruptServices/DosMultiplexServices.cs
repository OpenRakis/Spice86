namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 2Fh services.
/// </summary>
public sealed class DosMultiplexServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosMultiplexServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public DosMultiplexServices(CSharpOverrideHelper helper) : base(helper, 0x2F, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 2Fh AH=10h: SHARE.EXE multiplex services (AL selects).
    /// </summary>
    public void ShareExeServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x10);
    }

    /// <summary>
    /// INT 2Fh AH=16h: DOS virtual machine multiplex services (AL selects).
    /// </summary>
    public void DosVirtualMachineServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x16);
    }

    /// <summary>
    /// INT 2Fh AH=1Ah: ANSI.SYS console multiplex services (AL selects).
    /// </summary>
    public void AnsiConsoleServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x1A);
    }

    /// <summary>
    /// INT 2Fh AH=43h: XMS driver multiplex services (AL selects).
    /// </summary>
    public void XmsServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x43);
    }

    /// <summary>
    /// INT 2Fh AH=46h: Windows virtual machine multiplex services (AL selects).
    /// </summary>
    public void WindowsVirtualMachineServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x46);
    }

    /// <summary>
    /// INT 2Fh AH=4Ah: high memory area (HMA) multiplex services (AL selects).
    /// </summary>
    public void HighMemoryAreaServices(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x4A);
    }
}
