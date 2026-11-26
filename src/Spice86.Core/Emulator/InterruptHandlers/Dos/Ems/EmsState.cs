namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Collections.Immutable;

/// <summary>
///     Complete state of the EMS driver, as captured by <see cref="ExpandedMemoryManager.CaptureState" />.
/// </summary>
/// <remarks>
///     The driver maps the same page object in a handle, in the page frame and in the saved page map. The state
///     lists each page object once in <see cref="Pages" />, and the other members refer to a page by its index
///     in that list.
/// </remarks>
/// <param name="Pages">
///     Every page a handle, the page frame or the saved page map holds: first the pages of each handle in
///     <see cref="Handles" /> order, then the pages only the page frame or the saved page map holds.
/// </param>
/// <param name="Handles">The allocated handles, in the driver's order.</param>
/// <param name="PageFrame">For each physical page of the page frame, the index of the page mapped there.</param>
/// <param name="SavedPageMap">
///     For each physical page, the index of the page the last Save Page Map recorded. Empty when no page map is
///     saved.
/// </param>
public sealed record EmsState(
    ImmutableArray<EmmPageState> Pages,
    ImmutableArray<EmmHandleState> Handles,
    ImmutableArray<int> PageFrame,
    ImmutableArray<int> SavedPageMap);
