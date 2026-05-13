namespace Spice86.Views.Converters;

using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Threading;

using Spice86.ViewModels.TextPresentation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>
/// Converts a list of <see cref="FormattedTextToken"/> to an <see cref="InlineCollection"/>.
/// Results are cached per list instance so that container recycling in the virtualized disassembly
/// list reuses the same <see cref="InlineCollection"/> objects instead of rebuilding them on every
/// scroll. The cache is invalidated automatically when the application theme changes.
/// </summary>
public class FormattedTextOffsetsConverter : IValueConverter {
    private sealed class CachedEntry {
        public CachedEntry(InlineCollection inlines, int version) {
            Inlines = inlines;
            Version = version;
        }

        public InlineCollection Inlines { get; }
        public int Version { get; }
    }

    // Keyed by List<FormattedTextToken> instance identity (ConditionalWeakTable uses reference equality).
    // Each DebuggerLineViewModel owns exactly one List instance for DisassemblyTextOffsets, so this
    // gives a per-instruction cache with correct lifetime (entry is collected when the VM is GC'd).
    private static readonly ConditionalWeakTable<List<FormattedTextToken>, CachedEntry> _cache = new();

    /// <summary>
    /// Converts a list of <see cref="FormattedTextToken"/> to an <see cref="InlineCollection"/>.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not List<FormattedTextToken> textOffsets) {
            return null;
        }

        if (!Dispatcher.UIThread.CheckAccess()) {
            return new InlineCollection();
        }

        int currentVersion = FormatterTextKindToBrushConverter.BrushCacheVersion;
        if (_cache.TryGetValue(textOffsets, out CachedEntry? cached) && cached.Version == currentVersion) {
            return cached.Inlines;
        }

        InlineCollection inlines = BuildInlines(textOffsets);
        _cache.AddOrUpdate(textOffsets, new CachedEntry(inlines, currentVersion));
        return inlines;
    }

    private static InlineCollection BuildInlines(List<FormattedTextToken> textOffsets) {
        InlineCollection inlines = new();
        foreach (FormattedTextToken textOffset in textOffsets) {
            inlines.Add(new Run {
                Text = textOffset.Text,
                Foreground = FormatterTextKindToBrushConverter.GetBrush(textOffset.Kind),
            });
        }
        return inlines;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}