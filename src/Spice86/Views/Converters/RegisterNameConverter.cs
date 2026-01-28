using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Spice86.Views.Converters;

public class RegisterNameConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not string name) {
            return value;
        }

        string? param = parameter as string;

        return (name, param) switch {
            ("EAX", "16") => "AX",
            ("EAX", "H") => "AH",
            ("EAX", "L") => "AL",

            ("EBX", "16") => "BX",
            ("EBX", "H") => "BH",
            ("EBX", "L") => "BL",

            ("ECX", "16") => "CX",
            ("ECX", "H") => "CH",
            ("ECX", "L") => "CL",

            ("EDX", "16") => "DX",
            ("EDX", "H") => "DH",
            ("EDX", "L") => "DL",

            ("ESI", "16") => "SI",
            ("EDI", "16") => "DI",
            ("EBP", "16") => "BP",
            ("ESP", "16") => "SP",
            ("EIP", "16") => "IP",

            _ => name
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}
