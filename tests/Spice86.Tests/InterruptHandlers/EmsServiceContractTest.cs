namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Xunit;

/// <summary>
/// Differential contract tests for the INT 67h expanded memory (EMS) services.
/// </summary>
public class EmsServiceContractTest {
    private const uint BufferAddress = 0x8000;

    /// <summary>One case per declared INT 67h service. EMS must be enabled.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x40, 0x41, 0x42, 0x46, 0x4B
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase {
                    Vector = 0x67, SelectorValue = selector, NeedsEms = true
                };
            }
            // A small page count can be satisfied.
            yield return new ServiceContractCase {
                Vector = 0x67, SelectorValue = 0x43, NeedsEms = true,
                Setup = machine => machine.Spice86.Machine.CpuState.BX = 4
            };
            // Handle-based services get a freshly allocated handle in DX.
            foreach (ServiceContractCase handleCase in new[] { 0x44, 0x45, 0x4C, 0x51 }.Select(selector => new ServiceContractCase {
                Vector = 0x67, SelectorValue = selector, NeedsEms = true,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = AllocateHandle(machine);
                    machine.Spice86.Machine.CpuState.BX = 0;
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            })) {
                yield return handleCase;
            }
            // Save and restore the page map of a real handle.
            yield return new ServiceContractCase {
                Vector = 0x67, SelectorValue = 0x47, NeedsEms = true,
                Setup = machine => machine.Spice86.Machine.CpuState.DX = AllocateHandle(machine)
            };
            yield return new ServiceContractCase {
                Vector = 0x67, SelectorValue = 0x48, NeedsEms = true,
                Setup = machine => {
                    ushort handle = AllocateHandle(machine);
                    machine.Spice86.Machine.CpuState.DX = handle;
                    Emm(machine).SavePageMap();
                }
            };
            // Map pages from a list at DS:SI.
            yield return new ServiceContractCase {
                Vector = 0x67, SelectorValue = 0x50, NeedsEms = true,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = AllocateHandle(machine);
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.CX = 1;
                    machine.Spice86.Machine.Memory.UInt16[BufferAddress] = 0;
                    machine.Spice86.Machine.Memory.UInt16[BufferAddress + 2] = 0;
                    machine.Spice86.Machine.CpuState.DS = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.SI = (ushort)(BufferAddress & 0xF);
                }
            };
            // GetSetHandleName: AL=0 reads the name into the buffer at ES:DI.
            yield return new ServiceContractCase {
                Vector = 0x67, SelectorValue = 0x53, NeedsEms = true,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = AllocateHandle(machine);
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.DI = (ushort)(BufferAddress & 0xF);
                }
            };
            // Buffer-output services write into the array at ES:DI.
            foreach (ServiceContractCase bufferCase in new[] { 0x4D, 0x58, 0x59 }.Select(selector => new ServiceContractCase {
                Vector = 0x67, SelectorValue = selector, NeedsEms = true,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.DI = (ushort)(BufferAddress & 0xF);
                    machine.Spice86.Machine.CpuState.AL = 0x01;
                }
            })) {
                yield return bufferCase;
            }
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

    private static ExpandedMemoryManager Emm(ServiceContractMachine machine) {
        return (ExpandedMemoryManager)machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x67];
    }

    private static ushort AllocateHandle(ServiceContractMachine machine) {
        machine.Spice86.Machine.CpuState.BX = 4;
        Emm(machine).AllocatePages();
        return machine.Spice86.Machine.CpuState.DX;
    }
}
