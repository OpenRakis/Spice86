namespace Spice86.Emulator.Loadablefile.Dos;

using Spice86.Emulator.InterruptHandlers.Dos;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using System.Text;

public class PspGenerator {
    private static readonly ushort DTA_OR_COMMAND_LINE_OFFSET = 0x80;
    private static readonly ushort LAST_FREE_SEGMENT_OFFSET = 0x02;
    private readonly Machine machine;

    public PspGenerator(Machine machine) {
        this.machine = machine;
    }

    public void GeneratePsp(ushort pspSegment, string arguments) {
        Memory memory = machine.GetMemory();
        uint pspAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);

        // https://en.wikipedia.org/wiki/Program_Segment_Prefix
        memory.SetUint16(pspAddress, 0xCD20); // INT20h

        // last free segment, dosbox seems to put it just before VRAM.
        ushort lastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
        memory.SetUint16(pspAddress + LAST_FREE_SEGMENT_OFFSET, lastFreeSegment);
        memory.LoadData(pspAddress + DTA_OR_COMMAND_LINE_OFFSET, ArgumentsToDosBytes(arguments));
        DosInt21Handler dosFunctionDispatcher = machine.GetDosInt21Handler();
        dosFunctionDispatcher.GetDosMemoryManager().Init(pspSegment, lastFreeSegment);
        dosFunctionDispatcher.GetDosFileManager().SetDiskTransferAreaAddress(pspSegment, DTA_OR_COMMAND_LINE_OFFSET);
    }

    private byte[] ArgumentsToDosBytes(string? arguments) {
        byte[] res = new byte[128];
        string correctLengthArguments = "";
        if (string.IsNullOrWhiteSpace(arguments) == false) {
            // Cut strings longer than 127 chrs
            correctLengthArguments = arguments.Length > 127 ? arguments[..127] : arguments;
        }

        // Command line size
        res[0] = (byte)correctLengthArguments.Length;

        // Copy actual characters
        int index = 0;
        for (; index < correctLengthArguments.Length; index++) {
            char str = correctLengthArguments[index];
            byte chr = Encoding.ASCII.GetBytes(str.ToString())[0];
            res[index + 1] = chr;
        }

        res[index + 1] = 0x0D;
        return res;
    }
}