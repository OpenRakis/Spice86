namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

// ADC
[OpRmReg("Adc", 8)]
public partial class AdcRmReg8;

[OpRmReg("Adc", 16)]
public partial class AdcRmReg16;

[OpRmReg("Adc", 32)]
public partial class AdcRmReg32;

// ADD
[OpRmReg("Add", 8)]
public partial class AddRmReg8;

[OpRmReg("Add", 16)]
public partial class AddRmReg16;

[OpRmReg("Add", 32)]
public partial class AddRmReg32;

// AND
[OpRmReg("And", 8)]
public partial class AndRmReg8;

[OpRmReg("And", 16)]
public partial class AndRmReg16;

[OpRmReg("And", 32)]
public partial class AndRmReg32;

// OR
[OpRmReg("Or", 8)]
public partial class OrRmReg8;

[OpRmReg("Or", 16)]
public partial class OrRmReg16;

[OpRmReg("Or", 32)]
public partial class OrRmReg32;

// SBB
[OpRmReg("Sbb", 8)]
public partial class SbbRmReg8;

[OpRmReg("Sbb", 16)]
public partial class SbbRmReg16;

[OpRmReg("Sbb", 32)]
public partial class SbbRmReg32;

// SUB
[OpRmReg("Sub", 8)]
public partial class SubRmReg8;

[OpRmReg("Sub", 16)]
public partial class SubRmReg16;

[OpRmReg("Sub", 32)]
public partial class SubRmReg32;

// XOR
[OpRmReg("Xor", 8)]
public partial class XorRmReg8;

[OpRmReg("Xor", 16)]
public partial class XorRmReg16;

[OpRmReg("Xor", 32)]
public partial class XorRmReg32;


// CMP (Sub without assigment)
[OpRmReg("Sub", 8, false, "cmp")]
public partial class CmpRmReg8;

[OpRmReg("Sub", 16, false, "cmp")]
public partial class CmpRmReg16;

[OpRmReg("Sub", 32, false, "cmp")]
public partial class CmpRmReg32;

// TEST (And without assigment)
[OpRmReg("And", 8, false)]
public partial class TestRmReg8;

[OpRmReg("And", 16, false)]
public partial class TestRmReg16;

[OpRmReg("And", 32, false)]
public partial class TestRmReg32;
