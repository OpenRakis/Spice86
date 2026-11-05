namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.VGA;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 10h video services.
/// </summary>
public class VgaBiosServiceContractTest {
    private const uint BufferAddress = 0x8000;

    /// <summary>One case per declared INT 10h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0A, 0x0B, 0x0E, 0x0F, 0x1A
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x10, SelectorValue = selector };
            }
            // Graphics pixel services need a graphical video mode; mode 0x13 is set first.
            foreach (ServiceContractCase pixelCase in new[] { 0x0C, 0x0D }.Select(selector => new ServiceContractCase {
                Vector = 0x10, SelectorValue = selector,
                Setup = machine => {
                    VgaBios vgaBios = Vga(machine);
                    machine.Spice86.Machine.CpuState.AH = 0x00;
                    machine.Spice86.Machine.CpuState.AL = 0x13;
                    vgaBios.Run();
                    machine.Spice86.Machine.CpuState.AH = (byte)selector;
                    machine.Spice86.Machine.CpuState.AL = 0x0F;
                    machine.Spice86.Machine.CpuState.CX = 4;
                    machine.Spice86.Machine.CpuState.DX = 4;
                }
            })) {
                yield return pixelCase;
            }
            // WriteString reads the string at ES:BP.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x13,
                Setup = machine => {
                    byte[] text = "HI"u8.ToArray();
                    for (int i = 0; i < text.Length; i++) {
                        machine.Spice86.Machine.Memory.UInt8[BufferAddress + (uint)i] = text[i];
                    }
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.BP = (ushort)(BufferAddress & 0xF);
                    machine.Spice86.Machine.CpuState.CX = (ushort)text.Length;
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.BH = 0;
                    machine.Spice86.Machine.CpuState.BL = 7;
                }
            };
            // AL=0x15 reads one DAC register, writing DH/CH/CL.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x10,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x15
            };
            // AL=0x30 returns the font address, writing ES/BP/CX/DL.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x11,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x30
            };
            // BL=0x10 returns EGA/VGA information, writing BH/BL/CX.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x12,
                Setup = machine => machine.Spice86.Machine.CpuState.BL = 0x10
            };
            // Functionality info fills the buffer at ES:DI.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x1B,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.DI = (ushort)(BufferAddress & 0xF);
                    machine.Spice86.Machine.CpuState.BX = 0;
                }
            };
            // AL=0 requests the VESA information block into the buffer at ES:DI.
            yield return new ServiceContractCase {
                Vector = 0x10, SelectorValue = 0x4F,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.DI = (ushort)(BufferAddress & 0xF);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
        }
    }

    /// <summary>Theory data.</summary>
    public static IEnumerable<object[]> Cases => ContractCases.Select(c => new object[] { c });

    /// <summary>
    /// Each declared contract holds across two runs with different undeclared inputs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void ServicesHonourDeclaredContract(ServiceContractCase testCase) {
        // Arrange / Act / Assert
        ServiceContractHarness.AssertContract(testCase);
    }

    private static VgaBios Vga(ServiceContractMachine machine) {
        return (VgaBios)machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x10];
    }
}
