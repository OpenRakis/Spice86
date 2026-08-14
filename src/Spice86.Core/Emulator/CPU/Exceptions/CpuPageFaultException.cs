namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// A Page Fault (#PF, vector 14) is raised by the paging unit when a linear-to-physical translation
/// fails: the page directory or page table entry is not present, or the access violates the
/// entry's combined User/Supervisor or Read/Write protection. The faulting linear address is recorded
/// in CR2 by the paging unit before this exception is thrown.
/// </summary>
/// <remarks>
/// Error code bit layout (pushed on the stack like any other exception with an error code):
/// bit 0 (P) is 0 for a not-present page, 1 for a protection violation on a present page; bit 1 (W/R)
/// is 1 when the fault was caused by a write; bit 2 (U/S) is 1 when the fault occurred at CPL 3.
/// </remarks>
public class CpuPageFaultException : CpuException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="errorCode">The page-fault error code (P/W/U bits).</param>
    public CpuPageFaultException(string message, ushort errorCode)
        : base(message, 0x0E, CpuExceptionType.Fault, "#PF", errorCode) {
    }
}
