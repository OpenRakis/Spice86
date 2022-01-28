namespace Spice86.UI;

using Avalonia.Input;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IVideoKeyboardMouseIO {

    void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false);

    void Draw(byte[] memory, Rgb[] palette);

    int GetHeight();

    Key? GetLastKeyCode();

    int GetMouseX();

    int GetMouseY();

    IDictionary<uint, VideoBufferViewModel> GetVideoBuffers();

    int GetWidth();

    bool IsKeyPressed(Key keyCode);

    bool IsLeftButtonClicked();

    bool IsRightButtonClicked();

    void RemoveBuffer(uint address);

    void SetMouseX(int mouseX);

    void SetMouseY(int mouseY);

    void SetOnKeyPressedEvent(Action onKeyPressedEvent);

    void SetOnKeyReleasedEvent(Action onKeyReleasedEvent);

    void SetResolution(int width, int height, uint address);
}