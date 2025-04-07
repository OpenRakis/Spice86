namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

// ADC
[OpAccImm("Adc", "AL", 8, "byte")]
public partial class AdcAccImm8;

[OpAccImm("Adc", "AX",16, "ushort")]
public partial class AdcAccImm16;

[OpAccImm("Adc", "EAX",32, "uint")]
public partial class AdcAccImm32;

// ADD
[OpAccImm("Add", "AL", 8, "byte")]
public partial class AddAccImm8;

[OpAccImm("Add", "AX",16, "ushort")]
public partial class AddAccImm16;

[OpAccImm("Add", "EAX",32, "uint")]
public partial class AddAccImm32;

// AND
[OpAccImm("And", "AL", 8, "byte")]
public partial class AndAccImm8;

[OpAccImm("And", "AX",16, "ushort")]
public partial class AndAccImm16;

[OpAccImm("And", "EAX",32, "uint")]
public partial class AndAccImm32;

// OR
[OpAccImm("Or", "AL", 8, "byte")]
public partial class OrAccImm8;

[OpAccImm("Or", "AX",16, "ushort")]
public partial class OrAccImm16;

[OpAccImm("Or", "EAX",32, "uint")]
public partial class OrAccImm32;

// SBB
[OpAccImm("Sbb", "AL", 8, "byte")]
public partial class SbbAccImm8;

[OpAccImm("Sbb", "AX",16, "ushort")]
public partial class SbbAccImm16;

[OpAccImm("Sbb", "EAX",32, "uint")]
public partial class SbbAccImm32;

// SUB
[OpAccImm("Sub", "AL", 8, "byte")]
public partial class SubAccImm8;

[OpAccImm("Sub", "AX",16, "ushort")]
public partial class SubAccImm16;

[OpAccImm("Sub", "EAX",32, "uint")]
public partial class SubAccImm32;

// XOR
[OpAccImm("Xor", "AL", 8, "byte")]
public partial class XorAccImm8;

[OpAccImm("Xor", "AX",16, "ushort")]
public partial class XorAccImm16;

[OpAccImm("Xor", "EAX",32, "uint")]
public partial class XorAccImm32;

// CMP (Sub without assigment)
[OpAccImm("Sub", "AL", 8, "byte", false, "cmp")]
public partial class CmpAccImm8;

[OpAccImm("Sub", "AX", 16, "ushort", false, "cmp")]
public partial class CmpAccImm16;

[OpAccImm("Sub", "EAX", 32, "uint", false, "cmp")]
public partial class CmpAccImm32;


// Test (Sub without assigment)
[OpAccImm("And", "AL", 8, "byte", false, "test")]
public partial class TestAccImm8;

[OpAccImm("And", "AX", 16, "ushort", false, "test")]
public partial class TestAccImm16;

[OpAccImm("And", "EAX", 32, "uint", false, "test")]
public partial class TestAccImm32;