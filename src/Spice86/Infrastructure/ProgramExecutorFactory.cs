namespace Spice86.Infrastructure;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class ProgramExecutorFactory : IProgramExecutorFactory {
    private readonly Configuration _configuration;
    private readonly ILoggerService _loggerService;
    private readonly IPauseHandler _pauseHandler;

    public ProgramExecutorFactory(Configuration configuration, ILoggerService loggerService, IPauseHandler pauseHandler) {
        _loggerService = loggerService;
        _configuration = configuration;
        _pauseHandler = pauseHandler;
    }

    public IProgramExecutor Create(IGui? gui = null) {
        return new ProgramExecutor(_configuration, _loggerService, _pauseHandler, gui);
    }
}