namespace Spice86.Ui;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.Emulator.Devices.Video;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
/// <summary>
/// TODO: complete it!
/// </summary>
public class VideoBuffer : IComparable<VideoBuffer>, ISerializable {
    private int address;
    private int width;
    private int height;
    private double scaleFactor;
    private int index;
    private Canvas canvas;
    public VideoBuffer(int width, int height, double scaleFactor, int address, int index) {
        this.width = width;
        this.height = height;
        this.scaleFactor = scaleFactor;
        this.address = address;
        this.index = index;
        //this.canvas = new Canvas(width, height);
        //canvas.GetGraphicsContext2D();
        //if (scaleFactor != 1) {
        //    Scale scale = new Scale();
        //    scale.SetPivotX(0);
        //    scale.SetPivotY(0);
        //    scale.SetX(this.scaleFactor);
        //    scale.SetY(this.scaleFactor);
        //    canvas.GetTransforms().Add(scale);
        //}
    }

    public virtual Canvas GetCanvas() {
        return canvas;
    }

    public virtual int GetIndex() {
        return index;
    }

    public virtual void Draw(byte[] memory, Rgb[] palette) {
        if (canvas == null) {
            return;
        }

        int size = width * height;
        //IntBuffer buffer = IntBuffer.Allocate(size);
        //int endAddress = address + size;
        //for (int i = address; i < endAddress; i++) {
        //    int colorIndex = ConvertUtils.Uint8(memory[i]);
        //    Rgb pixel = palette[colorIndex];
        //    int argb = pixel.ToArgb();
        //    buffer.Put(argb);
        //}

        //buffer.Flip();
        //Dispatcher.UIThread.InvokeAsync(() => {
        //    GraphicsContext gc = canvas.GetGraphicsContext2D();
        //    PixelWriter pw = gc.GetPixelWriter();
        //    pw.SetPixels(0, 0, width, height, PixelFormat.Bgra8888, buffer, width);
        //});
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context) {
        throw new NotImplementedException();
    }

    public int CompareTo(VideoBuffer? other) {
        throw new NotImplementedException();
    }
}
