namespace Spice86.UI;

using Avalonia.Input;

using Spice86.Emulator.Devices.Video;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// TODO : complete it, along with all the UI framework.
/// </summary>
public class Gui {

    internal void AddBuffer(uint address, double scale, int v1, int v2, object? p) {
        throw new NotImplementedException();
    }

    internal void Draw(byte[] vs, Rgb[] rgbs) {
        throw new NotImplementedException();
    }

    internal int GetHeight() {
        throw new NotImplementedException();
    }

    internal Key GetLastKeyCode() {
        throw new NotImplementedException();
    }

    internal int GetMouseX() {
        throw new NotImplementedException();
    }

    internal int GetMouseY() {
        throw new NotImplementedException();
    }

    internal IDictionary<uint, VideoBuffer> GetVideoBuffers() {
        throw new NotImplementedException();
    }

    internal int GetWidth() {
        throw new NotImplementedException();
    }

    internal bool IsKeyPressed(Key keyCode) {
        throw new NotImplementedException();
    }

    internal bool IsLeftButtonClicked() {
        throw new NotImplementedException();
    }

    internal bool IsRightButtonClicked() {
        throw new NotImplementedException();
    }

    internal void RemoveBuffer(uint address) {
        throw new NotImplementedException();
    }

    internal void SetMouseX(int x) {
        throw new NotImplementedException();
    }

    internal void SetOnCloseRequest(Action<object> p) {
        throw new NotImplementedException();
    }

    internal void Show() {
        throw new NotImplementedException();
    }

    internal void SetOnShown(Func<object, Task> p) {
        throw new NotImplementedException();
    }

    internal void SetTitle(string v) {
        throw new NotImplementedException();
    }

    internal void SetMouseY(int y) {
        throw new NotImplementedException();
    }

    internal void SetOnKeyPressedEvent(Action v) {
        throw new NotImplementedException();
    }

    internal void SetOnKeyReleasedEvent(Action v) {
        throw new NotImplementedException();
    }

    internal void SetResolution(int videoWidth, int videoHeight, uint v) {
        throw new NotImplementedException();
    }
}