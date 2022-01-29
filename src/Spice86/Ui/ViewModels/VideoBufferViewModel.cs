namespace Spice86.UI.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;

using Spice86.Emulator.Devices.Video;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class VideoBufferViewModel : ViewModelBase, IComparable<VideoBufferViewModel>, IDisposable {
    private bool _disposedValue;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 320;
        Height = 200;
        ScaleFactor = 1;
        Address = 1;
        Index = 1;
    }

    public VideoBufferViewModel(MainWindowViewModel mainWindowViewModel, int width, int height, double scaleFactor, uint address, int index, bool isPrimaryDisplay) {
        MainWindowViewModel = mainWindowViewModel;
        IsPrimaryDisplay = isPrimaryDisplay;
        Width = width;
        Height = height;
        ScaleFactor = scaleFactor;
        Address = address;
        Index = index;
    }

    public event EventHandler? Dirty;

    public uint Address { get; private set; }

    [JsonIgnore]

    private WriteableBitmap? _bitmap;

    /// <summary>
    /// DPI: AvaloniaUI, like WPF, renders UI Controls in Device Independant Pixels.<br/>
    /// According to searches online, DPI is tied to a TopLevel control (a Window).<br/>
    /// Right now, the DPI is hardcoded for WriteableBitmap : https://github.com/AvaloniaUI/Avalonia/issues/1292 <br/>
    /// See also : https://github.com/AvaloniaUI/Avalonia/pull/1889 <br/>
    /// Also WriteableBitmap is an IImage implementation and not a UI Control,<br/>
    /// that's why it's used to bind the Source property of the Image control in VideoBufferView.xaml<br/>
    /// TODO: As a workaround, we must least get the DPI from the Window.<br/>
    /// The ViewModel is not aware of the View, so the Bitmap property is set in VideBufferView.xaml.cs.<br/>
    /// </summary>
    public WriteableBitmap? Bitmap {
        get => _bitmap;
        set => this.RaiseAndSetIfChanged(ref _bitmap, value);
    }

    public int Height { get; private set; }
    public bool IsPrimaryDisplay { get; private set; }
    public MainWindowViewModel? MainWindowViewModel { get; private set; }

    private double _scaleFactor = 1;
    public double ScaleFactor {
        get => _scaleFactor;
        set => this.RaiseAndSetIfChanged(ref _scaleFactor, value);
    }

    public int Width { get; private set; }
    private int Index { get; set; }

    public int CompareTo(VideoBufferViewModel? other) {
        if (Index < other?.Index) {
            return -1;
        } else if (Index == other?.Index) {
            return 0;
        } else {
            return 1;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public unsafe void Draw(byte[] memory, Rgb[] palette) {
        if (_disposedValue || Dirty is null || Bitmap is null) {
            return;
        }
        int size = Width * Height;
        long endAddress = Address + size;
        var buffer = new List<uint>(size);
        for (long i = Address; i < endAddress; i++) {
            byte colorIndex = memory[i];
            Rgb pixel = palette[colorIndex];
            uint argb = pixel.ToArgb();
            buffer.Add(argb);
        }
        using ILockedFramebuffer buf = Bitmap.Lock();
        uint* dst = (uint*)buf.Address;
        for (int i = 0; i < size; i++) {
            uint argb = buffer[i];
            dst[i] = argb;
        }
        Dirty.Invoke(this, EventArgs.Empty);
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is VideoBufferViewModel other) && Index == other.Index;
    }

    public override int GetHashCode() {
        return Index;
    }

    public int GetIndex() {
        return Index;
    }

    public override string? ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                Bitmap?.Dispose();
            }
            _disposedValue = true;
        }
    }
}