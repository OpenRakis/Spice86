namespace Spice86.UI.EventArgs;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;

public delegate void NextFrameEventHandler(FrameEventArgs e);

public class FrameEventArgs : EventArgs {
    public FrameEventArgs(byte[] frame, Rgb[] palette, long frameCount, IEnumerable<VideoBufferViewModel> sortedBuffers) {
        this.Memory = frame;
        this.Palette = palette;
        this.FrameNumber = frameCount;
        this.SortedBuffers = sortedBuffers;
    }

    public byte[] Memory { get; }

    public Rgb[] Palette { get; }

    public long FrameNumber { get; }

    public IEnumerable<VideoBufferViewModel> SortedBuffers { get; }

}
