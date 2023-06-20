namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
///     Thin interface between renderer and gui.
/// </summary>
public class VgaCard : IVideoCard {
    private readonly IMainWindowViewModel? _gui;
    private readonly ILoggerService _logger;
    private readonly IVgaRenderer _renderer;
    private int _renderHeight;
    private int _renderWidth;
    private int _requiredBufferSize;

    /// <summary>
    ///     Create a new VGA card.
    /// </summary>
    public VgaCard(IMainWindowViewModel? gui, IVgaRenderer renderer, ILoggerService loggerService) {
        _gui = gui;
        _logger = loggerService;
        _renderer = renderer;
        _renderWidth = renderer.Width;
        _renderHeight = renderer.Height;
        _requiredBufferSize = _renderWidth * _renderHeight;
    }

    /// <inheritdoc />
    public void UpdateScreen() {
        if (_renderer.Width != _renderWidth || _renderer.Height != _renderHeight) {
            _gui?.SetResolution(_renderer.Width, _renderer.Height);
            _renderWidth = _renderer.Width;
            _renderHeight = _renderer.Height;
            _requiredBufferSize = _renderWidth * _renderHeight;
        }
        _gui?.UpdateScreen();
    }

    /// <inheritdoc />
    public void Render(Span<uint> buffer) {
        if (buffer.Length < _requiredBufferSize && _logger.IsEnabled(LogEventLevel.Warning)) {
            _logger.Warning("Buffer size {BufferLength} is too small for the required buffer size {RequiredBufferSize} for render resolution {RenderWidth} x {RenderHeight}",
                buffer.Length, _requiredBufferSize, _renderWidth, _renderHeight);
            return;
        }
        _renderer.Render(buffer);
    }
}