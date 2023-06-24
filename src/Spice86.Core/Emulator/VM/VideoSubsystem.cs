namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Contains the VGA card, the VGA port handler, the VGA services, the VGA registers, the VGA renderer, the video interrupts, and VGA ROM.
/// </summary>
public class VideoSubsystem {
    
    /// <summary>
    /// The VGA services for text and graphics
    /// </summary>
    public IVgaFunctionality VgaFunctions { get; set; }

    /// <summary>
    /// The VGA Card.
    /// </summary>
    public IVideoCard VgaCard { get; }
    
    /// <summary>
    /// The Vga Registers
    /// </summary>
    public VideoState VgaRegisters { get; set; }
    
    /// <summary>
    /// The VGA port handler
    /// </summary>
    public IIOPortHandler VgaIoPortHandler { get; }

    /// <summary>
    /// The class that handles converting video memory to a bitmap 
    /// </summary>
    public readonly IVgaRenderer VgaRenderer;
    
    /// <summary>
    /// The Video BIOS interrupt handler.
    /// </summary>
    public IVideoInt10Handler VideoInt10Handler { get; }
    
    /// <summary>
    /// The Video Rom containing fonts and other data.
    /// </summary>
    public VgaRom VgaRom { get; }
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="gui">The GUI. Can be <c>null</c> in headless mode.</param>
    public VideoSubsystem(Machine machine, Configuration configuration, ILoggerService loggerService, IMainWindowViewModel? gui) {
        VgaRegisters = new VideoState();
        VgaIoPortHandler = new VgaIoPortHandler(machine, configuration, loggerService, VgaRegisters);
        machine.RegisterIoPortHandler(VgaIoPortHandler);

        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(VgaRegisters);
        machine.Memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        VgaRenderer = new Renderer(VgaRegisters, vgaMemory);
        VgaCard = new VgaCard(gui, VgaRenderer, loggerService);
        VgaRom = new VgaRom();
        machine.Memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, VgaRom.Size, VgaRom);
        VgaFunctions = new VgaFunctionality(machine.Memory, machine.IoPortDispatcher, machine.BiosDataArea, VgaRom);
        VideoInt10Handler = new VgaBios(machine, VgaFunctions, machine.BiosDataArea, loggerService);
        machine.RegisterCallbackHandler(VideoInt10Handler);
    }
}