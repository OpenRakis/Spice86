namespace Spice86.Converters;

using Avalonia.Collections;
using Avalonia.Data.Converters;

using Iced.Intel;

using Spice86.Models.Debugging;

using System.Globalization;
using System.Text;

public class InstructionToStringConverter : IValueConverter {
    private StringBuilder _outputString = new();
    private Formatter _formatter = new MasmFormatter();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is CpuInstructionInfo cpuInstructionInfo) {
            Instruction instr = cpuInstructionInfo.Instruction;
            _outputString.Clear();
            _formatter.Options.DigitSeparator = "`";
            _formatter.Options.FirstOperandCharIndex = 10;
            var output = new StringOutput();
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