namespace Spice86.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Emulator.Machine;

using System;
using System.Runtime.InteropServices;
using System.Text;


/// <summary>
/// TODO: Complete it !
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<DosInt21Handler>();

    /// <summary>
    /// TODO : Fix it, the Jave original uses a different CharSet.
    /// </summary>

    private static readonly CharSet CP850_CHARSET = CharSet.Ansi;

    private bool ctrlCFlag = false;

    // dosbox
    private int defaultDrive = 2;

    private StringBuilder displayOutputBuilder = new StringBuilder();
    private DosFileManager dosFileManager;
    private DosMemoryManager dosMemoryManager;

    public DosInt21Handler(Machine machine) : base(machine) {
        dosMemoryManager = new DosMemoryManager(machine.GetMemory());
        dosFileManager = new DosFileManager(memory);
        //FillDispatchTable();
    }

    public override int GetIndex() {
        throw new NotImplementedException();
    }

    public override void Run() {
        throw new NotImplementedException();
    }

    internal DosFileManager GetDosFileManager() {
        return this.dosFileManager;
    }

    internal DosMemoryManager GetDosMemoryManager() {
        return this.dosMemoryManager;
    }
}