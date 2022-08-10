
namespace SDLSharp
{
    public enum PixelType
    {
        Unknown,
        Index1,
        Index4,
        Index8,
        Packed8,
        Packed16,
        Packed32,
        ArrayU8,
        ArrayU16,
        ArrayU32,
        ArrayF16,
        ArrayF32,
    }

    public enum BitmapOrder
    {
        None,
        Order4321,
        Order1234,
    }

    public enum PackedOrder
    {
        None,
        XRGB,
        RGBX,
        ARGB,
        RGBA,
        XBGR,
        BGRX,
        ABGR,
        BGRA,
    }

    public enum ArrayOrder
    {
        None,
        RGB,
        RGBA,
        ARGB,
        BGR,
        BGRA,
        ABGR,
    }

    public enum PackedLayout
    {
        None,
        Layout332,
        Layout4444,
        Layout1555,
        Layout5551,
        Layout565,
        Layout8888,
        Layout2101010,
        Layout1010102,
    }

    public enum PixelDataFormat : uint
    {
        Unknown,
        Index1LSB =
            ((1 << 28) | ((PixelType.Index1) << 24) | ((BitmapOrder.Order4321) << 20) | ((0) << 16) | ((1) << 8) | ((0) << 0))
                                      ,
        Index1MSB =
            ((1 << 28) | ((PixelType.Index1) << 24) | ((BitmapOrder.Order4321) << 20) | ((0) << 16) | ((1) << 8) | ((0) << 0))
                                      ,
        Index4LSB =
            ((1 << 28) | ((PixelType.Index4) << 24) | ((BitmapOrder.Order4321) << 20) | ((0) << 16) | ((4) << 8) | ((0) << 0))
                                      ,
        Index4MSB =
            ((1 << 28) | ((PixelType.Index4) << 24) | ((BitmapOrder.Order1234) << 20) | ((0) << 16) | ((4) << 8) | ((0) << 0))
                                      ,
        Index8 =
            ((1 << 28) | ((PixelType.Index8) << 24) | ((0) << 20) | ((0) << 16) | ((8) << 8) | ((1) << 0)),
        RGB332 =
            ((1 << 28) | ((PixelType.Packed8) << 24) | ((PackedOrder.XRGB) << 20) | ((PackedLayout.Layout332) << 16) | ((8) << 8) | ((1) << 0))
                                                            ,
        RGB444 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.XRGB) << 20) | ((PackedLayout.Layout4444) << 16) | ((12) << 8) | ((2) << 0))
                                                              ,
        RGB555 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.XRGB) << 20) | ((PackedLayout.Layout1555) << 16) | ((15) << 8) | ((2) << 0))
                                                              ,
        BGR555 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.XBGR) << 20) | ((PackedLayout.Layout1555) << 16) | ((15) << 8) | ((2) << 0))
                                                              ,
        ARGB4444 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.ARGB) << 20) | ((PackedLayout.Layout4444) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        RGBA4444 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.RGBA) << 20) | ((PackedLayout.Layout4444) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        ABGR4444 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.ABGR) << 20) | ((PackedLayout.Layout4444) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        BGRA4444 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.BGRA) << 20) | ((PackedLayout.Layout4444) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        ARGB1555 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.ARGB) << 20) | ((PackedLayout.Layout1555) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        RGBA5551 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.RGBA) << 20) | ((PackedLayout.Layout5551) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        ABGR1555 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.ABGR) << 20) | ((PackedLayout.Layout1555) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        BGRA5551 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.BGRA) << 20) | ((PackedLayout.Layout5551) << 16) | ((16) << 8) | ((2) << 0))
                                                              ,
        RGB565 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.XRGB) << 20) | ((PackedLayout.Layout565) << 16) | ((16) << 8) | ((2) << 0))
                                                             ,
        BGR565 =
            ((1 << 28) | ((PixelType.Packed16) << 24) | ((PackedOrder.XBGR) << 20) | ((PackedLayout.Layout565) << 16) | ((16) << 8) | ((2) << 0))
                                                             ,
        RGB24 =
            ((1 << 28) | ((PixelType.ArrayU8) << 24) | ((ArrayOrder.RGB) << 20) | ((0) << 16) | ((24) << 8) | ((3) << 0))
                                       ,
        BGR24 =
            ((1 << 28) | ((PixelType.ArrayU8) << 24) | ((ArrayOrder.BGR) << 20) | ((0) << 16) | ((24) << 8) | ((3) << 0))
                                       ,
        RGB888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.XRGB) << 20) | ((PackedLayout.Layout8888) << 16) | ((24) << 8) | ((4) << 0))
                                                              ,
        RGBX8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.RGBX) << 20) | ((PackedLayout.Layout8888) << 16) | ((24) << 8) | ((4) << 0))
                                                              ,
        BGR888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.XBGR) << 20) | ((PackedLayout.Layout8888) << 16) | ((24) << 8) | ((4) << 0))
                                                              ,
        BGRX8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.BGRX) << 20) | ((PackedLayout.Layout8888) << 16) | ((24) << 8) | ((4) << 0))
                                                              ,
        ARGB8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.ARGB) << 20) | ((PackedLayout.Layout8888) << 16) | ((32) << 8) | ((4) << 0))
                                                              ,
        RGBA8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.RGBA) << 20) | ((PackedLayout.Layout8888) << 16) | ((32) << 8) | ((4) << 0))
                                                              ,
        ABGR8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.ABGR) << 20) | ((PackedLayout.Layout8888) << 16) | ((32) << 8) | ((4) << 0))
                                                              ,
        BGRA8888 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.BGRA) << 20) | ((PackedLayout.Layout8888) << 16) | ((32) << 8) | ((4) << 0))
                                                              ,
        ARGB2101010 =
            ((1 << 28) | ((PixelType.Packed32) << 24) | ((PackedOrder.ARGB) << 20) | ((PackedLayout.Layout2101010) << 16) | ((32) << 8) | ((4) << 0))
                                                                 ,

        YV12 =
            ((((uint)(((byte)(('Y'))))) << 0) | (((uint)(((byte)(('V'))))) << 8) | (((uint)(((byte)(('1'))))) << 16) | (((uint)(((byte)(('2'))))) << 24)),
        IYUV =
            ((((uint)(((byte)(('I'))))) << 0) | (((uint)(((byte)(('Y'))))) << 8) | (((uint)(((byte)(('U'))))) << 16) | (((uint)(((byte)(('V'))))) << 24)),
        YUY2 =
            ((((uint)(((byte)(('Y'))))) << 0) | (((uint)(((byte)(('U'))))) << 8) | (((uint)(((byte)(('Y'))))) << 16) | (((uint)(((byte)(('2'))))) << 24)),
        UYVY =
            ((((uint)(((byte)(('U'))))) << 0) | (((uint)(((byte)(('Y'))))) << 8) | (((uint)(((byte)(('V'))))) << 16) | (((uint)(((byte)(('Y'))))) << 24)),
        YVYU =
            ((((uint)(((byte)(('Y'))))) << 0) | (((uint)(((byte)(('V'))))) << 8) | (((uint)(((byte)(('Y'))))) << 16) | (((uint)(((byte)(('U'))))) << 24)),
        NV12 =
            ((((uint)(((byte)(('N'))))) << 0) | (((uint)(((byte)(('V'))))) << 8) | (((uint)(((byte)(('1'))))) << 16) | (((uint)(((byte)(('2'))))) << 24)),
        NV21 =
            ((((uint)(((byte)(('N'))))) << 0) | (((uint)(((byte)(('V'))))) << 8) | (((uint)(((byte)(('2'))))) << 16) | (((uint)(((byte)(('1'))))) << 24))
    }
}

