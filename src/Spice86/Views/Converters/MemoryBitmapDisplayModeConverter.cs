namespace Spice86.Views.Converters;

using Avalonia.Data.Converters;

using Spice86.ViewModels;

/// <summary>
/// Provides converters for the <see cref="MemoryBitmapDisplayMode"/> enum.
/// </summary>
public static class MemoryBitmapDisplayModeConverter {
    /// <summary>
    /// Converts a <see cref="MemoryBitmapDisplayMode"/> value to a human-readable display name.
    /// </summary>
    public static readonly IValueConverter ToDisplayName = new FuncValueConverter<MemoryBitmapDisplayMode, string>(mode => mode switch {
        MemoryBitmapDisplayMode.Vga8Bpp => "VGA 8BPP (256 colors)",
        MemoryBitmapDisplayMode.Cga4Color => "CGA 4 colors",
        MemoryBitmapDisplayMode.Ega16Color => "EGA 16 colors",
        MemoryBitmapDisplayMode.TextMode => "Text Mode (IBM fonts)",
        MemoryBitmapDisplayMode.HerculesMonochrome => "Hercules Monochrome",
        _ => mode.ToString()
    });
}
