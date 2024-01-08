namespace Spice86.Infrastructure;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Shared.Interfaces;

public class ProgramExecutorFactory : IProgramExecutorFactory {
    private readonly Configuration _configuration;
    private readonly ILoggerService _loggerService;

    public ProgramExecutorFactory(Configuration configuration, ILoggerService loggerService) {
        _loggerService = loggerService;
        _configuration = configuration;
    }

    public IProgramExecutor Create(IGui? gui = null) {
        return new ProgramExecutor(_configuration, _loggerService, gui);
    }
}