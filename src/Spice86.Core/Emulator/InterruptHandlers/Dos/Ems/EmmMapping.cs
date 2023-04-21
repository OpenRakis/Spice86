namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// The link between an EMM handle and one or more logical page.
/// </summary>
public class EmmMapping {
    /// <summary>
    /// The EMM handle, allocated to the DOS program.
    /// </summary>
    public EmmHandle Handle { get; set; } = new();

    /// <summary>
    /// The logical page behind the handle.
    /// </summary>
    public IEnumerable<EmmPage> LogicalPage { get; set; } = new List<EmmPage>();
}