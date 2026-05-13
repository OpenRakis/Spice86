namespace Spice86.Views.Converters;

using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

using Iced.Intel;

using System.Collections.Generic;

/// <summary>
/// Converts FormatterTextKind to Brushes for syntax highlighting in the disassembly view.
/// </summary>
public static class FormatterTextKindToBrushConverter {
    // Resource key mapping for dynamic resources
    private static readonly Dictionary<FormatterTextKind, string> ResourceKeys = new() {
        {FormatterTextKind.Data, "DisassemblyDataBrush"},
        {FormatterTextKind.Decorator, "DisassemblyDecoratorBrush"},
        {FormatterTextKind.Directive, "DisassemblyDirectiveBrush"},
        {FormatterTextKind.Function, "DisassemblyFunctionBrush"},
        {FormatterTextKind.FunctionAddress, "DisassemblyFunctionAddressBrush"},
        {FormatterTextKind.Keyword, "DisassemblyKeywordBrush"},
        {FormatterTextKind.Label, "DisassemblyLabelBrush"},
        {FormatterTextKind.LabelAddress, "DisassemblyLabelAddressBrush"},
        {FormatterTextKind.Mnemonic, "DisassemblyMnemonicBrush"},
        {FormatterTextKind.Number, "DisassemblyNumberBrush"},
        {FormatterTextKind.Operator, "DisassemblyOperatorBrush"},
        {FormatterTextKind.Prefix, "DisassemblyPrefixBrush"},
        {FormatterTextKind.Punctuation, "DisassemblyPunctuationBrush"},
        {FormatterTextKind.Register, "DisassemblyRegisterBrush"},
        {FormatterTextKind.SelectorValue, "DisassemblySelectorValueBrush"},
        {FormatterTextKind.Text, "DisassemblyTextBrush"},
    };

    // Cache of resolved brushes to avoid per-Run dynamic resource subscriptions.
    // Invalidated when the theme changes.
    private static readonly Dictionary<FormatterTextKind, IBrush> BrushCache = new();
    private static ThemeVariant? _cachedTheme;

    /// <summary>
    /// Incremented each time the brush cache is cleared due to a theme change.
    /// Used by <see cref="FormattedTextOffsetsConverter"/> to invalidate its own cache.
    /// </summary>
    internal static int BrushCacheVersion { get; private set; }

    /// <summary>
    /// Gets a brush for the specified formatter text kind, resolved from application resources.
    /// The result is cached per theme variant to avoid repeated lookups.
    /// </summary>
    public static IBrush GetBrush(FormatterTextKind kind) {
        if (Application.Current is null) {
            return Brushes.White;
        }
        ThemeVariant currentTheme = Application.Current.ActualThemeVariant;
        if (currentTheme != _cachedTheme) {
            BrushCache.Clear();
            BrushCacheVersion++;
            _cachedTheme = currentTheme;
        }
        if (BrushCache.TryGetValue(kind, out IBrush? cached)) {
            return cached;
        }
        if (ResourceKeys.TryGetValue(kind, out string? resourceKey) &&
            Application.Current.TryGetResource(resourceKey, currentTheme, out object? resource) &&
            resource is IBrush brush) {
            BrushCache[kind] = brush;
            return brush;
        }
        return Brushes.White;
    }

    /// <summary>
    /// Gets a resource for the specified formatter text kind.
    /// </summary>
    /// <param name="kind">The formatter text kind.</param>
    /// <returns>A dynamic resource extension for the specified formatter text kind.</returns>
    public static DynamicResourceExtension GetDynamicResourceExtension(FormatterTextKind kind) {
        if (ResourceKeys.TryGetValue(kind, out string? resourceKey)) {
            return new DynamicResourceExtension(resourceKey);
        }

        throw new InvalidOperationException($"Unknown FormatterTextKind {kind}");
    }
}