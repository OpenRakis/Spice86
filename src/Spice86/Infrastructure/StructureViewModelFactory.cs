namespace Spice86.Infrastructure;

using AvaloniaHex.Document;

using Spice86.Core.CLI;
using Spice86.Interfaces;
using Spice86.ViewModels;

using Structurizer;
using Structurizer.Types;

public class StructureViewModelFactory : IStructureViewModelFactory {
    private readonly Hydrator? _hydrator;
    private readonly Parser? _parser;
    private readonly StructurizerSettings _structurizerSettings = new();
    private FileSystemWatcher? _fileWatcher;
    private StructureInformation? _structureInformation;
    private readonly Configuration _configuration;

    public StructureViewModelFactory(Configuration configuration) {
        _configuration = configuration;
        if (!TryGetHeaderFilePath(out string headerFilePath)) {
            return;
        }
        _parser = new Parser(_structurizerSettings);
        _hydrator = new Hydrator(_structurizerSettings);

        Parse(headerFilePath);
        SetupFileWatcher(headerFilePath);
    }

    public bool IsInitialized => _structureInformation != null && _hydrator != null;

    public StructureViewModel CreateNew(IBinaryDocument data) {
        if (_structureInformation == null || _hydrator == null) {
            throw new InvalidOperationException("Factory not initialized.");
        }

        return new StructureViewModel(_structureInformation, _hydrator, data);
    }

    public void Parse(string headerFilePath) {
        if (_parser == null) {
            throw new InvalidOperationException("Factory not initialized.");
        }
        if (!File.Exists(headerFilePath)) {
            throw new FileNotFoundException($"Specified structure file not found: '{headerFilePath}'");
        }
        _structureInformation = _parser.ParseFile(headerFilePath);
    }

    private void SetupFileWatcher(string filePath) {
        string? directory = Path.GetDirectoryName(filePath);
        string fileName = Path.GetFileName(filePath);

        _fileWatcher = new FileSystemWatcher(directory!, fileName) {
            NotifyFilter = NotifyFilters.LastWrite
        };

        _fileWatcher.Changed += (_, _) => Parse(filePath);
        _fileWatcher.EnableRaisingEvents = true;
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