namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

// ADC
[OpRegRm("Adc", 8)]
public partial class AdcRegRm8;

[OpRegRm("Adc", 16)]
public partial class AdcRegRm16;

[OpRegRm("Adc", 32)]
public partial class AdcRegRm32;

// ADD
[OpRegRm("Add", 8)]
public partial class AddRegRm8;

[OpRegRm("Add", 16)]
public partial class AddRegRm16;

[OpRegRm("Add", 32)]
public partial class AddRegRm32;

// AND
[OpRegRm("And", 8)]
public partial class AndRegRm8;

[OpRegRm("And", 16)]
public partial class AndRegRm16;

[OpRegRm("And", 32)]
public partial class AndRegRm32;

// CMP (Sub without assigment)
[OpRegRm("Sub", 8, false)]
public partial class CmpRegRm8;

[OpRegRm("Sub", 16, false)]
public partial class CmpRegRm16;

[OpRegRm("Sub", 32, false)]
public partial class CmpRegRm32;

// OR
[OpRegRm("Or", 8)]
public partial class OrRegRm8;

[OpRegRm("Or", 16)]
public partial class OrRegRm16;

[OpRegRm("Or", 32)]
public partial class OrRegRm32;

// SBB
[OpRegRm("Sbb", 8)]
public partial class SbbRegRm8;

[OpRegRm("Sbb", 16)]
public partial class SbbRegRm16;

[OpRegRm("Sbb", 32)]
public partial class SbbRegRm32;

// SUB
[OpRegRm("Sub", 8)]
public partial class SubRegRm8;

[OpRegRm("Sub", 16)]
public partial class SubRegRm16;

[OpRegRm("Sub", 32)]
public partial class SubRegRm32;

// XOR
[OpRegRm("Xor", 8)]
public partial class XorRegRm8;

[OpRegRm("Xor", 16)]
public partial class XorRegRm16;

[OpRegRm("Xor", 32)]
public partial class XorRegRm32;