namespace Spice86.ViewModels;

/// <summary>
/// An autocomplete suggestion for a breakpoint targeting a specific interrupt vector or
/// I/O port range. Used to populate the AutoCompleteBox in the breakpoint creation dialog.
/// </summary>
/// <param name="HexValue">The primary hex address string shown and inserted in the text box when the suggestion is selected (e.g. "0x21" or "0x388").</param>
/// <param name="Description">Human-readable label describing the interrupt or port range (e.g. "DOS Functions (AH=function)").</param>
public sealed record KnownBreakpointSuggestion(string HexValue, string Description);
