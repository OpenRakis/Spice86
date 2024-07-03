namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Memory;

using Structurizer;
using Structurizer.Types;

public class StructureViewModelFactory : IStructureViewModelFactory {
    private readonly Hydrator? _hydrator;
    private readonly Parser? _parser;
    private readonly StructurizerSettings _structurizerSettings = new();
    private FileSystemWatcher? _fileWatcher;
    private StructureInformation? _structureInformation;

    public StructureViewModelFactory() {
        if (!TryGetHeaderFilePath(out string headerFilePath)) {
            return;
        }
        _parser = new Parser(_structurizerSettings);
        _hydrator = new Hydrator(_structurizerSettings);

        Parse(headerFilePath);
        SetupFileWatcher(headerFilePath);
    }

    public bool IsInitialized => _structureInformation != null && _hydrator != null;

    public StructureViewModel CreateNew(IMemory memory) {
        if (_structureInformation == null || _hydrator == null) {
            throw new InvalidOperationException("Factory not initialized.");
        }

        return new StructureViewModel(_structureInformation, _hydrator, memory);
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

    private static bool TryGetHeaderFilePath(out string headerFilePath) {
        headerFilePath = string.Empty;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        Configuration configuration = CommandLineParser.ParseCommandLine(lifetime?.Args ?? []);
        if (string.IsNullOrWhiteSpace(configuration.StructureFile)) {
            return false;
        }
        headerFilePath = configuration.StructureFile;

        return true;
    }
}