namespace Spice86.Core.Emulator.LoadableFile.Dos;

using System.Text;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
/// <summary>
/// Generates a Process Segment Prefix (PSP) and sets it up with the appropriate values.
/// </summary>
public class PspGenerator {
    private const ushort DTA_OR_COMMAND_LINE_OFFSET = 0x80;
    private const ushort LAST_FREE_SEGMENT_OFFSET = 0x02;
    private const ushort ENVIRONMENT_SEGMENT_OFFSET = 0x2C;
    private readonly IMemory _memory;
    private readonly EnvironmentBlockGenerator _environmentBlockGenerator;
    private readonly DosMemoryManager _dosMemoryManager;
    private readonly DosFileManager _dosFileManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PspGenerator"/>
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="environmentVariables">The master environment block from the DOS kernel.</param>
    /// <param name="dosMemoryManager">The DOS memory manager.</param>
    /// <param name="dosFileManager">The DOS file manager.</param>
    public PspGenerator(IMemory memory, EnvironmentVariables environmentVariables, DosMemoryManager dosMemoryManager, DosFileManager dosFileManager) {
        _memory = memory;
        _dosMemoryManager = dosMemoryManager;
        _dosFileManager = dosFileManager;
        _environmentBlockGenerator = new(environmentVariables);
    }

    /// <summary>
    /// Generates a Process Segment Prefix (PSP) at the specified segment address and sets it up with the appropriate values.
    /// </summary>
    /// <param name="pspSegment">The segment address at which to generate the PSP.</param>
    /// <param name="arguments">The command-line arguments to pass to the program.</param>
    public void GeneratePsp(ushort pspSegment, string? arguments) {
        uint pspAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0);

        // Set the PSP's first 2 bytes to INT 20h.
        _memory.UInt16[pspAddress] = 0xCD20;

        // Set the PSP's last free segment value.
        const ushort lastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;
        _memory.UInt16[pspAddress + LAST_FREE_SEGMENT_OFFSET] = lastFreeSegment;

        // Load the command-line arguments into the PSP.
        _memory.LoadData(pspAddress + DTA_OR_COMMAND_LINE_OFFSET, ArgumentsToDosBytes(arguments));

        // Copy the DOS env vars into the PSP.
        _memory.LoadData(pspAddress + ENVIRONMENT_SEGMENT_OFFSET, _environmentBlockGenerator.BuildEnvironmentBlock());

        // Initialize the memory manager with the PSP segment and the last free segment value.
        _dosMemoryManager.Init(pspSegment, lastFreeSegment);

        // Set the disk transfer area address to the command-line offset in the PSP.
        _dosFileManager.SetDiskTransferAreaAddress(pspSegment, DTA_OR_COMMAND_LINE_OFFSET);
    }

    /// <summary>
    /// Converts the specified command-line arguments string into the format used by DOS.
    /// </summary>
    /// <param name="arguments">The command-line arguments string.</param>
    /// <returns>The command-line arguments in the format used by DOS.</returns>
    private static byte[] ArgumentsToDosBytes(string? arguments) {
        byte[] res = new byte[128];
        string correctLengthArguments = "";
        if (string.IsNullOrWhiteSpace(arguments) == false) {
            // Cut strings longer than 127 characters.
            correctLengthArguments = arguments.Length > 127 ? arguments[..127] : arguments;
        }

        // Set the command line size.
        res[0] = (byte)correctLengthArguments.Length;

        // Copy the actual characters.
        int index = 0;
        for (; index < correctLengthArguments.Length; index++) {
            char str = correctLengthArguments[index];
            res[index + 1] = Encoding.ASCII.GetBytes(str.ToString())[0];
        }

        res[index + 1] = 0x0D; // Carriage return.
        return res;
    }
}