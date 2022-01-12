namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Emulator.Machine;

using System;

public class GdbCommandHandler
{
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbCommandHandler>();
    private GdbIo gdbIo;
    private Machine machine;
    private bool connected = true;
    private GdbCommandRegisterHandler gdbCommandRegisterHandler;

    //private GdbCommandMemoryHandler gdbCommandMemoryHandler;
    //private GdbCustomCommandsHandler gdbCustomCommandsHandler;
    //private GdbCommandBreakpointHandler gdbCommandBreakpointHandler;
    public GdbCommandHandler(GdbIo gdbIo, Machine machine, string defaultDumpDirectory)
    {
        this.gdbIo = gdbIo;
        this.machine = machine;
        this.gdbCommandRegisterHandler = new GdbCommandRegisterHandler(gdbIo, machine);
        //this.gdbCommandMemoryHandler = new GdbCommandMemoryHandler(gdbIo, machine);
        //this.gdbCommandBreakpointHandler = new GdbCommandBreakpointHandler(gdbIo, machine);
        //this.gdbCustomCommandsHandler = new GdbCustomCommandsHandler(gdbIo, machine, gdbCommandBreakpointHandler.OnBreakPointReached(), defaultDumpDirectory);
    }

    public bool IsConnected()
    {
        return connected;
    }

    public void RunCommand(string command)
    {
        _logger.Information("Received command {@Command}", command);
        char first = command[0];
        string commandContent = command.Substring(1);
        PauseHandler pauseHandler = machine.GetMachineBreakpoints().GetPauseHandler();
        pauseHandler.RequestPauseAndWait();
        try
        {
            string response = "";
            if (response != null)
            {
                gdbIo.SendResponse(response);
            }
        }
        finally
        {
            //if (gdbCommandBreakpointHandler.IsResumeEmulatorOnCommandEnd())
            //{
            //    pauseHandler.RequestResume();
            //}
        }
    }

    private string HandleThreadALive()
    {
        return gdbIo.GenerateResponse("OK");
    }

    public void PauseEmulator()
    {
        //gdbCommandBreakpointHandler.SetResumeEmulatorOnCommandEnd(false);
        machine.GetMachineBreakpoints().GetPauseHandler().RequestPause();
    }

    private string SetThreadContext()
    {
        return gdbIo.GenerateResponse("OK");
    }

    private string ReasonHalted()
    {
        return gdbIo.GenerateResponse("S05");
    }

    private string QueryVariable(string command)
    {
        if (command.StartsWith("Supported:"))
        {
            String[] supportedRequestItems = command.Replace("Supported:", "").Split(";");
            //Dictionary<string, object> supportedRequest = Arrays.Stream(supportedRequestItems).Map(this.ParseSupportedQuery()).Collect(Collectors.ToMap((data) => (string)data[0];
            //    , (data) => data[1];
            //    ));
            //if (!"i386".Equals(supportedRequest.Get("xmlRegisters")))
            //{
            //    return gdbIo.GenerateUnsupportedResponse();
            //}

            return gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("L"))
        {
            string nextthread = command.Substring(4);
            return gdbIo.GenerateResponse("qM011" + nextthread + "00000001");
        }

        if (command.StartsWith("P"))
        {
            return gdbIo.GenerateResponse("");
        }

        if (command.StartsWith("ThreadExtraInfo"))
        {
            return gdbIo.GenerateMessageToDisplayResponse("spice86");
        }

        if (command.StartsWith("Rcmd"))
        {
            //return gdbCustomCommandsHandler.HandleCustomCommands(command);
        }

        if (command.StartsWith("Search"))
        {
            //return gdbCommandMemoryHandler.SearchMemory(command);
        }

        return "";
    }

    private Object[] ParseSupportedQuery(string item)
    {
        Object[] res = new Object[2];
        if (item.EndsWith("+"))
        {
            res[0] = item.Substring(0, item.Length - 1);
            res[1] = true;
        }
        else if (item.EndsWith("-"))
        {
            res[0] = item.Substring(0, item.Length - 1);
            res[1] = false;
        }
        else
        {
            String[] split = item.Split("=");
            res[0] = split[0];
            if (split.Length == 2)
            {
                res[1] = split[1];
            }
        }

        return res;
    }

    private string ProcessVPacket(string commandContent)
    {
        return "";
    }

    private string Kill()
    {
        machine.GetCpu().SetRunning(false);
        return Detach();
    }

    private string Detach()
    {
        connected = false;
        //gdbCommandBreakpointHandler.SetResumeEmulatorOnCommandEnd(true);
        return gdbIo.GenerateResponse("");
    }
}