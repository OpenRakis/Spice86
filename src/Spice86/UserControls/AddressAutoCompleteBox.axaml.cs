using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Spice86.UserControls;

public partial class AddressAutoCompleteBox : UserControl {
    public AddressAutoCompleteBox() {
        InitializeComponent();
        AddressesSuggestions = new();
        this.AddressTextCompleteBox.KeyUp += OnKeyUp;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) {
        if(e.Key == Key.Enter) {
            OnTextChanged(AddressTextCompleteBox.Text);
        }
    }

    private bool TryParseMemoryAddress(string? addressExpression,
        [NotNullWhen(true)] out uint? address) {
        if (string.IsNullOrWhiteSpace(addressExpression)) {
            address = null;
            return false;
        }

        try {
            if (addressExpression.Contains(':')) {
                string[] split = addressExpression.Split(":");
                string firstPart = split[0];
                string secondPart = split[1];

                if(State is not null) {
                    firstPart = ReplaceRegisterNameWithValue(firstPart, State);
                    secondPart = ReplaceRegisterNameWithValue(secondPart, State);
                }

                if (split.Length > 1 &&
                    ushort.TryParse(firstPart, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort hexSegment) &&
                    ushort.TryParse(secondPart, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort hexOffset)) {
                    address = Math.Min(A20Gate.EndOfHighMemoryArea, MemoryUtils.
                        ToPhysicalAddress(hexSegment, hexOffset));
                    return true;
                }

                if (split.Length > 1 &&
                    ushort.TryParse(firstPart, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out ushort segment) &&
                    ushort.TryParse(secondPart, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out ushort offset)) {
                    address = Math.Min(A20Gate.EndOfHighMemoryArea, MemoryUtils.
                        ToPhysicalAddress(segment, offset));
                    return true;
                }
            } else if (ulong.TryParse(addressExpression,
                      CultureInfo.InvariantCulture, out ulong value)) {
                address = (uint)value;
                return true;
            } else if (ulong.TryParse(addressExpression.AsSpan(
                  addressExpression.StartsWith("0x") ? 2 : 0),
                  NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                      out ulong hexValue)) {
                address = (uint)hexValue;
                return true;
            }
        } catch { }
        address = null;
        return false;
    }

    private string ReplaceRegisterNameWithValue(string part, State state) {
        return part.ToUpperInvariant() switch {
            "CS" => state.CS.ToString("X4"),
            "IP" => state.IP.ToString("X4"),
            "DS" => state.DS.ToString("X4"),
            "ES" => state.ES.ToString("X4"),
            "FS" => state.FS.ToString("X4"),
            "GS" => state.GS.ToString("X4"),
            "SS" => state.SS.ToString("X4"),
            _ => part
        };
    }

    public static StyledProperty<State?> StateProperty =
        AvaloniaProperty.Register<AddressAutoCompleteBox, State?>(nameof(State));

    public State? State {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly StyledProperty<AvaloniaList<string>> AddressesSuggestionsProperty =
        AvaloniaProperty.Register<AddressAutoCompleteBox, AvaloniaList<string>>(nameof(AddressesSuggestions));

    public AvaloniaList<string> AddressesSuggestions {
        get => GetValue(AddressesSuggestionsProperty);
        set => SetValue(AddressesSuggestionsProperty, value);
    }

    public static readonly StyledProperty<uint?> ParsedAddressProperty =
        AvaloniaProperty.Register<AddressAutoCompleteBox, uint?>(nameof(ParsedAddressProperty),
            coerce: CoerceParseAddress);

    private static uint? CoerceParseAddress(AvaloniaObject @object, uint? nullable) {
        if(@object is AddressAutoCompleteBox instance) {
            if (nullable.HasValue) {
                instance.AddressTextCompleteBox.Text = nullable.Value.ToString(
                    CultureInfo.InvariantCulture);
            } else {
                instance.AddressTextCompleteBox.Text = null;
            }
        }
        return nullable;
    }

    public uint? ParsedAddress {
        get => GetValue(ParsedAddressProperty);
        set => SetValue(ParsedAddressProperty, value);
    }

    private void OnTextChanged(string? address) {
        if (string.IsNullOrWhiteSpace(address)) {
            return;
        }
        if (TryParseMemoryAddress(address, out var parsedAddress)) {
            ParsedAddress = parsedAddress;
            if (!AddressesSuggestions.Contains(address)) {
                AddressesSuggestions.Add(address);
            }
            this.AddressTextCompleteBox.SelectedItem = address;
        } else {
            ParsedAddress = null;
        }
    }
}
