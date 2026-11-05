namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Typed helpers for the INT 67h services.
/// </summary>
public sealed class EmsServices : InterruptServiceGroup {
    /// <summary>
    /// Initializes a new instance of the <see cref="EmsServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public EmsServices(CSharpOverrideHelper helper) : base(helper, 0x67, ServiceSelector.Ah) {
    }

    /// <summary>
    /// INT 67h AH=40h: returns the EMM status in AH.
    /// </summary>
    public void GetStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x40);
    }

    /// <summary>
    /// INT 67h AH=41h: returns the page frame segment in BX; status in AH.
    /// </summary>
    public void GetPageFrameSegment(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x41);
    }

    /// <summary>
    /// INT 67h AH=42h: returns the unallocated and total page counts in BX and DX; status in AH.
    /// </summary>
    public void GetUnallocatedPageCount(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x42);
    }

    /// <summary>
    /// INT 67h AH=43h: allocates BX expanded memory pages and returns the handle in DX; status in AH.
    /// </summary>
    public void AllocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        Call(expectedReturnCs, expectedReturnIp, 0x43);
    }

    /// <summary>
    /// INT 67h AH=44h: maps or unmaps logical page BX of handle DX to physical page AL; status in AH.
    /// </summary>
    public void MapUnmapHandlePage(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x44);
    }

    /// <summary>
    /// INT 67h AH=45h: deallocates the pages of handle DX; status in AH.
    /// </summary>
    public void DeallocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x45);
    }

    /// <summary>
    /// INT 67h AH=46h: returns the EMM version in AL; status in AH.
    /// </summary>
    public void GetEmmVersion(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x46);
    }

    /// <summary>
    /// INT 67h AH=47h: saves the page map of handle DX; status in AH.
    /// </summary>
    public void SavePageMap(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x47);
    }

    /// <summary>
    /// INT 67h AH=48h: restores the page map of handle DX; status in AH.
    /// </summary>
    public void RestorePageMap(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x48);
    }

    /// <summary>
    /// INT 67h AH=4Bh: returns the number of EMM handles in BX; status in AH.
    /// </summary>
    public void GetEmmHandleCount(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        CheckVector();
        State.AL = al;
        Call(expectedReturnCs, expectedReturnIp, 0x4B);
    }

    /// <summary>
    /// INT 67h AH=4Ch: returns the page count of handle DX in BX; status in AH.
    /// </summary>
    public void GetHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        CheckVector();
        State.AL = al;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x4C);
    }

    /// <summary>
    /// INT 67h AH=4Dh: writes the handle/page-count table to ES:DI; status in AH.
    /// </summary>
    public void GetAllHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x4D);
    }

    /// <summary>
    /// INT 67h AH=50h: maps or unmaps CX pages of handle DX from the table at DS:SI (AL selects); status in AH.
    /// </summary>
    public void MapUnmapMultipleHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort si, ushort ds) {
        CheckVector();
        State.DS = ds;
        State.AL = al;
        State.CX = cx;
        State.DX = dx;
        State.SI = si;
        Call(expectedReturnCs, expectedReturnIp, 0x50);
    }

    /// <summary>
    /// INT 67h AH=51h: resizes handle DX to BX pages; status in AH.
    /// </summary>
    public void ReallocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        CheckVector();
        State.AL = al;
        State.BX = bx;
        State.DX = dx;
        Call(expectedReturnCs, expectedReturnIp, 0x51);
    }

    /// <summary>
    /// INT 67h AH=53h: gets or sets the 8-byte name of handle DX via ES:DI (AL selects); status in AH.
    /// </summary>
    public void GetSetHandleName(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort si, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.DX = dx;
        State.SI = si;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x53);
    }

    /// <summary>
    /// INT 67h AH=58h: writes the mappable physical address array to ES:DI; status in AH.
    /// </summary>
    public void GetMappablePhysicalAddressArray(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x58);
    }

    /// <summary>
    /// INT 67h AH=59h: writes expanded memory hardware information to ES:DI (AL selects); status in AH.
    /// </summary>
    public void GetExpandedMemoryHardwareInformation(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        CheckVector();
        State.ES = es;
        State.AL = al;
        State.DI = di;
        Call(expectedReturnCs, expectedReturnIp, 0x59);
    }
}
