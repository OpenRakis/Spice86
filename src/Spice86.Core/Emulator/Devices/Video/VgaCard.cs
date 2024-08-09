namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
///     Thin interface between renderer and gui.
/// </summary>
public class VgaCard {
    private readonly IGui? _gui;
    private readonly ILoggerService _logger;
    private readonly IVgaRenderer _renderer;

    /// <summary>
    ///     Create a new VGA card.
    /// </summary>
    /// <param name="gui">The GUI to render to.</param>
    /// <param name="renderer">The VGA renderer to use.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public VgaCard(IGui? gui, IVgaRenderer renderer, ILoggerService loggerService) {
        _gui = gui;
        _logger = loggerService;
        _renderer = renderer;
        if (_gui is not null) {
            _gui.RenderScreen += (_, e) => Render(e);
            // Init bitmaps, needed for GUI to start calling Render function
            _gui.SetResolution(_renderer.Width, _renderer.Height);
        }
    }

    private bool EnsureGuiResolutionMatchesHardware() {
        if (_renderer.Width == _gui?.Width && _renderer.Height == _gui?.Height) {
            // Resolution is matching, nothing to do.
            return true;
        }
        _gui?.SetResolution(_renderer.Width, _renderer.Height);
        // Wait for it to be applied
        while (_renderer.Width != _gui?.Width || _renderer.Height != _gui?.Height);
        // Report that resolution did not match
        return false;
    }
    

    private unsafe void Render(UIRenderEventArgs uiRenderEventArgs) {
        if (!EnsureGuiResolutionMatchesHardware()) {
            // Gui resolution had to be changed, UI event is not valid anymore.
            return;
        }
        var buffer = new Span<uint>((void*)uiRenderEventArgs.Address, uiRenderEventArgs.Length);
        int requiredBufferSize = _renderer.Width * _renderer.Height;
        if (buffer.Length < requiredBufferSize && _logger.IsEnabled(LogEventLevel.Warning)) {
            _logger.Warning("Buffer size {BufferLength} is too small for the required buffer size {RequiredBufferSize} for render resolution {RenderWidth} x {RenderHeight}",
                buffer.Length, requiredBufferSize, _renderer.Width, _renderer.Height);
            return;
        }
        _renderer.Render(buffer);
    }
}