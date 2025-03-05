namespace Spice86.Converters;

using Avalonia.Collections;
using Avalonia.Data.Converters;

using Iced.Intel;

using Spice86.Models.Debugging;

using System.Globalization;
using System.Text;

public class InstructionToStringConverter : IValueConverter {
    private readonly StringBuilder _outputString = new();
    private readonly Formatter _formatter = new MasmFormatter(
        new FormatterOptions() {
            AddLeadingZeroToHexNumbers = true,
            ShowBranchSize = true,
            ShowSymbolAddress = true,
            AlwaysShowSegmentRegister = true,
            DisplacementLeadingZeros = true,
            HexPrefix = "0x",
            UppercasePrefixes = true,
            UppercaseKeywords = true,
            LeadingZeros = true,
            MasmDisplInBrackets = true,
            MasmSymbolDisplInBrackets = true,
            MasmAddDsPrefix32 = true,
            ScaleBeforeIndex = true,
            SmallHexNumbersInDecimal = true,
            UppercaseRegisters = true,
            BranchLeadingZeros = true });

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is CpuInstructionInfo cpuInstructionInfo) {
            Instruction instr = cpuInstructionInfo.Instruction;
            if(instr.IsInvalid) {
                return "(bad or emulator instruction 0xFE38)";
            }
            _outputString.Clear();
            var output = new StringOutput();
            if (!string.IsNullOrWhiteSpace(cpuInstructionInfo.FunctionName)) {
                _outputString.AppendLine($"{cpuInstructionInfo.FunctionName} entry point");
            }
            _formatter.Format(instr, output);
            string decodecInstructionString = output.ToStringAndReset();
            _outputString.AppendLine(decodecInstructionString);
            if (instr.GetOpKind(0) is OpKind.Memory or OpKind.MemorySegESI or OpKind.MemorySegEDI) {
                _outputString.AppendLine($"Memory segment: {instr.MemorySegment}");
            } else if (instr.IsCallFar || instr.IsCallNear || instr.IsCallNearIndirect) {
                _outputString.AppendLine($"Memory segment: {instr.MemorySegment}");
            }
                return _outputString.ToString();
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return null;
    }
}