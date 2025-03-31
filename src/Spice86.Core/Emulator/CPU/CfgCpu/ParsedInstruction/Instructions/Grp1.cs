namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

// ADC
[Grp1("Adc", 8, "byte", Mnemonic: "ADC")]
public partial class Grp1Adc8;

[Grp1("Adc", 16, "sbyte", "(ushort)", Mnemonic: "ADC")]
public partial class Grp1AdcSigned16;

[Grp1("Adc", 32, "sbyte", "(uint)", Mnemonic: "ADC")]
public partial class Grp1AdcSigned32;

[Grp1("Adc", 16, "ushort", Mnemonic: "ADC")]
public partial class Grp1AdcUnsigned16;

[Grp1("Adc", 32, "uint", Mnemonic: "ADC")]
public partial class Grp1AdcUnsigned32;

// ADD
[Grp1("Add", 8, "byte", Mnemonic: "ADD")]
public partial class Grp1Add8;

[Grp1("Add", 16, "sbyte", "(ushort)", Mnemonic: "ADD")]
public partial class Grp1AddSigned16;

[Grp1("Add", 32, "sbyte", "(uint)", Mnemonic: "ADD")]
public partial class Grp1AddSigned32;

[Grp1("Add", 16, "ushort", Mnemonic: "ADD")]
public partial class Grp1AddUnsigned16;

[Grp1("Add", 32, "uint", Mnemonic: "ADD")]
public partial class Grp1AddUnsigned32;

// AND
[Grp1("And", 8, "byte", Mnemonic: "AND")]
public partial class Grp1And8;

[Grp1("And", 16, "sbyte", "(ushort)", Mnemonic: "AND")]
public partial class Grp1AndSigned16;

[Grp1("And", 32, "sbyte", "(uint)", Mnemonic: "AND")]
public partial class Grp1AndSigned32;

[Grp1("And", 16, "ushort", Mnemonic: "AND")]
public partial class Grp1AndUnsigned16;

[Grp1("And", 32, "uint", Mnemonic: "AND")]
public partial class Grp1AndUnsigned32;

// CMP (Sub without assigment)
[Grp1("Sub", 8, "byte", "", false, Mnemonic: "CMP")]
public partial class Grp1Cmp8;

[Grp1("Sub", 16, "sbyte", "(ushort)", false, Mnemonic: "CMP")]
public partial class Grp1CmpSigned16;

[Grp1("Sub", 32, "sbyte", "(uint)", false, Mnemonic: "CMP")]
public partial class Grp1CmpSigned32;

[Grp1("Sub", 16, "ushort", "", false, Mnemonic: "CMP")]
public partial class Grp1CmpUnsigned16;

[Grp1("Sub", 32, "uint", "", false, Mnemonic: "CMP")]
public partial class Grp1CmpUnsigned32;

// OR
[Grp1("Or", 8, "byte", Mnemonic: "OR")]
public partial class Grp1Or8;

[Grp1("Or", 16, "sbyte", "(ushort)", Mnemonic: "OR")]
public partial class Grp1OrSigned16;

[Grp1("Or", 32, "sbyte", "(uint)", Mnemonic: "OR")]
public partial class Grp1OrSigned32;

[Grp1("Or", 16, "ushort", Mnemonic: "OR")]
public partial class Grp1OrUnsigned16;

[Grp1("Or", 32, "uint", Mnemonic: "OR")]
public partial class Grp1OrUnsigned32;

// SBB
[Grp1("Sbb", 8, "byte", Mnemonic: "SBB")]
public partial class Grp1Sbb8;

[Grp1("Sbb", 16, "sbyte", "(ushort)", Mnemonic: "SBB")]
public partial class Grp1SbbSigned16;

[Grp1("Sbb", 32, "sbyte", "(uint)", Mnemonic: "SBB")]
public partial class Grp1SbbSigned32;

[Grp1("Sbb", 16, "ushort", Mnemonic: "SBB")]
public partial class Grp1SbbUnsigned16;

[Grp1("Sbb", 32, "uint", Mnemonic: "SBB")]
public partial class Grp1SbbUnsigned32;

// SUB
[Grp1("Sub", 8, "byte", Mnemonic: "SUB")]
public partial class Grp1Sub8;

[Grp1("Sub", 16, "sbyte", "(ushort)", Mnemonic: "SUB")]
public partial class Grp1SubSigned16;

[Grp1("Sub", 32, "sbyte", "(uint)", Mnemonic: "SUB")]
public partial class Grp1SubSigned32;

[Grp1("Sub", 16, "ushort", Mnemonic: "SUB")]
public partial class Grp1SubUnsigned16;

[Grp1("Sub", 32, "uint", Mnemonic: "SUB")]
public partial class Grp1SubUnsigned32;

// XOR
[Grp1("Xor", 8, "byte", Mnemonic: "XOR")]
public partial class Grp1Xor8;

[Grp1("Xor", 16, "sbyte", "(ushort)", Mnemonic: "XOR")]
public partial class Grp1XorSigned16;

[Grp1("Xor", 32, "sbyte", "(uint)", Mnemonic: "XOR")]
public partial class Grp1XorSigned32;

[Grp1("Xor", 16, "ushort", Mnemonic: "XOR")]
public partial class Grp1XorUnsigned16;

[Grp1("Xor", 32, "uint", Mnemonic: "XOR")]
public partial class Grp1XorUnsigned32;