namespace Spice86.ViewModels;

using Spice86.Core.Emulator.Memory;

public interface IStructureViewModelFactory {
    bool IsInitialized { get; }
    StructureViewModel CreateNew(IMemory memory);
    void Parse(string headerFilePath);
}