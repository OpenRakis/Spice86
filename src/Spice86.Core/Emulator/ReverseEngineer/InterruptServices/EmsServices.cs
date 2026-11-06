namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Shared.Utils;

/// <summary>
/// Typed helpers for the INT 67h services.
/// </summary>
public sealed class EmsServices : InterruptServiceGroup<ExpandedMemoryManager> {
    /// <summary>
    /// Initializes a new instance of the <see cref="EmsServices"/> class.
    /// </summary>
    /// <param name="helper">The override helper the interrupt calls go through.</param>
    public EmsServices(CSharpOverrideHelper helper) : base(helper) {
    }

    /// <summary>
    /// INT 67h AH=40h: returns the EMM status in AH.
    /// </summary>
    public byte GetStatus(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetStatus), () => Handler.GetStatus());
    }

    /// <summary>
    /// INT 67h AH=41h: returns the page frame segment in BX; status in AH.
    /// </summary>
    public (byte ah, ushort bx) GetPageFrameSegment(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetPageFrameSegment), () => Handler.GetPageFrameSegment());
    }

    /// <summary>
    /// INT 67h AH=42h: returns the unallocated and total page counts in BX and DX; status in AH.
    /// </summary>
    public (byte ah, ushort dx, ushort bx) GetUnallocatedPageCount(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetUnallocatedPageCount), () => Handler.GetUnallocatedPageCount());
    }

    /// <summary>
    /// INT 67h AH=43h: allocates BX expanded memory pages and returns the handle in DX; status in AH.
    /// </summary>
    public (byte ah, ushort dx) AllocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort bx) {
        (byte ah, ushort? dx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(AllocatePages), () => Handler.AllocatePageHandle(bx));
        return (ah, dx ?? State.DX);
    }

    /// <summary>
    /// INT 67h AH=44h: maps or unmaps logical page BX of handle DX to physical page AL; status in AH.
    /// </summary>
    public byte MapUnmapHandlePage(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(MapUnmapHandlePage), () => Handler.MapUnmapHandlePage(bx, al, dx));
    }

    /// <summary>
    /// INT 67h AH=45h: deallocates the pages of handle DX; status in AH.
    /// </summary>
    public byte DeallocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(DeallocatePages), () => Handler.DeallocatePages(dx));
    }

    /// <summary>
    /// INT 67h AH=46h: returns the EMM version in AL; status in AH.
    /// </summary>
    public ushort GetEmmVersion(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetEmmVersion), () => Handler.GetEmmVersion());
    }

    /// <summary>
    /// INT 67h AH=47h: saves the page map of handle DX; status in AH.
    /// </summary>
    public byte SavePageMap(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(SavePageMap), () => Handler.SavePageMap(dx));
    }

    /// <summary>
    /// INT 67h AH=48h: restores the page map of handle DX; status in AH.
    /// </summary>
    public byte RestorePageMap(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(RestorePageMap), () => Handler.RestorePageMap(dx));
    }

    /// <summary>
    /// INT 67h AH=4Bh: returns the number of EMM handles in BX; status in AH.
    /// </summary>
    public (byte ah, ushort bx) GetEmmHandleCount(ushort expectedReturnCs, ushort expectedReturnIp, byte al) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetEmmHandleCount), () => Handler.GetEmmHandleCount());
    }

    /// <summary>
    /// INT 67h AH=4Ch: returns the page count of handle DX in BX; status in AH.
    /// </summary>
    public (ushort ax, ushort bx) GetHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx) {
        (byte ah, ushort bx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetHandlePages), () => Handler.GetHandlePages(dx));
        return ((ushort)(ah << 8), bx);
    }

    /// <summary>
    /// INT 67h AH=4Dh: writes the handle/page-count table to ES:DI; status in AH.
    /// </summary>
    public (ushort ax, ushort bx) GetAllHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        (byte ah, ushort bx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetAllHandlePages), () => Handler.GetAllHandlePages(MemoryUtils.ToPhysicalAddress(es, di)));
        return ((ushort)(ah << 8), bx);
    }

    /// <summary>
    /// INT 67h AH=50h: maps or unmaps CX pages of handle DX from the table at DS:SI (AL selects); status in AH.
    /// </summary>
    public byte MapUnmapMultipleHandlePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort cx, ushort dx, ushort si, ushort ds) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(MapUnmapMultipleHandlePages),
            () => Handler.MapUnmapMultipleHandlePages(al, cx, dx, MemoryUtils.ToPhysicalAddress(ds, si)));
    }

    /// <summary>
    /// INT 67h AH=51h: resizes handle DX to BX pages; status in AH.
    /// </summary>
    public byte ReallocatePages(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort bx) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(ReallocatePages), () => Handler.ReallocatePages(dx, bx));
    }

    /// <summary>
    /// INT 67h AH=53h: gets or sets the 8-byte name of handle DX via ES:DI (AL selects); status in AH.
    /// </summary>
    public byte GetSetHandleName(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort dx, ushort si, ushort di, ushort es) {
        uint nameAddress = al == EmmSubFunctionsCodes.HandleNameSet
            ? MemoryUtils.ToPhysicalAddress(si, di)
            : MemoryUtils.ToPhysicalAddress(es, di);
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetSetHandleName), () => Handler.GetSetHandleName(dx, al, nameAddress));
    }

    /// <summary>
    /// INT 67h AH=58h: writes the mappable physical address array to ES:DI; status in AH.
    /// </summary>
    public (byte ah, ushort cx) GetMappablePhysicalAddressArray(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        return CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetMappablePhysicalAddressArray),
            () => Handler.GetMappablePhysicalAddressArray(MemoryUtils.ToPhysicalAddress(es, di)));
    }

    /// <summary>
    /// INT 67h AH=59h: writes expanded memory hardware information to ES:DI (AL selects); status in AH.
    /// </summary>
    public (byte ah, ushort dx, ushort bx) GetExpandedMemoryHardwareInformation(ushort expectedReturnCs, ushort expectedReturnIp, byte al, ushort di, ushort es) {
        (byte? ah, ushort? dx, ushort? bx) = CallDirect(expectedReturnCs, expectedReturnIp,
            nameof(GetExpandedMemoryHardwareInformation),
            () => Handler.ReadExpandedMemoryHardwareInformation(al, MemoryUtils.ToPhysicalAddress(es, di)));
        return (ah ?? (byte)EntryOf(nameof(GetExpandedMemoryHardwareInformation)).SelectorValue,
            dx ?? State.DX, bx ?? State.BX);
    }
}
