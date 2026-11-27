namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
///     State of one EMS page, part of <see cref="EmsState" />.
/// </summary>
/// <param name="PageNumber">Value of <see cref="EmmPage.PageNumber" />.</param>
/// <param name="Content">The bytes of the page up to its last non-zero byte. The rest of the page is zero.</param>
public sealed record EmmPageState(ushort PageNumber, ReadOnlyMemory<byte> Content);
