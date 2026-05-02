namespace Spice86.DebuggerKnowledgeBase.Xms;

using System.Collections.Generic;

/// <summary>
/// Static name/description tables for the eXtended Memory Specification (XMS) function set.
/// Mirrors the dispatch table in <c>ExtendedMemoryManager.RunMultiplex</c> and the XMS 3.0
/// specification, including the 32-bit "Any" extensions (88h, 89h, 8Eh, 8Fh).
/// </summary>
internal static class XmsDecodingTables {
    /// <summary>XMS sub-functions, keyed by AH.</summary>
    public static readonly IReadOnlyDictionary<byte, XmsFunctionEntry> ByAh = new Dictionary<byte, XmsFunctionEntry> {
        [0x00] = new XmsFunctionEntry("Get XMS Version Number", "Return the XMS version (BCD) in AX, driver revision in BX, and HMA-exists flag in DX."),
        [0x01] = new XmsFunctionEntry("Request High Memory Area", "Request the HMA for the caller; DX = bytes needed (FFFFh = entire HMA, TSRs / device drivers)."),
        [0x02] = new XmsFunctionEntry("Release High Memory Area", "Release the HMA back to the XMS manager."),
        [0x03] = new XmsFunctionEntry("Global Enable A20", "Enable the A20 line for an HMA-using program."),
        [0x04] = new XmsFunctionEntry("Global Disable A20", "Disable the A20 line."),
        [0x05] = new XmsFunctionEntry("Local Enable A20", "Enable A20 directly (bypasses the HMA reference count)."),
        [0x06] = new XmsFunctionEntry("Local Disable A20", "Disable A20 directly."),
        [0x07] = new XmsFunctionEntry("Query A20 State", "Return A20 state in AX (0001h = enabled, 0000h = disabled)."),
        [0x08] = new XmsFunctionEntry("Query Free Extended Memory", "Return the largest free EMB in AX (KB) and total free EMBs in DX (KB)."),
        [0x09] = new XmsFunctionEntry("Allocate Extended Memory Block", "Allocate DX KB of extended memory; return the new handle in DX."),
        [0x0A] = new XmsFunctionEntry("Free Extended Memory Block", "Free the EMB referenced by handle DX."),
        [0x0B] = new XmsFunctionEntry("Move Extended Memory Block", "Copy a memory block; DS:SI points at an ExtendedMemoryMoveStructure (length / src handle:offset / dst handle:offset)."),
        [0x0C] = new XmsFunctionEntry("Lock Extended Memory Block", "Lock handle DX in physical memory and return the 32-bit linear address in DX:BX."),
        [0x0D] = new XmsFunctionEntry("Unlock Extended Memory Block", "Unlock handle DX (decrement its lock count)."),
        [0x0E] = new XmsFunctionEntry("Get EMB Handle Information", "Return the lock count in BH, free handles in BL, and block size in DX (KB) for handle DX."),
        [0x0F] = new XmsFunctionEntry("Reallocate Extended Memory Block", "Resize handle DX so it owns BX KB."),
        [0x10] = new XmsFunctionEntry("Request Upper Memory Block", "Request a UMB of DX paragraphs; return the segment in BX. (Not supported by Spice86's manager.)"),
        [0x11] = new XmsFunctionEntry("Release Upper Memory Block", "Release the UMB at segment DX. (Not supported by Spice86's manager.)"),
        [0x12] = new XmsFunctionEntry("Reallocate Upper Memory Block", "Resize the UMB at segment DX to BX paragraphs. (Not supported by Spice86's manager.)"),
        [0x88] = new XmsFunctionEntry("Query Any Free Extended Memory (32-bit)", "XMS 3.0: return largest/total free EMB in EAX/EDX (KB) and the highest extended-memory address in ECX."),
        [0x89] = new XmsFunctionEntry("Allocate Any Extended Memory (32-bit)", "XMS 3.0: allocate EDX KB of extended memory; return the new handle in DX."),
        [0x8E] = new XmsFunctionEntry("Get Extended EMB Handle Information (32-bit)", "XMS 3.0: return the lock count in EAX, free handles in CX, block size in EDX (KB) for handle DX."),
        [0x8F] = new XmsFunctionEntry("Reallocate Any Extended Memory (32-bit)", "XMS 3.0: resize handle DX so it owns EBX KB.")
    };

    /// <summary>Standard XMS error / status codes returned in BL.</summary>
    public static readonly IReadOnlyDictionary<byte, string> ErrorCodes = new Dictionary<byte, string> {
        [0x80] = "Function not implemented",
        [0x81] = "VDISK detected",
        [0x82] = "A20 error",
        [0x8E] = "General driver error",
        [0x8F] = "Unrecoverable driver error",
        [0x90] = "HMA does not exist",
        [0x91] = "HMA already in use",
        [0x92] = "DX is less than the /HMAMIN= parameter",
        [0x93] = "HMA not allocated",
        [0x94] = "A20 still enabled",
        [0xA0] = "All extended memory is allocated",
        [0xA1] = "All available extended memory handles are in use",
        [0xA2] = "Invalid handle",
        [0xA3] = "Invalid source handle",
        [0xA4] = "Invalid source offset",
        [0xA5] = "Invalid destination handle",
        [0xA6] = "Invalid destination offset",
        [0xA7] = "Invalid length",
        [0xA8] = "Invalid overlap in move request",
        [0xA9] = "Parity error",
        [0xAA] = "Block is not locked",
        [0xAB] = "Block is locked",
        [0xAC] = "Block lock count overflow",
        [0xAD] = "Lock failed",
        [0xB0] = "Smaller UMB is available",
        [0xB1] = "No UMBs are available",
        [0xB2] = "UMB segment number is invalid"
    };
}
