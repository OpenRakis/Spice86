namespace Spice86.Converters;

using Avalonia.Collections;
using Avalonia.Data.Converters;

using Iced.Intel;

using Spice86.Models.Debugging;

using System.Globalization;
using System.Text;

public class InstructionToStringConverter : IValueConverter {
    private readonly StringBuilder _outputString = new();
    private readonly Formatter _formatter = new MasmFormatter();
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
            // Don't use instr.ToString(), it allocates more, uses masm syntax and default options
            _formatter.Format(instr, output);
            _outputString.AppendLine(output.ToStringAndReset());
            return _outputString.ToString();
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return null;
    }
}