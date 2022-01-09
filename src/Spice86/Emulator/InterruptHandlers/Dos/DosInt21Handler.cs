namespace Spice86.Emulator.InterruptHandlers.Dos;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Serilog;

using Spice86.Emulator.Machine;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Memory;
using Spice86.Utils;
using System.Runtime.InteropServices;

// TODO: Complete it
public class DosInt21Handler : InterruptHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<DosInt21Handler>();

    private static readonly CharSet CP850_CHARSET = CharSet.Ansi;

    private bool ctrlCFlag = false;
    // dosbox
    private int defaultDrive = 2;
    private DosMemoryManager dosMemoryManager;
    private DosFileManager dosFileManager;
    private StringBuilder displayOutputBuilder = new StringBuilder();

    public DosInt21Handler(Machine machine) : base(machine)
    {
        dosMemoryManager = new DosMemoryManager(machine.GetMemory());
        dosFileManager = new DosFileManager(memory);
        //FillDispatchTable();
    }
    public override int GetIndex()
    {
        throw new NotImplementedException();
    }

    public override void Run()
    {
        throw new NotImplementedException();
    }

    internal DosMemoryManager GetDosMemoryManager()
    {
        return this.dosMemoryManager;
    }

    internal DosFileManager GetDosFileManager()
    {
        return this.dosFileManager;
    }
}
