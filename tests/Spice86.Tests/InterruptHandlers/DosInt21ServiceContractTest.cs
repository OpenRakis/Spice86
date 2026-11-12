namespace Spice86.Tests.InterruptHandlers;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

/// <summary>
/// Differential contract tests for the DOS INT 21h services.
/// </summary>
public class DosInt21ServiceContractTest {
    private const uint BufferAddress = 0x8000;
    private const uint FcbAddress = 0x8100;
    private const uint StringAddress = 0x8200;

    /// <summary>One case per declared non-Unknown INT 21h service.</summary>
    public static IEnumerable<ServiceContractCase> ContractCases {
        get {
            int[] selectors = [
                0x02, 0x03, 0x04, 0x05, 0x0B, 0x0D, 0x0E, 0x19,
                0x1A, 0x1B, 0x1C, 0x26, 0x2A, 0x2C, 0x2F, 0x30, 0x34,
                0x35, 0x36, 0x37, 0x38, 0x4D, 0x50, 0x51, 0x52, 0x58,
                0x62, 0x63, 0x66
            ];
            foreach (int selector in selectors) {
                yield return new ServiceContractCase { Vector = 0x21, SelectorValue = selector };
            }
            // Commit an open file without closing it, or reject an invalid DOS handle.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x68,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "COMMIT.DAT", "DATA", 2);
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x68,
                Setup = machine => machine.Spice86.Machine.CpuState.BX = 0xFFFF
            };
            // DS:DX points at a "$"-terminated string.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x09,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "HELLO$"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                }
            };
            // FCB services get a valid FCB naming a file that exists on drive C:.
            foreach (ServiceContractCase fcbCase in new[] { 0x0F, 0x11, 0x13, 0x23 }.Select(selector => new ServiceContractCase {
                Vector = 0x21, SelectorValue = selector,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteFcb(machine, FcbAddress, "TEST    DAT");
                    SetDsDx(machine, FcbAddress);
                }
            })) {
                yield return fcbCase;
            }
            // Find next follows a find first on the same FCB.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x12,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteFcb(machine, FcbAddress, "TEST    DAT");
                    SetDsDx(machine, FcbAddress);
                    RunService(machine, 0x11);
                    machine.Spice86.Machine.CpuState.AH = 0x12;
                }
            };
            // Close, sequential and random record services need an open FCB.
            foreach (ServiceContractCase recordCase in new[] { 0x10, 0x14, 0x15, 0x21, 0x22, 0x24, 0x27, 0x28 }.Select(selector => new ServiceContractCase {
                Vector = 0x21, SelectorValue = selector,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteFcb(machine, FcbAddress, "TEST    DAT");
                    SetDsDx(machine, FcbAddress);
                    machine.Spice86.Machine.CpuState.CX = 2;
                    RunService(machine, 0x0F);
                    machine.Spice86.Machine.CpuState.AH = (byte)selector;
                }
            })) {
                yield return recordCase;
            }
            // CX=0 is the truncate-to-random-record-position path of random block write.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x28,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteFcb(machine, FcbAddress, "TEST    DAT");
                    SetDsDx(machine, FcbAddress);
                    machine.Spice86.Machine.CpuState.CX = 0;
                    RunService(machine, 0x0F);
                    machine.Spice86.Machine.CpuState.AH = 0x28;
                }
            };
            // Create names a file that does not exist yet.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x16,
                Setup = machine => {
                    WriteFcb(machine, FcbAddress, "NEW     DAT");
                    SetDsDx(machine, FcbAddress);
                }
            };
            // Rename: extended FCB with the new name at offset 0x11.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x17,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteFcb(machine, FcbAddress, "TEST    DAT");
                    WriteBytes(machine, FcbAddress + 0x11, "RENAMED DAT"u8.ToArray());
                    SetDsDx(machine, FcbAddress);
                }
            };
            // Parse: DS:SI is a file name, ES:DI is the FCB to fill.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x29,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "TEST.DAT\0"u8.ToArray());
                    machine.Spice86.Machine.CpuState.DS = (ushort)(StringAddress >> 4);
                    machine.Spice86.Machine.CpuState.SI = (ushort)(StringAddress & 0xF);
                    machine.Spice86.Machine.CpuState.ES = (ushort)(FcbAddress >> 4);
                    machine.Spice86.Machine.CpuState.DI = (ushort)(FcbAddress & 0xF);
                    machine.Spice86.Machine.CpuState.AL = 0x0F;
                }
            };
            // DL=0xFF is the input path, which writes AL and ZF.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x06,
                Setup = machine => machine.Spice86.Machine.CpuState.DL = 0xFF
            };
            // AL selects a free vector; DS:DX is the handler address to store.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x25,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.AL = 0x61;
                    SetDsDx(machine, BufferAddress);
                }
            };
            // AL=6 is the Get break-extended-flag path, the only one that writes BL/BH/DH.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x33,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x06
            };
            // SetDate / SetTime take valid date and time values.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x2B,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.CX = 1993;
                    machine.Spice86.Machine.CpuState.DH = 6;
                    machine.Spice86.Machine.CpuState.DL = 1;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x2D,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.CH = 12;
                    machine.Spice86.Machine.CpuState.CL = 30;
                    machine.Spice86.Machine.CpuState.DH = 0;
                    machine.Spice86.Machine.CpuState.DL = 0;
                }
            };
            // Directory services take a valid ASCIIZ path at DS:DX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x39,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "NEWDIR\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x39,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "Z:\\MISSING\\DIR\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3A,
                Setup = machine => {
                    CreateDirectory(machine, "RMDIR");
                    WriteBytes(machine, StringAddress, "RMDIR\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3B,
                Setup = machine => {
                    CreateDirectory(machine, "CHDIR");
                    WriteBytes(machine, StringAddress, "CHDIR\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3B,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "Z:\\MISSING\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                }
            };
            // Handle-based file services open TEST.DAT first and keep the handle in BX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3C,
                Setup = machine => {
                    WriteBytes(machine, StringAddress, "CREATED.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.CX = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3D,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "TEST.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            foreach (ServiceContractCase handleCase in new[] { 0x3E, 0x45, 0x46 }.Select(selector => new ServiceContractCase {
                Vector = 0x21, SelectorValue = selector,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO");
                    machine.Spice86.Machine.CpuState.CX = 5;
                    machine.Spice86.Machine.CpuState.AH = (byte)selector;
                }
            })) {
                yield return handleCase;
            }
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x3F,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO");
                    machine.Spice86.Machine.CpuState.CX = 4;
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.AH = 0x3F;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x40,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO", 2);
                    machine.Spice86.Machine.CpuState.CX = 4;
                    WriteBytes(machine, BufferAddress, "DATA"u8.ToArray());
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.AH = 0x40;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x41,
                Setup = machine => {
                    CreateFile(machine, "DELETEME.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "DELETEME.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            // A valid handle takes the success path that writes DX:AX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x42,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO");
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.CX = 0;
                    machine.Spice86.Machine.CpuState.DX = 0;
                    machine.Spice86.Machine.CpuState.AH = 0x42;
                }
            };
            // An invalid handle takes the error path that writes AX and CF.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x42,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 0xFFFF;
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x43,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "TEST.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 0;
                }
            };
            // IoControl: Get Device Information on the console device and on an open
            // file; the DosFile path writes DX and AX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x00;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO");
                    machine.Spice86.Machine.CpuState.AL = 0x00;
                }
            };
            // An invalid handle takes the error path that writes AX and CF.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 0xFFFF;
                    machine.Spice86.Machine.CpuState.AL = 0x00;
                }
            };
            // Read and write control data on the console handle, with a small count
            // and a buffer at DS:DX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    WriteBytes(machine, BufferAddress, new byte[8]);
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.CX = 4;
                    machine.Spice86.Machine.CpuState.AL = 0x02;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    WriteBytes(machine, BufferAddress, new byte[8]);
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.CX = 4;
                    machine.Spice86.Machine.CpuState.AL = 0x03;
                }
            };
            // Set Device Information accepts DH=0 and rejects any other DH.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x01;
                    machine.Spice86.Machine.CpuState.DH = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x01;
                    machine.Spice86.Machine.CpuState.DH = 1;
                }
            };
            // The console device does not support the control channels: error path.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x02;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x03;
                }
            };
            // Get Input Status on the console handle and on an open file.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 0;
                    machine.Spice86.Machine.CpuState.AL = 0x06;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = OpenFile(machine, "TEST.DAT", "HELLO");
                    machine.Spice86.Machine.CpuState.AL = 0x06;
                }
            };
            // Get Output Status on the console handle.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x07;
                }
            };
            // Is Device Removable: fixed drive C: returns AX=1, drive A: returns AX=0.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 3;
                    machine.Spice86.Machine.CpuState.AL = 0x08;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x08;
                }
            };
            // Is Device Remote on drive C: writes DX and AX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 3;
                    machine.Spice86.Machine.CpuState.AL = 0x09;
                }
            };
            // Set Sharing Retry Count: DX=0 is an error, DX=1 succeeds.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = 1;
                    machine.Spice86.Machine.CpuState.AL = 0x0B;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = 0;
                    machine.Spice86.Machine.CpuState.AL = 0x0B;
                }
            };
            // Generic Block Device Request on drive C: (CH=08h, parameter block at
            // DS:DX): get device parameters and get volume information.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.BX = 3;
                    machine.Spice86.Machine.CpuState.CH = 0x08;
                    machine.Spice86.Machine.CpuState.CL = 0x60;
                    machine.Spice86.Machine.CpuState.AL = 0x0D;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    SetDsDx(machine, BufferAddress);
                    machine.Spice86.Machine.CpuState.BX = 3;
                    machine.Spice86.Machine.CpuState.CH = 0x08;
                    machine.Spice86.Machine.CpuState.CL = 0x66;
                    machine.Spice86.Machine.CpuState.AL = 0x0D;
                }
            };
            // Get Logical Drive Map on drive C: writes AH and AL.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.BX = 3;
                    machine.Spice86.Machine.CpuState.AL = 0x0E;
                }
            };
            // An invalid subfunction takes the error path that writes AX and CF.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x44,
                Setup = machine => machine.Spice86.Machine.CpuState.AL = 0x0F
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x47,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DL = 0;
                    machine.Spice86.Machine.CpuState.DS = (ushort)(BufferAddress >> 4);
                    machine.Spice86.Machine.CpuState.SI = (ushort)(BufferAddress & 0xF);
                }
            };
            // An invalid drive takes the error path that writes AX and CF.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x47,
                Setup = machine => machine.Spice86.Machine.CpuState.DL = 0xFF
            };
            // A small block fits in free conventional memory.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x48,
                Setup = machine => machine.Spice86.Machine.CpuState.BX = 0x10
            };
            // Free and resize operate on a block allocated in the setup.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x49,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = AllocateBlock(machine);
                    machine.Spice86.Machine.CpuState.AH = 0x49;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x4A,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.ES = AllocateBlock(machine);
                    machine.Spice86.Machine.CpuState.BX = 0x20;
                    machine.Spice86.Machine.CpuState.AH = 0x4A;
                }
            };
            // Find first matches an existing file; find next follows a find first.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x4E,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "*.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.CX = 0;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x4F,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "*.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.CX = 0;
                    RunService(machine, 0x4E);
                    machine.Spice86.Machine.CpuState.AH = 0x4F;
                }
            };
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x55,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.DX = 0x2000;
                    machine.Spice86.Machine.CpuState.SI = 0;
                }
            };
            // AL=2/3 gets or sets a file date and time through the path at DS:DX.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x57,
                Setup = machine => {
                    CreateFile(machine, "TEST.DAT", "HELLO");
                    WriteBytes(machine, StringAddress, "TEST.DAT\0"u8.ToArray());
                    SetDsDx(machine, StringAddress);
                    machine.Spice86.Machine.CpuState.AL = 2;
                }
            };
            // Requesting the current table size takes the success path, which writes
            // no register: AL is pinned so the declared AX write is deterministic.
            yield return new ServiceContractCase {
                Vector = 0x21, SelectorValue = 0x67,
                Setup = machine => {
                    machine.Spice86.Machine.CpuState.AL = 0;
                    machine.Spice86.Machine.CpuState.BX = 0x14;
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

    private static void SetDsDx(ServiceContractMachine machine, uint physicalAddress) {
        machine.Spice86.Machine.CpuState.DS = (ushort)(physicalAddress >> 4);
        machine.Spice86.Machine.CpuState.DX = (ushort)(physicalAddress & 0xF);
    }

    private static void WriteBytes(ServiceContractMachine machine, uint physicalAddress, byte[] bytes) {
        for (int i = 0; i < bytes.Length; i++) {
            machine.Spice86.Machine.Memory.UInt8[physicalAddress + (uint)i] = bytes[i];
        }
    }

    private static void WriteFcb(ServiceContractMachine machine, uint physicalAddress, string nameAndExtension) {
        byte[] bytes = new byte[36];
        for (int i = 0; i < nameAndExtension.Length && i < 11; i++) {
            bytes[1 + i] = (byte)nameAndExtension[i];
        }
        WriteBytes(machine, physicalAddress, bytes);
    }

    private static void CreateFile(ServiceContractMachine machine, string name, string content) {
        string path = Path.Join(machine.CDrivePath, name);
        File.WriteAllText(path, content);
        File.SetAttributes(path, FileAttributes.Normal);
        // DOS exposes local file times. The baseline records 14:00 local on this date.
        DateTime stamp = new(1993, 6, 1, 14, 0, 0, DateTimeKind.Local);
        File.SetCreationTime(path, stamp);
        File.SetLastWriteTime(path, stamp);
        File.SetLastAccessTime(path, stamp);
    }

    private static void CreateDirectory(ServiceContractMachine machine, string name) {
        Directory.CreateDirectory(Path.Join(machine.CDrivePath, name));
    }

    private static void RunService(ServiceContractMachine machine, int selectorValue) {
        machine.Spice86.Machine.CpuState.AH = (byte)selectorValue;
        ((InterruptHandler)machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x21]).Run();
    }

    private static ushort OpenFile(ServiceContractMachine machine, string name, string content) {
        return OpenFile(machine, name, content, 0);
    }

    private static ushort OpenFile(ServiceContractMachine machine, string name, string content,
        byte accessMode) {
        CreateFile(machine, name, content);
        WriteBytes(machine, StringAddress, (name + "\0").Select(c => (byte)c).ToArray());
        SetDsDx(machine, StringAddress);
        machine.Spice86.Machine.CpuState.AL = accessMode;
        RunService(machine, 0x3D);
        return machine.Spice86.Machine.CpuState.AX;
    }

    private static ushort AllocateBlock(ServiceContractMachine machine) {
        machine.Spice86.Machine.CpuState.BX = 0x10;
        RunService(machine, 0x48);
        return machine.Spice86.Machine.CpuState.AX;
    }

    /// <summary>A duplicated DOS handle resolves through the system file table without the
    /// core writing any register: register writes stay the interrupt dispatcher's job.</summary>
    [Fact]
    public void RawDosIoControlResolvesDuplicatedDosHandlesWithoutWritingRegisters() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        DosInt21Handler handler =
            (DosInt21Handler)machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x21];
        (ushort created, bool carryFlag) = handler.CreateFile("RAW.DAT", 0);
        carryFlag.Should().BeFalse();
        ushort originalHandle = created;
        handler.ForceDuplicateHandle(originalHandle, 19).carryFlag.Should().BeFalse();
        handler.CloseFile(originalHandle, 0).carryFlag.Should().BeFalse();
        StateSnapshot before = new(machine.Spice86.Machine.CpuState);

        // Act
        (ushort ax, ushort dx, bool resultError) = handler.IoControl(0, 0, 0, 19, 0);
        (ushort closed, _, bool closedError) = handler.IoControl(0, 0, 0, originalHandle, 0);

        // Assert
        resultError.Should().BeFalse();
        ax.Should().Be(dx);
        closedError.Should().BeTrue();
        closed.Should().Be((ushort)DosErrorCode.InvalidHandle);
        before.AssertUnchanged(machine.Spice86.Machine.CpuState);
        handler.CloseFile(19, 0).carryFlag.Should().BeFalse();
    }

    /// <summary>An invalid get/set-attribute operation reports the error through the raw core
    /// without touching CPU state; the interrupt dispatcher is where the CPU exception for an
    /// unhandled operation lives.</summary>
    [Fact]
    public void InvalidAttributeOperationKeepsCpuExceptionsAtInterruptBoundary() {
        // Arrange
        using ServiceContractMachine machine = new(enableEms: false, enableXms: false);
        DosInt21Handler handler =
            (DosInt21Handler)machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x21];
        File.WriteAllText(Path.Join(machine.CDrivePath, "ATTRIB.DAT"), "data");
        State state = machine.Spice86.Machine.CpuState;
        state.AX = 0x4302;
        state.DS = 0x0800;
        state.DX = 0x0100;
        state.CarryFlag = true;
        machine.Spice86.Machine.Memory.LoadData(0x8100, "C:\\ATTRIB.DAT\0"u8.ToArray());
        StateSnapshot before = new(state);

        // Act / Assert: a raw API reports the error without reading or writing CPU state.
        (ushort ax, ushort cx, bool resultError) = handler.GetSetFileAttributes(2, "C:\\ATTRIB.DAT", 0);
        resultError.Should().BeTrue();
        ax.Should().Be((ushort)DosErrorCode.FunctionNumberInvalid);
        cx.Should().Be(0);
        before.AssertUnchanged(state);
        Action interrupt = () => handler.GetSetFileAttributes(false);
        interrupt.Should().Throw<UnhandledOperationException>();
        state.CarryFlag.Should().BeFalse();
        state.AX.Should().Be(0x4302);
    }

    private sealed class StateSnapshot {
        private readonly uint _eax, _ebx, _ecx, _edx, _esi, _edi, _ebp, _esp;
        private readonly ushort _ds, _es, _fs, _gs, _ss, _cs, _ip;
        private readonly uint _flags;

        public StateSnapshot(State state) {
            _eax = state.EAX; _ebx = state.EBX; _ecx = state.ECX; _edx = state.EDX;
            _esi = state.ESI; _edi = state.EDI; _ebp = state.EBP; _esp = state.ESP;
            _ds = state.DS; _es = state.ES; _fs = state.FS; _gs = state.GS;
            _ss = state.SS; _cs = state.CS; _ip = state.IP;
            _flags = state.Flags.FlagRegister;
        }

        public void AssertUnchanged(State state) {
            state.EAX.Should().Be(_eax); state.EBX.Should().Be(_ebx);
            state.ECX.Should().Be(_ecx); state.EDX.Should().Be(_edx);
            state.ESI.Should().Be(_esi); state.EDI.Should().Be(_edi);
            state.EBP.Should().Be(_ebp); state.ESP.Should().Be(_esp);
            state.DS.Should().Be(_ds); state.ES.Should().Be(_es);
            state.FS.Should().Be(_fs); state.GS.Should().Be(_gs);
            state.SS.Should().Be(_ss); state.CS.Should().Be(_cs);
            state.IP.Should().Be(_ip);
            state.Flags.FlagRegister.Should().Be(_flags);
        }
    }
}
