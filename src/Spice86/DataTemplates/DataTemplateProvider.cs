namespace Spice86.DataTemplates;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using CommunityToolkit.Mvvm.Input;

using Structurizer.Types;

using System.Text;

public static class DataTemplateProvider {
    public static FuncDataTemplate<StructureMember> StructureMemberValueTemplate { get; } = new(BuildStructureMemberValuePresenter);

    private static Control? BuildStructureMemberValuePresenter(StructureMember? structureMember, INameScope scope) {
        if (structureMember is null) {
            return null;
        }
        if (structureMember.Type is {IsPointer: true, IsArray: false}) {
            return new Button {
                Content = FormatPointer(structureMember),
                Command = new RelayCommand(() => throw new NotImplementedException("This should open a new memory view at the address the pointer points to")),
                Classes = {"hyperlink"},
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0,0,5,0)
            };
        }

        return new TextBlock {
            Text = FormatValue(structureMember),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0,0,5,0)
        };
    }

    private static string FormatValue(StructureMember structureMember) {
        if (structureMember.Members != null && structureMember.Type.Type != "char") {
            return string.Empty;
        }
        if (structureMember.Type.EnumType != null) {
            return FormatEnum(structureMember.Type.EnumType, structureMember.Data);
        }
        if (structureMember.Type is {IsPointer: true, Count: 1}) {
            return FormatPointer(structureMember);
        }

        return structureMember.Type.Type switch {
            "char" when structureMember.Type.IsArray => '"' + Encoding.ASCII.GetString(structureMember.Data) + '"',
            "__int8" or "char" or "_BYTE" => FormatChar(structureMember.Data[0]),
            "__int16" or "short" or "int" when structureMember.Type.Unsigned => FormatUnsignedShort(structureMember),
            "__int16" or "short" or "int" => FormatShort(structureMember),
            "__int32" or "long" when structureMember.Type.Unsigned => FormatUnsignedLong(structureMember),
            "__int32" or "long" => FormatLong(structureMember),
            _ => FormatHex(structureMember)
        };
    }

    private static string FormatHex(StructureMember structureMember) {
        switch (structureMember.Size) {
            case 1:
                return $"0x{structureMember.Data[0]:X2}";
            case 2:
                return $"0x{BitConverter.ToUInt16(structureMember.Data):X4}";
            case 4:
                return $"0x{BitConverter.ToUInt32(structureMember.Data):X8}";
            default:
                return "???";
        }
    }

    private static string FormatPointer(StructureMember structureMember) {
        Span<byte> bytes = structureMember.Data.AsSpan();
        ushort targetSegment;
        ushort targetOffset;
        if (structureMember.Type.IsNear && bytes.Length == 2) {
            targetSegment = 0;
            targetOffset = BitConverter.ToUInt16(bytes);
        } else if (bytes.Length == 4) {
            targetSegment = BitConverter.ToUInt16(bytes[2..]);
            targetOffset = BitConverter.ToUInt16(bytes[..2]);
        } else {
            throw new ArgumentException($"Invalid pointer size: {bytes.Length * 8}");
        }

        return structureMember.Type.IsNear
            ? $"DS:{targetOffset:X4}"
            : $"{targetSegment:X4}:{targetOffset:X4}";
    }

    private static string FormatEnum(EnumType enumType, byte[] bytes) {
        uint value = enumType.MemberSize switch {
            1 => bytes[0],
            2 => BitConverter.ToUInt16(bytes),
            4 => BitConverter.ToUInt32(bytes),
            _ => throw new NotSupportedException($"Enum member size {enumType.MemberSize} not supported")
        };

        if (enumType.Members.TryGetValue(value, out string? name)) {
            return $"{name} [0x{value:X}]";
        }

        throw new ArgumentOutOfRangeException(nameof(bytes), $"Enum value {value} not found in enum");
    }

    private static string FormatLong(StructureMember structureMember) {
        int value = BitConverter.ToInt32(structureMember.Data);

        return $"{value} [0x{value:X8}]";
    }

    private static string FormatUnsignedLong(StructureMember structureMember) {
        uint value = BitConverter.ToUInt32(structureMember.Data);

        return $"{value} [0x{value:X8}]";
    }

    private static string FormatShort(StructureMember structureMember) {
        short value = BitConverter.ToInt16(structureMember.Data);

        return $"{value} [0x{value:X4}]";
    }

    private static string FormatUnsignedShort(StructureMember structureMember) {
        ushort value = BitConverter.ToUInt16(structureMember.Data);

        return $"{value} [0x{value:X4}]";
    }

    private static string FormatChar(byte value) {
        return value is < 32 or > 126
            ? $"{value} [0x{value:X2}]"
            : $"{value} '{(char)value}' [0x{value:X2}]";
    }
}