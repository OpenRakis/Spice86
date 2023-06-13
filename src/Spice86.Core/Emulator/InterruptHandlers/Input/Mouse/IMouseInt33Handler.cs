namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using Spice86.Core.Emulator.Callback;

public interface IMouseInt33Handler : ICallback {
    void GetSoftwareVersionAndMouseType();

    /// <summary>
    /// Interrupt rate is set by the host OS. NOP.
    /// </summary>
    void SetInterruptRate();

    void GetMousePositionAndStatus();
    void MouseInstalledFlag();
    void SetMouseCursorPosition();
    void SetMouseDoubleSpeedThreshold();
    void SetMouseHorizontalMinMaxPosition();
    void SetMouseMickeyPixelRatio();
    void SetMouseSensitivity();
    void SetMouseUserDefinedSubroutine();
    void SetMouseVerticalMinMaxPosition();
    void SwapMouseUserDefinedSubroutine();
    void ShowMouseCursor();
    void HideMouseCursor();
    void Update();
}