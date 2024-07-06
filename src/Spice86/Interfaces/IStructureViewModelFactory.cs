namespace Spice86.Interfaces;

using AvaloniaHex.Document;

using Spice86.ViewModels;

public interface IStructureViewModelFactory {
    bool IsInitialized { get; }
    StructureViewModel CreateNew(IBinaryDocument data);
    void Parse(string headerFilePath);
}