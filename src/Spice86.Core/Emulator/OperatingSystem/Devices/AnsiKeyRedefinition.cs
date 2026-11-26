namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
///     A key reassignment of the ANSI driver (ESC[p), part of <see cref="ConsoleDeviceState" />.
/// </summary>
/// <param name="KeyCode">ASCII code of the key, or its scan code shifted left by 8 for an extended key.</param>
/// <param name="Replacement">Bytes the key produces.</param>
public readonly record struct AnsiKeyRedefinition(ushort KeyCode, ReadOnlyMemory<byte> Replacement);
