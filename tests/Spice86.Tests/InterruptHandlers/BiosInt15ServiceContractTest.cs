namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 15h system services.
/// </summary>
public class BiosInt15ServiceContractTest {
    private const uint BufferAddress = 0x8000;
    private const uint GdtAddress = 0x8100;
    private const uint SourceAddress = 0x8200;
    private const uint DestinationAddress = 0x8400;

    /// <summary>One case per declared INT 15h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x06, 0x41, 0x4F, 0x86, 0x88, 0x90, 0x91,
                0xC0, 0xC2, 0xC4
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x15, SelectorValue = selector };
            }
            // ES:BX names the VFont structure the service reads.
            yield return new ServiceContractCase {
                Vector = 0x15, SelectorValue = 0x50,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.BX = (ushort)(BufferAddress & 0xF);
                }
            };
            // CopyExtendedMemory reads a GDT at ES:SI and copies CX words from the
            // source descriptor to the destination descriptor.
            yield return new ServiceContractCase {
                Vector = 0x15, SelectorValue = 0x87,
                Setup = machine => {
                    WriteDescriptor(machine, GdtAddress + 2 * 8, SourceAddress, 0x100);
                    WriteDescriptor(machine, GdtAddress + 3 * 8, DestinationAddress, 0x100);
                    machine.Spice86.Machine.CpuState.ES = (ushort)(GdtAddress >> 4);
                    machine.Spice86.Machine.CpuState.SI = (ushort)(GdtAddress & 0xF);
                    machine.Spice86.Machine.CpuState.CX = 2;
                }
            };
            // AL=3 returns the A20 support bitmask, writing BX and AH.
            yield return new ServiceContractCase {
                Vector = 0x15, SelectorValue = 0x24,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x03
            };
            // AL=1 cancels the wait: it always writes CF and the BIOS data area.
            yield return new ServiceContractCase {
                Vector = 0x15, SelectorValue = 0x83,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x01
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

    private static void WriteDescriptor(ServiceContractMachine machine, uint address,
        uint baseAddress, ushort limit) {
        machine.Spice86.Machine.Memory.UInt16[address] = limit;
        machine.Spice86.Machine.Memory.UInt16[address + 2] = (ushort)(baseAddress & 0xFFFF);
        machine.Spice86.Machine.Memory.UInt8[address + 4] = (byte)(baseAddress >> 16);
        machine.Spice86.Machine.Memory.UInt8[address + 5] = 0x93;
        machine.Spice86.Machine.Memory.UInt8[address + 6] = 0;
        machine.Spice86.Machine.Memory.UInt8[address + 7] = 0;
    }
}
