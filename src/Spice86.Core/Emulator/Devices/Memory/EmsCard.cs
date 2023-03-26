namespace Spice86.Core.Emulator.Devices.Memory; 

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Basic implementation of an EMS Memory add-on card
/// </summary>
public class EmsCard : DefaultIOPortHandler {
    public const int EmmMaxHandles = 200;
    public const int MemorySizeInMb = 6;
    public EmsCard(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService)
    {
        for (int i = 0; i < EmmHandles.Length; i++) {
            EmmHandles[i] = new();
        }

        for (int i = 0; i < EmmMappings.Length; i++) {
            EmmMappings[i] = new();
        }

        for (int i = 0; i < EmmSegmentMappings.Length; i++) {
            EmmSegmentMappings[i] = new();
        }

        Memory = new(MemorySizeInMb);
    }

    public MemoryBlock Memory { get; }

    public EmmMapping[] EmmSegmentMappings { get; } = new EmmMapping[0x40];

    public EmmMapping[] EmmMappings { get; } = new EmmMapping[EmsHandle.EmmMaxPhysicalPages];
    
    public EmsHandle[] EmmHandles { get; } = new EmsHandle[EmmMaxHandles];
    
    public const ushort XmsStart = 0x110;

    public int TotalPages => Memory.Pages;

    public ushort FreeMemoryTotal {
        get {
            ushort free = 0;
            ushort index = XmsStart;
            while (index < TotalPages) {
                if (Memory.MemoryHandles[index] == 0) {
                    free++;
                }

                index++;
            }
            return free;
        }
    }
    
    public ushort FreePages {
        get {
            ushort count = (ushort)(FreeMemoryTotal / 4);
            if (count > 0x7FFF) {
                return 0x7FFF;
            }

            return count;
        }
    }
}