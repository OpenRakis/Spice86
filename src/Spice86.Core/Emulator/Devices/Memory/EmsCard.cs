using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

namespace Spice86.Core.Emulator.Devices.Memory; 

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Basic implementation of an EMS Memory add-on card
/// </summary>
public class EmsCard : DefaultIOPortHandler {
    public EmsCard(Machine machine, Configuration configuration) : base(machine, configuration)
    {
        for (int i = 0; i < 4096; i++) {
            _memoryHandles.Add(i, new());
        }
        _memoryHandles[0] = new EmsHandle(Enumerable.Range(0, 24).Select(i => (ushort)i));

    }
    
    private readonly SortedList<int, EmsHandle> _memoryHandles = new();

    public SortedList<int, EmsHandle> MemoryHandles => _memoryHandles;


    public const ushort XmsStart = 0x110;

    public int TotalPages => _memoryHandles.Count;

    public ushort FreeMemory {
        get {
            ushort free = 0;
            ushort index = XmsStart;
            while (index < TotalPages) {
                if (_memoryHandles[index].PagesAllocated == 0) {
                    free++;
                }

                index++;
            }
            return free;
        }
    }
    
    public ushort FreePages {
        get {
            ushort count = (ushort)(FreeMemory / 4);
            if (count > 0x7FFF) {
                return 0x7FFF;
            }

            return count;
        }
    }
    
    public Memory ExpandedMemory { get; init; } = new(6 * 1024);

}