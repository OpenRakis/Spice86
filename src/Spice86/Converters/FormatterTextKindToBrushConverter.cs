namespace Spice86.Converters;

using Avalonia.Markup.Xaml.MarkupExtensions;

using Iced.Intel;

using System.Collections.Generic;

/// <summary>
/// Converts FormatterTextKind to Brushes for syntax highlighting in the disassembly view.
/// </summary>
public static class FormatterTextKindToBrushConverter {
    // Resource key mapping for dynamic resources
    private static readonly Dictionary<FormatterTextKind, string> ResourceKeys = new() {
        {FormatterTextKind.Directive, "DisassemblyDirectiveBrush"},
        {FormatterTextKind.Keyword, "DisassemblyKeywordBrush"},
        {FormatterTextKind.LabelAddress, "DisassemblyLabelAddressBrush"},
        {FormatterTextKind.FunctionAddress, "DisassemblyFunctionAddressBrush"},
        {FormatterTextKind.Label, "DisassemblyLabelBrush"},
        {FormatterTextKind.Function, "DisassemblyFunctionBrush"},
        {FormatterTextKind.Mnemonic, "DisassemblyMnemonicBrush"},
        {FormatterTextKind.Number, "DisassemblyNumberBrush"},
        {FormatterTextKind.Operator, "DisassemblyOperatorBrush"},
        {FormatterTextKind.Prefix, "DisassemblyPrefixBrush"},
        {FormatterTextKind.Punctuation, "DisassemblyPunctuationBrush"},
        {FormatterTextKind.Register, "DisassemblyRegisterBrush"},
        {FormatterTextKind.Text, "DisassemblyTextBrush"}
    };

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