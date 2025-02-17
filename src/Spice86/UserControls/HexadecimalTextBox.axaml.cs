using Avalonia;
using Avalonia.Controls;

using Spice86.UserControls;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Spice86.UserControls;

public partial class HexadecimalTextBox : UserControl {
    public HexadecimalTextBox() {
        InitializeComponent();
        this.HexTextBox.TextChanged += OnTextChanged;
    }


    private void OnTextChanged(object? sender, TextChangedEventArgs e) {
        OnTextChanged(HexTextBox.Text);
    }

    private bool TryParseHexValue(string? hexpression,
        [NotNullWhen(true)] out uint? hexadecimalValue) {
        if (string.IsNullOrWhiteSpace(hexpression)) {
            hexadecimalValue = null;
            return false;
        }

        try {
            if(ulong.TryParse(hexpression.StartsWith("0x") ?
                hexpression[2..] : hexpression, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out ulong hexValue)) {
                hexadecimalValue = (uint)hexValue;
                return true;
            }
        } catch { }
        hexadecimalValue = null;
        return false;
    }


    public static readonly StyledProperty<uint?> ParsedHexadecimalNumberProperty =
        AvaloniaProperty.Register<AddressAutoCompleteBox, uint?>(nameof(ParsedHexadecimalNumber),
            coerce: CoerceParsedHexadecimalNumber);

    private static uint? CoerceParsedHexadecimalNumber(AvaloniaObject @object, uint? nullable) {
        if (@object is HexadecimalTextBox instance) {
            if (nullable.HasValue) {
                instance.HexTextBox.Text = nullable.Value.ToString(
                    CultureInfo.InvariantCulture);
            } else {
                instance.HexTextBox.Text = null;
            }
        }
        return nullable;
    }

    public uint? ParsedHexadecimalNumber {
        get => GetValue(ParsedHexadecimalNumberProperty);
        set => SetValue(ParsedHexadecimalNumberProperty, value);
    }

    private void OnTextChanged(string? address) {
        if (string.IsNullOrWhiteSpace(address)) {
            return;
        }
        if (TryParseHexValue(address, out var parsedAddress)) {
            ParsedHexadecimalNumber = parsedAddress;
        } else {
            ParsedHexadecimalNumber = null;
        }
    }
}