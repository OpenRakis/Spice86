namespace Spice86.Core.CLI;

using System.ComponentModel;
using System.Globalization;

/// <summary>Accepts DOSBox's bare hexadecimal GUS addresses as well as standard numeric port values.</summary>
public sealed class GusBasePortConverter : UInt16Converter {
    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        if (value is string text && text is "210" or "220" or "230" or "240" or "250" or "260") {
            return ushort.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
        return base.ConvertFrom(context, culture, value);
    }
}
