namespace Spice86.Interfaces;

using Spice86.Core.Emulator;

public interface IDebugViewModel {
    void ShowColorPalette();
    void ShowPerformance();
    
    IProgramExecutor ProgramExecutor { set; }
}