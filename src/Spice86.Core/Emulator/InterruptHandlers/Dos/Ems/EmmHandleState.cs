namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Collections.Immutable;

/// <summary>
///     State of one EMS handle, part of <see cref="EmsState" />.
/// </summary>
/// <param name="HandleNumber">Value of <see cref="EmmHandle.HandleNumber" />.</param>
/// <param name="Name">Value of <see cref="EmmHandle.Name" />.</param>
/// <param name="SavedPageMap">Value of <see cref="EmmHandle.SavedPageMap" />.</param>
/// <param name="LogicalPages">For each logical page of the handle, its index in <see cref="EmsState.Pages" />.</param>
public sealed record EmmHandleState(ushort HandleNumber, string Name, bool SavedPageMap, ImmutableArray<int> LogicalPages);
