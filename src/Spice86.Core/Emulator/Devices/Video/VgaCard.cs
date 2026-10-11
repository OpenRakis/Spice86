using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.Devices.Video;


using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
///     Thin interface between renderer and gui.
/// </summary>
public class VgaCard {
    private readonly IGuiVideoPresentation? _gui;
    private readonly ILogger _logger;
    private readonly IVgaRenderer _renderer;
    private VideoFrameInfo? _requestedFrameInfo;

    /// <summary>
    ///     Create a new VGA card.
    /// </summary>
    /// <param name="gui">The GUI to render to.</param>
    /// <param name="renderer">The VGA renderer to use.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public VgaCard(IGuiVideoPresentation? gui, IVgaRenderer renderer, ILogger loggerService) {
        _gui = gui;
        _logger = loggerService;
        _renderer = renderer;
    }

    /// <summary>
    /// Completes the initialization of the VGA card by subscribing it to external events.
    /// This method should not be called from the constructor to prevent race conditions
    /// where an event handler (like Render) could be invoked on a partially constructed object.
    /// </summary>
    public void SubscribeToEvents() {
        if (_gui is not null) {
            // Init bitmaps, needed for GUI to start calling Render function
            VideoFrameInfo info = _renderer.FrameInfo;
            _gui.UpdateResolution(info.Width, info.Height, info.AspectRatioCorrectionFactor);
            _requestedFrameInfo = info;
            _gui.RenderScreen += (_, e) => Render(e);
        }
    }

    private bool EnsureGuiResolutionMatchesHardware() {
        VideoFrameInfo info = _renderer.FrameInfo;
        if (info == _requestedFrameInfo && info.Width == _gui?.Width && info.Height == _gui?.Height) {
            // Resolution is matching, nothing to do.
            return true;
        }
        _gui?.UpdateResolution(info.Width, info.Height, info.AspectRatioCorrectionFactor);
        _requestedFrameInfo = info;
        // Resolution change is asynchronous via Dispatcher.Post; skip this frame
        // to avoid blocking the UI thread (the previous busy-wait would deadlock
        // now that DrawScreen runs on the UI thread via DispatcherTimer).
        return false;
    }


    private unsafe void Render(UIRenderEventArgs uiRenderEventArgs) {
        if (!EnsureGuiResolutionMatchesHardware()) {
            // Gui resolution had to be changed, UI event is not valid anymore.
            return;
        }
        Span<uint> buffer = new((void*)uiRenderEventArgs.Address, uiRenderEventArgs.Length);
        int requiredBufferSize = uiRenderEventArgs.RowStride * uiRenderEventArgs.Height;
        if (buffer.Length < requiredBufferSize) {
            if (_logger.IsEnabled(LogLevel.Warning)) {
                _logger.LogWarning("Buffer size {BufferLength} is too small for the required buffer size {RequiredBufferSize} for render resolution {RenderWidth} x {RenderHeight}",
                    buffer.Length, requiredBufferSize, _renderer.Width, _renderer.Height);
            }
            return;
        }
        _renderer.Render(buffer, uiRenderEventArgs.Width, uiRenderEventArgs.Height, uiRenderEventArgs.RowStride);
    }
}
