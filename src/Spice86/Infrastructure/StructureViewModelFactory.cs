namespace Spice86.Infrastructure;

using AvaloniaHex.Document;

using Spice86.Core.CLI;
using Spice86.Interfaces;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;

using Structurizer;
using Structurizer.Types;

public class StructureViewModelFactory : IStructureViewModelFactory {
    private readonly Hydrator? _hydrator;
    private readonly Parser? _parser;
    private readonly StructurizerSettings _structurizerSettings = new();
    private StructureInformation? _structureInformation;
    private readonly Configuration _configuration;
    private readonly ILoggerService _logger;

    public event EventHandler? StructureInformationChanged;

    public StructureViewModelFactory(Configuration configuration, ILoggerService logger) {
        _logger = logger;
        _configuration = configuration;
        if (!TryGetHeaderFilePath(out string headerFilePath)) {
            return;
        }
        _parser = new Parser(_structurizerSettings);
        _hydrator = new Hydrator(_structurizerSettings);

        Parse(headerFilePath);
        var poller = new FilePoller(headerFilePath, () => Parse(headerFilePath));
        poller.Start();
    }

    public bool IsInitialized => _structureInformation != null && _hydrator != null;

    public StructureViewModel CreateNew(IBinaryDocument data) {
        if (_structureInformation == null || _hydrator == null) {
            throw new InvalidOperationException("Factory not initialized.");
        }

        var viewModel = new StructureViewModel(_structureInformation, _hydrator, data);
        StructureInformationChanged += viewModel.OnStructureInformationChanged;

        return viewModel;
    }

    public void Parse(string headerFilePath) {
        _logger.Information("Start parsing {HeaderFilePath} for structure information", headerFilePath);
        if (_parser == null) {
            throw new InvalidOperationException("Factory not initialized.");
        }
        if (!File.Exists(headerFilePath)) {
            throw new FileNotFoundException($"Specified structure file not found: '{headerFilePath}'");
        }

        string source = File.ReadAllText(headerFilePath);

        _structureInformation = _parser.ParseSource(source);
        StructureInformationChanged?.Invoke(this, EventArgs.Empty);
        _logger.Information("Parsing {HeaderFilePath} complete", headerFilePath);
    }

    private bool TryGetHeaderFilePath(out string headerFilePath) {
        headerFilePath = string.Empty;
        if (string.IsNullOrWhiteSpace(_configuration.StructureFile)) {
            return false;
        }
        headerFilePath = _configuration.StructureFile;

        return true;
    }
}