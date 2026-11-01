namespace Spice86.Tests.InterruptHandlers;

using Xunit;

/// <summary>
/// Differential contract tests for the BIOS INT 13h disk services.
/// </summary>
public class BiosInt13ServiceContractTest {
    private const uint BufferAddress = 0x8000;

    /// <summary>One case per declared non-Unknown INT 13h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x00, 0x01, 0x0C, 0x0D, 0x10,
                0x11, 0x16, 0x17, 0x18
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x13, SelectorValue = selector };
            }
            // Sector services get an ES:BX transfer buffer and valid CHS geometry
            // on the floppy drive.
            foreach (ServiceContractCase sectorCase in new[] { 0x02, 0x03, 0x04 }.Select(selector => new ServiceContractCase {
                Vector = 0x13, SelectorValue = selector,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DL = 0;
                    machine.Spice86.Machine.CpuState.AL = 1;
                    machine.Spice86.Machine.CpuState.CH = 0;
                    machine.Spice86.Machine.CpuState.CL = 1;
                    machine.Spice86.Machine.CpuState.DH = 0;
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.BX = (ushort)(BufferAddress & 0xF);
                }
            })) {
                yield return sectorCase;
            }
            // GetDriveParameters: one case per drive type, since each path writes a
            // different subset of the declared union. The hard disk path does not
            // write AL or BL, so they are pinned to keep the union deterministic.
            yield return new ServiceContractCase {
                Vector = 0x13, SelectorValue = 0x08,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DL = 0x80;
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.BX = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x13, SelectorValue = 0x08,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DL = 0;
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.BX = 0;
                    machine.Spice86.Machine.CpuState.CX = 0;
                    machine.Spice86.Machine.CpuState.DH = 0;
                }
            };
            // DL=0x80 is a fixed disk, the path that also writes CX/DX.
            yield return new ServiceContractCase {
                Vector = 0x13, SelectorValue = 0x15,
                Setup = machine => machine.Spice86.Machine.CpuState.DL = 0x80
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
}
