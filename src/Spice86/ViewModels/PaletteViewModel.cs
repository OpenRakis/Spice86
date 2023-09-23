namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Debugger;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Shared.Emulator.Video;

public partial class PaletteViewModel : ViewModelBase, IEmulatorDebugger {
    private ArgbPalette? _argbPalette;

    public PaletteViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }
    
    public PaletteViewModel(IUIDispatcherTimer uiDispatcherTimer, IProgramExecutor programExecutor) {
        programExecutor.Accept(this);
        for (int i = 0; i < 256; i++) {
            _palette.Add(new (){Fill = new SolidColorBrush()});
        }
        uiDispatcherTimer.StartNew(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors);
    }

    [ObservableProperty]
    private AvaloniaList<Rectangle> _palette = new();

    /// <summary>
    /// Invoked by the timer to update the displayed colors.
    /// </summary>
    /// <param name="sender">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void UpdateColors(object? sender, EventArgs e) {
        try {
            if(_argbPalette is null) {
                return;
            }
            for(int i = 0; i < Palette.Count; i++) {
                Rectangle rectangle = Palette[i];
                uint source = _argbPalette[i];
                Rgb rgb = Rgb.FromUint(source);
                if (rectangle.Fill is SolidColorBrush fill) {
                    fill.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                }
            }
        } catch {
            //A read during emulation provoked an OutOfRangeException (for example, in the DAC).
            // Ignore it.
        }
    }

    public void VisitMainMemory(IMemory memory) {
    }

    public void VisitCpuState(State state) {
    }

    public void VisitVgaRenderer(IVgaRenderer vgaRenderer) {
    }

    public void VisitVideoState(IVideoState videoState) {
    }

    public void VisitDacPalette(ArgbPalette argbPalette) => _argbPalette = argbPalette;
    
    public void VisitDacRegisters(DacRegisters dacRegisters) {
    }

    public void VisitVgaCard(VgaCard vgaCard) {
    }

    public void VisitCpu(Cpu cpu) {
    }

    public void VisitCpuFlags(Flags flags) {
    }
}