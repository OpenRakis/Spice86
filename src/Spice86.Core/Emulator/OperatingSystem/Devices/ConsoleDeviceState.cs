namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using System.Collections.Immutable;

/// <summary>
///     State of the CON device, as captured by <see cref="ConsoleDevice.CaptureState" />: its ANSI escape
///     sequence parser and the input it holds before the BIOS keyboard buffer.
/// </summary>
/// <param name="Attribute">Text attribute the ANSI parser writes with.</param>
/// <param name="SavedColumn">Cursor column saved by ESC[s, or -1.</param>
/// <param name="SavedRow">Cursor row saved by ESC[s, or -1.</param>
/// <param name="Phase">Phase of the escape sequence parser.</param>
/// <param name="Parameters">Parameter bytes collected for the current sequence.</param>
/// <param name="ParameterCount">Index of the parameter slot being built.</param>
/// <param name="PrefixAllowed">Whether the '=' and '?' prefixes are still allowed.</param>
/// <param name="EatNextSemicolon">Whether the next semicolon is skipped.</param>
/// <param name="WrapFlag">Whether output wraps at the end of a line.</param>
/// <param name="StringTerminator">Character that ends the current quoted string.</param>
/// <param name="KeyRedefinitions">Key reassignments made with ESC[p, in key code order.</param>
/// <param name="StuffAheadBuffer">Bytes waiting to be read before the keyboard buffer, oldest first.</param>
/// <param name="ExtendedKeyScanCode">Scan code of an extended key kept for the next read, or 0.</param>
public sealed record ConsoleDeviceState(
    byte Attribute,
    int SavedColumn,
    int SavedRow,
    AnsiParserPhase Phase,
    ReadOnlyMemory<byte> Parameters,
    int ParameterCount,
    bool PrefixAllowed,
    bool EatNextSemicolon,
    bool WrapFlag,
    byte StringTerminator,
    ImmutableArray<AnsiKeyRedefinition> KeyRedefinitions,
    ReadOnlyMemory<byte> StuffAheadBuffer,
    byte ExtendedKeyScanCode);
