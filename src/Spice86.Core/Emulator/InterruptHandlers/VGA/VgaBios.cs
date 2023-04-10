namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog;
using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class VgaBios : InterruptHandler, IVgaInterrupts {

    /* Mono */
    private static readonly byte[] palette0 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f,
        0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a, 0x2a,
        0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f,
        0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f, 0x3f
    };

    private static readonly byte[] palette1 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x15, 0x00, 0x2a, 0x2a, 0x2a,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x15, 0x00, 0x2a, 0x2a, 0x2a,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x15, 0x00, 0x2a, 0x2a, 0x2a,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x15, 0x00, 0x2a, 0x2a, 0x2a,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f
    };

    private static readonly byte[] palette2 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x2a, 0x00, 0x2a, 0x2a, 0x2a,
        0x00, 0x00, 0x15, 0x00, 0x00, 0x3f, 0x00, 0x2a, 0x15, 0x00, 0x2a, 0x3f,
        0x2a, 0x00, 0x15, 0x2a, 0x00, 0x3f, 0x2a, 0x2a, 0x15, 0x2a, 0x2a, 0x3f,
        0x00, 0x15, 0x00, 0x00, 0x15, 0x2a, 0x00, 0x3f, 0x00, 0x00, 0x3f, 0x2a,
        0x2a, 0x15, 0x00, 0x2a, 0x15, 0x2a, 0x2a, 0x3f, 0x00, 0x2a, 0x3f, 0x2a,
        0x00, 0x15, 0x15, 0x00, 0x15, 0x3f, 0x00, 0x3f, 0x15, 0x00, 0x3f, 0x3f,
        0x2a, 0x15, 0x15, 0x2a, 0x15, 0x3f, 0x2a, 0x3f, 0x15, 0x2a, 0x3f, 0x3f,
        0x15, 0x00, 0x00, 0x15, 0x00, 0x2a, 0x15, 0x2a, 0x00, 0x15, 0x2a, 0x2a,
        0x3f, 0x00, 0x00, 0x3f, 0x00, 0x2a, 0x3f, 0x2a, 0x00, 0x3f, 0x2a, 0x2a,
        0x15, 0x00, 0x15, 0x15, 0x00, 0x3f, 0x15, 0x2a, 0x15, 0x15, 0x2a, 0x3f,
        0x3f, 0x00, 0x15, 0x3f, 0x00, 0x3f, 0x3f, 0x2a, 0x15, 0x3f, 0x2a, 0x3f,
        0x15, 0x15, 0x00, 0x15, 0x15, 0x2a, 0x15, 0x3f, 0x00, 0x15, 0x3f, 0x2a,
        0x3f, 0x15, 0x00, 0x3f, 0x15, 0x2a, 0x3f, 0x3f, 0x00, 0x3f, 0x3f, 0x2a,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f
    };
    private static readonly byte[] palette3 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x00, 0x00, 0x2a, 0x2a,
        0x2a, 0x00, 0x00, 0x2a, 0x00, 0x2a, 0x2a, 0x15, 0x00, 0x2a, 0x2a, 0x2a,
        0x15, 0x15, 0x15, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x15, 0x15, 0x3f, 0x3f,
        0x3f, 0x15, 0x15, 0x3f, 0x15, 0x3f, 0x3f, 0x3f, 0x15, 0x3f, 0x3f, 0x3f,
        0x00, 0x00, 0x00, 0x05, 0x05, 0x05, 0x08, 0x08, 0x08, 0x0b, 0x0b, 0x0b,
        0x0e, 0x0e, 0x0e, 0x11, 0x11, 0x11, 0x14, 0x14, 0x14, 0x18, 0x18, 0x18,
        0x1c, 0x1c, 0x1c, 0x20, 0x20, 0x20, 0x24, 0x24, 0x24, 0x28, 0x28, 0x28,
        0x2d, 0x2d, 0x2d, 0x32, 0x32, 0x32, 0x38, 0x38, 0x38, 0x3f, 0x3f, 0x3f,
        0x00, 0x00, 0x3f, 0x10, 0x00, 0x3f, 0x1f, 0x00, 0x3f, 0x2f, 0x00, 0x3f,
        0x3f, 0x00, 0x3f, 0x3f, 0x00, 0x2f, 0x3f, 0x00, 0x1f, 0x3f, 0x00, 0x10,
        0x3f, 0x00, 0x00, 0x3f, 0x10, 0x00, 0x3f, 0x1f, 0x00, 0x3f, 0x2f, 0x00,
        0x3f, 0x3f, 0x00, 0x2f, 0x3f, 0x00, 0x1f, 0x3f, 0x00, 0x10, 0x3f, 0x00,
        0x00, 0x3f, 0x00, 0x00, 0x3f, 0x10, 0x00, 0x3f, 0x1f, 0x00, 0x3f, 0x2f,
        0x00, 0x3f, 0x3f, 0x00, 0x2f, 0x3f, 0x00, 0x1f, 0x3f, 0x00, 0x10, 0x3f,
        0x1f, 0x1f, 0x3f, 0x27, 0x1f, 0x3f, 0x2f, 0x1f, 0x3f, 0x37, 0x1f, 0x3f,
        0x3f, 0x1f, 0x3f, 0x3f, 0x1f, 0x37, 0x3f, 0x1f, 0x2f, 0x3f, 0x1f, 0x27,

        0x3f, 0x1f, 0x1f, 0x3f, 0x27, 0x1f, 0x3f, 0x2f, 0x1f, 0x3f, 0x37, 0x1f,
        0x3f, 0x3f, 0x1f, 0x37, 0x3f, 0x1f, 0x2f, 0x3f, 0x1f, 0x27, 0x3f, 0x1f,
        0x1f, 0x3f, 0x1f, 0x1f, 0x3f, 0x27, 0x1f, 0x3f, 0x2f, 0x1f, 0x3f, 0x37,
        0x1f, 0x3f, 0x3f, 0x1f, 0x37, 0x3f, 0x1f, 0x2f, 0x3f, 0x1f, 0x27, 0x3f,
        0x2d, 0x2d, 0x3f, 0x31, 0x2d, 0x3f, 0x36, 0x2d, 0x3f, 0x3a, 0x2d, 0x3f,
        0x3f, 0x2d, 0x3f, 0x3f, 0x2d, 0x3a, 0x3f, 0x2d, 0x36, 0x3f, 0x2d, 0x31,
        0x3f, 0x2d, 0x2d, 0x3f, 0x31, 0x2d, 0x3f, 0x36, 0x2d, 0x3f, 0x3a, 0x2d,
        0x3f, 0x3f, 0x2d, 0x3a, 0x3f, 0x2d, 0x36, 0x3f, 0x2d, 0x31, 0x3f, 0x2d,
        0x2d, 0x3f, 0x2d, 0x2d, 0x3f, 0x31, 0x2d, 0x3f, 0x36, 0x2d, 0x3f, 0x3a,
        0x2d, 0x3f, 0x3f, 0x2d, 0x3a, 0x3f, 0x2d, 0x36, 0x3f, 0x2d, 0x31, 0x3f,
        0x00, 0x00, 0x1c, 0x07, 0x00, 0x1c, 0x0e, 0x00, 0x1c, 0x15, 0x00, 0x1c,
        0x1c, 0x00, 0x1c, 0x1c, 0x00, 0x15, 0x1c, 0x00, 0x0e, 0x1c, 0x00, 0x07,
        0x1c, 0x00, 0x00, 0x1c, 0x07, 0x00, 0x1c, 0x0e, 0x00, 0x1c, 0x15, 0x00,
        0x1c, 0x1c, 0x00, 0x15, 0x1c, 0x00, 0x0e, 0x1c, 0x00, 0x07, 0x1c, 0x00,
        0x00, 0x1c, 0x00, 0x00, 0x1c, 0x07, 0x00, 0x1c, 0x0e, 0x00, 0x1c, 0x15,
        0x00, 0x1c, 0x1c, 0x00, 0x15, 0x1c, 0x00, 0x0e, 0x1c, 0x00, 0x07, 0x1c,

        0x0e, 0x0e, 0x1c, 0x11, 0x0e, 0x1c, 0x15, 0x0e, 0x1c, 0x18, 0x0e, 0x1c,
        0x1c, 0x0e, 0x1c, 0x1c, 0x0e, 0x18, 0x1c, 0x0e, 0x15, 0x1c, 0x0e, 0x11,
        0x1c, 0x0e, 0x0e, 0x1c, 0x11, 0x0e, 0x1c, 0x15, 0x0e, 0x1c, 0x18, 0x0e,
        0x1c, 0x1c, 0x0e, 0x18, 0x1c, 0x0e, 0x15, 0x1c, 0x0e, 0x11, 0x1c, 0x0e,
        0x0e, 0x1c, 0x0e, 0x0e, 0x1c, 0x11, 0x0e, 0x1c, 0x15, 0x0e, 0x1c, 0x18,
        0x0e, 0x1c, 0x1c, 0x0e, 0x18, 0x1c, 0x0e, 0x15, 0x1c, 0x0e, 0x11, 0x1c,
        0x14, 0x14, 0x1c, 0x16, 0x14, 0x1c, 0x18, 0x14, 0x1c, 0x1a, 0x14, 0x1c,
        0x1c, 0x14, 0x1c, 0x1c, 0x14, 0x1a, 0x1c, 0x14, 0x18, 0x1c, 0x14, 0x16,
        0x1c, 0x14, 0x14, 0x1c, 0x16, 0x14, 0x1c, 0x18, 0x14, 0x1c, 0x1a, 0x14,
        0x1c, 0x1c, 0x14, 0x1a, 0x1c, 0x14, 0x18, 0x1c, 0x14, 0x16, 0x1c, 0x14,
        0x14, 0x1c, 0x14, 0x14, 0x1c, 0x16, 0x14, 0x1c, 0x18, 0x14, 0x1c, 0x1a,
        0x14, 0x1c, 0x1c, 0x14, 0x1a, 0x1c, 0x14, 0x18, 0x1c, 0x14, 0x16, 0x1c,
        0x00, 0x00, 0x10, 0x04, 0x00, 0x10, 0x08, 0x00, 0x10, 0x0c, 0x00, 0x10,
        0x10, 0x00, 0x10, 0x10, 0x00, 0x0c, 0x10, 0x00, 0x08, 0x10, 0x00, 0x04,
        0x10, 0x00, 0x00, 0x10, 0x04, 0x00, 0x10, 0x08, 0x00, 0x10, 0x0c, 0x00,
        0x10, 0x10, 0x00, 0x0c, 0x10, 0x00, 0x08, 0x10, 0x00, 0x04, 0x10, 0x00,

        0x00, 0x10, 0x00, 0x00, 0x10, 0x04, 0x00, 0x10, 0x08, 0x00, 0x10, 0x0c,
        0x00, 0x10, 0x10, 0x00, 0x0c, 0x10, 0x00, 0x08, 0x10, 0x00, 0x04, 0x10,
        0x08, 0x08, 0x10, 0x0a, 0x08, 0x10, 0x0c, 0x08, 0x10, 0x0e, 0x08, 0x10,
        0x10, 0x08, 0x10, 0x10, 0x08, 0x0e, 0x10, 0x08, 0x0c, 0x10, 0x08, 0x0a,
        0x10, 0x08, 0x08, 0x10, 0x0a, 0x08, 0x10, 0x0c, 0x08, 0x10, 0x0e, 0x08,
        0x10, 0x10, 0x08, 0x0e, 0x10, 0x08, 0x0c, 0x10, 0x08, 0x0a, 0x10, 0x08,
        0x08, 0x10, 0x08, 0x08, 0x10, 0x0a, 0x08, 0x10, 0x0c, 0x08, 0x10, 0x0e,
        0x08, 0x10, 0x10, 0x08, 0x0e, 0x10, 0x08, 0x0c, 0x10, 0x08, 0x0a, 0x10,
        0x0b, 0x0b, 0x10, 0x0c, 0x0b, 0x10, 0x0d, 0x0b, 0x10, 0x0f, 0x0b, 0x10,
        0x10, 0x0b, 0x10, 0x10, 0x0b, 0x0f, 0x10, 0x0b, 0x0d, 0x10, 0x0b, 0x0c,
        0x10, 0x0b, 0x0b, 0x10, 0x0c, 0x0b, 0x10, 0x0d, 0x0b, 0x10, 0x0f, 0x0b,
        0x10, 0x10, 0x0b, 0x0f, 0x10, 0x0b, 0x0d, 0x10, 0x0b, 0x0c, 0x10, 0x0b,
        0x0b, 0x10, 0x0b, 0x0b, 0x10, 0x0c, 0x0b, 0x10, 0x0d, 0x0b, 0x10, 0x0f,
        0x0b, 0x10, 0x10, 0x0b, 0x0f, 0x10, 0x0b, 0x0d, 0x10, 0x0b, 0x0c, 0x10,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] sequ_01 = {0x08, 0x03, 0x00, 0x02};
    private static readonly byte[] crtc_01 = {
        0x2d, 0x27, 0x28, 0x90, 0x2b, 0xa0, 0xbf, 0x1f,
        0x00, 0x4f, 0x0d, 0x0e, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x14, 0x1f, 0x96, 0xb9, 0xa3,
        0xff
    };
    private static readonly byte[] actl_01 = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x14, 0x07,
        0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f,
        0x0c, 0x00, 0x0f, 0x08
    };
    private static readonly byte[] grdc_01 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x0e, 0x0f, 0xff
    };
    private static readonly byte[] sequ_03 = {0x00, 0x03, 0x00, 0x02};
    private static readonly byte[] crtc_03 = {
        0x5f, 0x4f, 0x50, 0x82, 0x55, 0x81, 0xbf, 0x1f,
        0x00, 0x4f, 0x0d, 0x0e, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x28, 0x1f, 0x96, 0xb9, 0xa3,
        0xff
    };
    private static readonly byte[] sequ_04 = {0x09, 0x03, 0x00, 0x02};
    private static readonly byte[] crtc_04 = {
        0x2d, 0x27, 0x28, 0x90, 0x2b, 0x80, 0xbf, 0x1f,
        0x00, 0xc1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x14, 0x00, 0x96, 0xb9, 0xa2,
        0xff
    };
    private static readonly byte[] actl_04 = {
        0x00, 0x13, 0x15, 0x17, 0x02, 0x04, 0x06, 0x07,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x01, 0x00, 0x03, 0x00
    };
    private static readonly byte[] grdc_04 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x30, 0x0f, 0x0f, 0xff
    };
    private static readonly byte[] sequ_06 = {0x01, 0x01, 0x00, 0x06};
    private static readonly byte[] crtc_06 = {
        0x5f, 0x4f, 0x50, 0x82, 0x54, 0x80, 0xbf, 0x1f,
        0x00, 0xc1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x28, 0x00, 0x96, 0xb9, 0xc2,
        0xff
    };
    private static readonly byte[] actl_06 = {
        0x00, 0x17, 0x17, 0x17, 0x17, 0x17, 0x17, 0x17,
        0x17, 0x17, 0x17, 0x17, 0x17, 0x17, 0x17, 0x17,
        0x01, 0x00, 0x01, 0x00
    };
    private static readonly byte[] grdc_06 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0d, 0x0f, 0xff
    };
    private static readonly byte[] crtc_07 = {
        0x5f, 0x4f, 0x50, 0x82, 0x55, 0x81, 0xbf, 0x1f,
        0x00, 0x4f, 0x0d, 0x0e, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x28, 0x0f, 0x96, 0xb9, 0xa3,
        0xff
    };
    private static readonly byte[] actl_07 = {
        0x00, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08,
        0x10, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18,
        0x0e, 0x00, 0x0f, 0x08
    };
    private static readonly byte[] grdc_07 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x0a, 0x0f, 0xff
    };
    private static readonly byte[] sequ_0d = {0x09, 0x0f, 0x00, 0x06};
    private static readonly byte[] crtc_0d = {
        0x2d, 0x27, 0x28, 0x90, 0x2b, 0x80, 0xbf, 0x1f,
        0x00, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x14, 0x00, 0x96, 0xb9, 0xe3,
        0xff
    };
    private static readonly byte[] actl_0d = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x01, 0x00, 0x0f, 0x00
    };
    private static readonly byte[] grdc_0d = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x0f, 0xff
    };
    private static readonly byte[] sequ_0e = {0x01, 0x0f, 0x00, 0x06};
    private static readonly byte[] crtc_0e = {
        0x5f, 0x4f, 0x50, 0x82, 0x54, 0x80, 0xbf, 0x1f,
        0x00, 0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x28, 0x00, 0x96, 0xb9, 0xe3,
        0xff
    };
    private static readonly byte[] crtc_0f = {
        0x5f, 0x4f, 0x50, 0x82, 0x54, 0x80, 0xbf, 0x1f,
        0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x83, 0x85, 0x5d, 0x28, 0x0f, 0x63, 0xba, 0xe3,
        0xff
    };
    private static readonly byte[] actl_0f = {
        0x00, 0x08, 0x00, 0x00, 0x18, 0x18, 0x00, 0x00,
        0x00, 0x08, 0x00, 0x00, 0x00, 0x18, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00
    };
    private static readonly byte[] actl_10 = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x14, 0x07,
        0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f,
        0x01, 0x00, 0x0f, 0x00
    };
    private static readonly byte[] crtc_11 = {
        0x5f, 0x4f, 0x50, 0x82, 0x54, 0x80, 0x0b, 0x3e,
        0x00, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xea, 0x8c, 0xdf, 0x28, 0x00, 0xe7, 0x04, 0xe3,
        0xff
    };
    private static readonly byte[] actl_11 = {
        0x00, 0x3f, 0x00, 0x3f, 0x00, 0x3f, 0x00, 0x3f,
        0x00, 0x3f, 0x00, 0x3f, 0x00, 0x3f, 0x00, 0x3f,
        0x01, 0x00, 0x0f, 0x00
    };
    private static readonly byte[] sequ_13 = {0x01, 0x0f, 0x00, 0x0e};
    private static readonly byte[] crtc_13 = {
        0x5f, 0x4f, 0x50, 0x82, 0x54, 0x80, 0xbf, 0x1f,
        0x00, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x9c, 0x8e, 0x8f, 0x28, 0x40, 0x96, 0xb9, 0xa3,
        0xff
    };
    private static readonly byte[] actl_13 = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
        0x41, 0x00, 0x0f, 0x00
    };
    private static readonly byte[] grdc_13 = {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x05, 0x0f, 0xff
    };
    private static readonly byte[] crtc_6A = {
        0x7f, 0x63, 0x63, 0x83, 0x6b, 0x1b, 0x72, 0xf0,
        0x00, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x59, 0x8d, 0x57, 0x32, 0x00, 0x57, 0x73, 0xe3,
        0xff
    };


    private static readonly StandardVgaMode[] VgaModes = {
        new(0x00, new VgaMode(MemoryModel.TEXT, 40, 25, 4, 9, 16, MemorySegment.CTEXT), 0xFF, palette2, sequ_01, 0x67, crtc_01, actl_01, grdc_01),
        new(0x01, new VgaMode(MemoryModel.TEXT, 40, 25, 4, 9, 16, MemorySegment.CTEXT), 0xFF, palette2, sequ_01, 0x67, crtc_01, actl_01, grdc_01),
        new(0x02, new VgaMode(MemoryModel.TEXT, 80, 25, 4, 9, 16, MemorySegment.CTEXT), 0xFF, palette2, sequ_03, 0x67, crtc_03, actl_01, grdc_01),
        new(0x03, new VgaMode(MemoryModel.TEXT, 80, 25, 4, 9, 16, MemorySegment.CTEXT), 0xFF, palette2, sequ_03, 0x67, crtc_03, actl_01, grdc_01),
        new(0x04, new VgaMode(MemoryModel.CGA, 320, 200, 2, 8, 8, MemorySegment.CTEXT), 0xFF, palette1, sequ_04, 0x63, crtc_04, actl_04, grdc_04),
        new(0x05, new VgaMode(MemoryModel.CGA, 320, 200, 2, 8, 8, MemorySegment.CTEXT), 0xFF, palette1, sequ_04, 0x63, crtc_04, actl_04, grdc_04),
        new(0x06, new VgaMode(MemoryModel.CGA, 640, 200, 1, 8, 8, MemorySegment.CTEXT), 0xFF, palette1, sequ_06, 0x63, crtc_06, actl_06, grdc_06),
        new(0x07, new VgaMode(MemoryModel.TEXT, 80, 25, 4, 9, 16, MemorySegment.MTEXT), 0xFF, palette0, sequ_03, 0x66, crtc_07, actl_07, grdc_07),
        new(0x0D, new VgaMode(MemoryModel.PLANAR, 320, 200, 4, 8, 8, MemorySegment.GRAPH), 0xFF, palette1, sequ_0d, 0x63, crtc_0d, actl_0d, grdc_0d),
        new(0x0E, new VgaMode(MemoryModel.PLANAR, 640, 200, 4, 8, 8, MemorySegment.GRAPH), 0xFF, palette1, sequ_0e, 0x63, crtc_0e, actl_0d, grdc_0d),
        new(0x0F, new VgaMode(MemoryModel.PLANAR, 640, 350, 1, 8, 14, MemorySegment.GRAPH), 0xFF, palette0, sequ_0e, 0xa3, crtc_0f, actl_0f, grdc_0d),
        new(0x10, new VgaMode(MemoryModel.PLANAR, 640, 350, 4, 8, 14, MemorySegment.GRAPH), 0xFF, palette2, sequ_0e, 0xa3, crtc_0f, actl_10, grdc_0d),
        new(0x11, new VgaMode(MemoryModel.PLANAR, 640, 480, 1, 8, 16, MemorySegment.GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_11, actl_11, grdc_0d),
        new(0x12, new VgaMode(MemoryModel.PLANAR, 640, 480, 4, 8, 16, MemorySegment.GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_11, actl_10, grdc_0d),
        new(0x13, new VgaMode(MemoryModel.PACKED, 320, 200, 8, 8, 8, MemorySegment.GRAPH), 0xFF, palette3, sequ_13, 0x63, crtc_13, actl_13, grdc_13),
        new(0x6A, new VgaMode(MemoryModel.PLANAR, 800, 600, 4, 8, 16, MemorySegment.GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_6A, actl_10, grdc_0d)
    };
    private readonly ILogger _logger;
    private readonly VgaRom _vgaRom;

    public VgaBios(Machine machine, ILogger logger) : base(machine) {
        _vgaRom = machine.VgaRom;
        _logger = logger;
        FillDispatchTable();

        stdvga_setup();
        init_bios_area();
    }

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;
    private void init_bios_area() {
        // init detected hardware BIOS Area
        // set 80x25 color (not clear from RBIL but usual)
        set_equipment_flags(0x30, 0x20);

        // Set the basic modeset options
        _machine.Bios.VideoDisplayData = 0x51;
        _machine.Bios.DisplayCombinationCode = 0x08;
    }

    private void stdvga_setup() {
        // switch to color mode and enable CPU access 480 lines
        stdvga_misc_write(0xc3);
        // more than 64k 3C4/04
        stdvga_sequ_write(0x04, 0x02);
    }

    private void set_equipment_flags(int clear, int set) {
        _machine.Bios.EquipmentListFlags = (ushort)(_machine.Bios.EquipmentListFlags & ~clear | set);
    }

    /// <summary>
    ///     Runs the specified video BIOS function.
    /// </summary>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x00, new Callback(0x00, SetMode));
        _dispatchTable.Add(0x01, new Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x03, new Callback(0x03, GetCursorPosition));
        _dispatchTable.Add(0x04, new Callback(0x04, ReadLightPenPosition));
        _dispatchTable.Add(0x05, new Callback(0x05, SelectActiveDisplayPage));
        _dispatchTable.Add(0x06, new Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x07, new Callback(0x07, ScrollPageDown));
        _dispatchTable.Add(0x08, new Callback(0x08, ReadCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x09, new Callback(0x09, WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(0x0A, new Callback(0x0A, WriteCharacterAtCursor));
        _dispatchTable.Add(0x0B, new Callback(0x0B, SetColorPaletteOrBackGroundColor));
        _dispatchTable.Add(0x0C, new Callback(0x0C, WriteDot));
        _dispatchTable.Add(0x0D, new Callback(0x0D, ReadDot));
        _dispatchTable.Add(0x0E, new Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, GetVideoState));
        _dispatchTable.Add(0x10, new Callback(0x10, SetPaletteRegisters));
        _dispatchTable.Add(0x11, new Callback(0x11, LoadFontInfo));
        _dispatchTable.Add(0x12, new Callback(0x12, VideoSubsystemConfiguration));
        _dispatchTable.Add(0x13, new Callback(0x13, WriteString));
        _dispatchTable.Add(0x1A, new Callback(0x1A, GetSetDisplayCombinationCode));
        _dispatchTable.Add(0x1B, new Callback(0x1B, () => GetFunctionalityInfo()));
    }
    private void ReadDot() {
        throw new NotImplementedException();
    }
    private void WriteDot() {
        throw new NotImplementedException();
    }
    private void ReadLightPenPosition() {
        throw new NotImplementedException();
    }
    private void SetMode() {
        int mode = _state.AL & 0x7f;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_machine.Bios.VideoDisplayData & (ModeFlags.NoPalette | ModeFlags.GraySum);
        if ((_state.AL & 0x80) != 0) {
            flags |= ModeFlags.NoClearMem;
        }

        // Set AL
        if (mode > 7) {
            _state.AL = 0x20;
        } else if (mode == 6) {
            _state.AL = 0x3f;
        } else {
            _state.AL = 0x30;
        }

        VgaSetMode(mode, flags);
    }
    private VBEReturnStatus VgaSetMode(int mode, ModeFlags flags) {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Set VGA mode 0x{Mode:X2}", mode);
        }

        StandardVgaMode stdMode = VgaHwFindMode(mode);

        VgahwSetMode(stdMode, flags);
        VgaMode vgaMode = stdMode.Info;

        // Set the BIOS mem
        ushort width = vgaMode.Width;
        ushort height = vgaMode.Height;
        MemoryModel memmodel = vgaMode.MemoryModel;
        ushort cheight = vgaMode.CharacterHeight;
        if (mode < 0x100) {
            _machine.Bios.VideoMode = (byte)mode;
        } else {
            _machine.Bios.VideoMode = 0xff;
        }
        // SET_BDA_EXT(vbe_mode, mode | (flags & MF_VBEFLAGS));
        // SET_BDA_EXT(vgamode_offset, (u32)vgaMode);
        // if (CONFIG_VGA_ALLOCATE_EXTRA_STACK)
        //     // Disable extra stack if it appears a modern OS is in use.
        //     // This works around bugs in some versions of Windows (Vista
        //     // and possibly later) when the stack is in the e-segment.
        //     MASK_BDA_EXT(flags, BF_EXTRA_STACK
        //         , (flags & MF_LEGACY) ? BF_EXTRA_STACK : 0);
        if (memmodel == MemoryModel.TEXT) {
            _machine.Bios.ScreenColumns = (byte)width;
            _machine.Bios.ScreenRows = (byte)(height - 1);
            _machine.Bios.CursorType = 0x0607;
        } else {
            _machine.Bios.ScreenColumns = (byte)(width / vgaMode.CharacterWidth);
            _machine.Bios.ScreenRows = (byte)(height / vgaMode.CharacterHeight - 1);
            _machine.Bios.CursorType = 0x0000;
        }
        _machine.Bios.VideoPageSize = calc_page_size(memmodel, width, height);
        _machine.Bios.CrtControllerBaseAddress = (ushort)stdvga_get_crtc();
        _machine.Bios.CharacterPointHeight = cheight;
        _machine.Bios.VideoCtl = (byte)(0x60 | (flags.HasFlag(ModeFlags.NoClearMem) ? 0x80 : 0x00));
        _machine.Bios.VideoModeOptions = 0xF9;
        _machine.Bios.VideoDisplayData &= 0x7F;
        for (int i = 0; i < 8; i++) {
            _machine.Bios.CursorPosition[i] = 0x0000;
        }
        _machine.Bios.VideoPageStart = 0x0000;
        _machine.Bios.CurrentVideoPage = 0x00;

        // Set the ints 0x1F and 0x43
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x1F] = _vgaRom.VgaFont8Address2.Offset;
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x1F + 2] = _vgaRom.VgaFont8Address2.Segment;

        SegmentedAddress address;
        switch (cheight) {
            case 8:
                address = _vgaRom.VgaFont8Address;
                break;
            case 14:
                address = _vgaRom.VgaFont14Address;
                break;
            case 16:
                address = _vgaRom.VgaFont16Address;
                break;
            default:
                return 0;
        }
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43] = address.Offset;
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43 + 2] = address.Segment;

        return 0;
    }
    private VGAREG stdvga_get_crtc() {
        if ((stdvga_misc_read() & 1) != 0) {
            return VGAREG.VGA_CRTC_ADDRESS;
        }
        return VGAREG.MDA_CRTC_ADDRESS;
    }
    private ushort calc_page_size(MemoryModel memmodel, int width, int height) {
        int result = memmodel switch {
            MemoryModel.TEXT => ALIGN(width * height * 2, 2 * 1024),
            MemoryModel.CGA => 16 * 1024,
            _ => ALIGN(width * height / 8, 8 * 1024)
        };
        return (ushort)result;
    }

    private int ALIGN(int alignment, int value) {
        int mask = alignment - 1;
        return value + mask & ~mask;
    }

    private void VgahwSetMode(StandardVgaMode stdmode_g, ModeFlags flags) {
        VgaMode vmode_g = stdmode_g.Info;

        // if palette loading (bit 3 of modeset ctl = 0)
        if (!flags.HasFlag(ModeFlags.NoPalette)) {
            // Set the PEL mask
            stdvga_pelmask_write(stdmode_g.Pelmask);

            // From which palette
            byte[] palette = stdmode_g.Dac;
            byte palsize = (byte)(stdmode_g.Dacsize / 3);

            // Always 256*3 values
            stdvga_dac_write(palette, 0, palsize);
            byte[] empty = new byte[3];
            for (byte i = palsize; i < byte.MaxValue; i++) {
                stdvga_dac_write(empty, i, 1);
            }

            if (flags.HasFlag(ModeFlags.GraySum)) {
                stdvga_perform_gray_scale_summing(0x00, 0x100);
            }
        }

        // Set Attribute Ctl
        for (byte i = 0; i <= 0x13; i++) {
            stdvga_attr_write(i, stdmode_g.ActlRegs[i]);
        }
        stdvga_attr_write(0x14, 0x00);

        // Set Sequencer Ctl
        stdvga_sequ_write(0x00, 0x03);
        for (byte i = 1; i <= 4; i++) {
            stdvga_sequ_write(i, stdmode_g.SequRegs[i - 1]);
        }

        // Set Grafx Ctl
        for (byte i = 0; i <= 8; i++) {
            stdvga_grdc_write(i, stdmode_g.GrdcRegs[i]);
        }

        // Set CRTC address VGA or MDA
        byte miscreg = stdmode_g.Miscreg;
        VGAREG crtc_addr = VGAREG.VGA_CRTC_ADDRESS;
        if ((miscreg & 1) == 0) {
            crtc_addr = VGAREG.MDA_CRTC_ADDRESS;
        }

        // Disable CRTC write protection
        stdvga_crtc_write(crtc_addr, 0x11, 0x00);
        // Set CRTC regs
        for (byte i = 0; i <= 0x18; i++) {
            stdvga_crtc_write(crtc_addr, i, stdmode_g.CrtcRegs[i]);
        }

        // Set the misc register
        stdvga_misc_write(miscreg);

        // Enable video
        stdvga_attrindex_write(0x20);

        // Clear screen
        if (!flags.HasFlag(ModeFlags.NoClearMem)) {
            clear_screen(vmode_g);
        }

        // Write the fonts in memory
        if (vmode_g.MemoryModel == MemoryModel.TEXT) {
            stdvga_load_font(VgaRom.vgafont16, 0x100, 0, 0, 16);
        }

    }
    private void stdvga_load_font(byte[] fontBytes, ushort count, ushort start, byte destFlags, byte fontSize) {
        get_font_access();
        ushort blockaddr = (ushort)(((destFlags & 0x03) << 14) + ((destFlags & 0x04) << 11));
        ushort dest_far = (ushort)(blockaddr + start * 32);
        for (ushort i = 0; i < count; i++) {
            ushort address = (ushort)MemoryUtils.ToPhysicalAddress((ushort)MemorySegment.GRAPH, (ushort)(dest_far + i * 32));
            var value = new Span<byte>(fontBytes, i * fontSize, fontSize);
            _machine.Memory.LoadData(address, value.ToArray(), fontSize);
        }
        release_font_access();
    }
    private void release_font_access() {
        stdvga_sequ_write(0x00, 0x01);
        stdvga_sequ_write(0x02, 0x03);
        stdvga_sequ_write(0x04, 0x03);
        stdvga_sequ_write(0x00, 0x03);
        byte value = (byte)((stdvga_misc_read() & 0x01) != 0 ? 0x0e : 0x0a);
        stdvga_grdc_write(0x06, value);
        stdvga_grdc_write(0x04, 0x00);
        stdvga_grdc_write(0x05, 0x10);
    }
    private byte stdvga_misc_read() {
        return inb(VGAREG.READ_MISC_OUTPUT);
    }

    private void get_font_access() {
        stdvga_sequ_write(0x00, 0x01);
        stdvga_sequ_write(0x02, 0x04);
        stdvga_sequ_write(0x04, 0x07);
        stdvga_sequ_write(0x00, 0x03);
        stdvga_grdc_write(0x04, 0x02);
        stdvga_grdc_write(0x05, 0x00);
        stdvga_grdc_write(0x06, 0x04);
    }

    private void clear_screen(VgaMode vmode_g) {
        switch (vmode_g.MemoryModel) {
            case MemoryModel.TEXT:
                memset16_far(vmode_g.Segment, 0, 0x0720, 32 * 1024);
                break;
            case MemoryModel.CGA:
                memset16_far(vmode_g.Segment, 0, 0x0000, 32 * 1024);
                break;
            default:
                memset16_far(vmode_g.Segment, 0, 0x0000, 64 * 1024);
                break;
        }
    }
    private void memset16_far(MemorySegment segment, ushort start, ushort value, int sizeInBytes) {
        uint address = MemoryUtils.ToPhysicalAddress((ushort)segment, start);
        for (int i = 0; i < sizeInBytes >> 1; i++) {
            _machine.Memory.SetUint16((uint)(address + i), value);
        }
    }
    private void stdvga_misc_write(byte value) {
        outb(value, VGAREG.WRITE_MISC_OUTPUT);
    }
    private void stdvga_crtc_write(VGAREG crtcAddr, byte index, byte value) {
        outw((ushort)(value << 8 | index), crtcAddr);
    }
    private void stdvga_grdc_write(byte index, byte value) {
        outw((ushort)(value << 8 | index), VGAREG.GRDC_ADDRESS);
    }
    private void stdvga_sequ_write(byte index, byte value) {
        outw((ushort)(value << 8 | index), VGAREG.SEQU_ADDRESS);
    }
    private void outw(ushort value, VGAREG port) {
        _machine.IoPortDispatcher.WriteWord((int)port, value);
    }

    private void stdvga_attr_write(byte index, byte value) {
        inb(VGAREG.ACTL_RESET);
        byte orig = inb(VGAREG.ACTL_ADDRESS);
        outb(index, VGAREG.ACTL_ADDRESS);
        outb(value, VGAREG.ACTL_WRITE_DATA);
        outb(orig, VGAREG.ACTL_ADDRESS);
    }

    private void stdvga_perform_gray_scale_summing(byte start, int count) {
        stdvga_attrindex_write(0x00);
        for (byte i = start; i < start + count; i++) {
            byte[] rgb = stdvga_dac_read(i, 1);

            // intensity = ( 0.3 * Red ) + ( 0.59 * Green ) + ( 0.11 * Blue )
            ushort intensity = (ushort)(77 * rgb[0] + 151 * rgb[1] + 28 * rgb[2] + 0x80 >> 8);
            if (intensity > 0x3f) {
                intensity = 0x3f;
            }
            rgb[0] = rgb[1] = rgb[2] = (byte)intensity;

            stdvga_dac_write(rgb, i, 1);
        }
        stdvga_attrindex_write(0x20);
    }

    private byte[] stdvga_dac_read(byte start, int count) {
        byte[] result = new byte[3 * count];
        outb(start, VGAREG.DAC_READ_ADDRESS);
        for (int i = 0; i < result.Length; i++) {
            result[i] = inb(VGAREG.DAC_DATA);
        }
        return result;
    }

    private void stdvga_attrindex_write(byte value) {
        inb(VGAREG.ACTL_RESET);
        outb(value, VGAREG.ACTL_ADDRESS);
    }

    private byte inb(VGAREG port) {
        return _machine.IoPortDispatcher.ReadByte((int)port);
    }

    private void stdvga_dac_write(IReadOnlyList<byte> palette, byte startIndex, ushort count) {
        outb(startIndex, VGAREG.DAC_WRITE_ADDRESS);
        int i = 0;
        while (count > 0) {
            outb(palette[i++], VGAREG.DAC_DATA);
            outb(palette[i++], VGAREG.DAC_DATA);
            outb(palette[i++], VGAREG.DAC_DATA);
            count--;
        }
    }
    private void outb(byte value, VGAREG port) {
        _machine.IoPortDispatcher.WriteByte((int)port, value);
    }

    private void stdvga_pelmask_write(byte value) {
        outb(value, VGAREG.PEL_MASK);
    }

    private static StandardVgaMode VgaHwFindMode(int mode) {
        foreach (StandardVgaMode standardVgaMode in VgaModes) {
            if (standardVgaMode.Mode == mode) {
                return standardVgaMode;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode");
    }
    public void WriteString() {
        throw new NotImplementedException();
    }
    public void SetVideoMode() {
        SetMode();
    }
    public VideoFunctionalityInfo GetFunctionalityInfo() {
        throw new NotImplementedException();
    }
    public void GetSetDisplayCombinationCode() {
        switch (_state.AL) {
            case 0x00: {
                GetDisplayCombinationCode();
                break;
            }
            case 0x01: {
                SetDisplayCombinationCode();
                break;
            }
            default: {
                throw new NotSupportedException($"AL=0x{_state.AL:X2} is not a valid subfunction for INT 10h AH=1Ah");
            }
        }
    }
    private void SetDisplayCombinationCode() {

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: Set display combination {0:X2}", _state.BL);
        }
        _state.AL = 0x1A; // Function supported
        _machine.Bios.DisplayCombinationCode = _state.BL;
    }
    private void GetDisplayCombinationCode() {

        _state.AL = 0x1A; // Function supported
        _state.BL = _machine.Bios.DisplayCombinationCode; // Primary display
        _state.BH = 0x00; // No secondary display
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: Get display combination {0:X2}", _state.BL);
        }
    }
    private void handle_101a00() {
        throw new NotImplementedException();
    }

    public void VideoSubsystemConfiguration() {
        throw new NotImplementedException();
    }
    public void LoadFontInfo() {
        switch (_state.AL) {
            case 0x30:
                GetFontInformation();
                break;

            default:
                throw new NotImplementedException($"Video command 11{_state.AL:X2}h not implemented.");
        }
    }
    private void GetFontInformation() {
        switch (_state.BH) {
            case 0x00: {
                _state.ES = _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x1F + 2];
                _state.BP = _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x1F];
                break;
            }
            case 0x01: {
                _state.ES = _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43 + 2];
                _state.BP = _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43];
                break;
            }
            case 0x02:
                _state.ES = _vgaRom.VgaFont14Address.Segment;
                _state.BP = _vgaRom.VgaFont14Address.Offset;
                break;
            case 0x03:
                _state.ES = _vgaRom.VgaFont8Address.Segment;
                _state.BP = _vgaRom.VgaFont8Address.Offset;
                break;
            case 0x04:
                _state.ES = _vgaRom.VgaFont8Address2.Segment;
                _state.BP = _vgaRom.VgaFont8Address2.Offset;
                break;
            case 0x05:
                // _state.es = get_global_seg();
                // _state.bp = (u32)vgafont14alt;
                _state.ES = _vgaRom.VgaFont14Address.Segment;
                _state.BP = _vgaRom.VgaFont14Address.Offset;
                break;
            case 0x06:
                _state.ES = _vgaRom.VgaFont16Address.Segment;
                _state.BP = _vgaRom.VgaFont16Address.Offset;
                break;
            case 0x07:
                // _state.es = get_global_seg();
                // _state.bp = (u32)vgafont16alt;
                _state.ES = _vgaRom.VgaFont16Address.Segment;
                _state.BP = _vgaRom.VgaFont16Address.Offset;
                break;
            default:
                throw new NotSupportedException($"{_state.BH} is not a valid font number");
        }
        // Set byte/char of on screen font
        _state.CX = (ushort)(_machine.Bios.CharacterPointHeight & 0xff);

        // Set Highest char row
        _state.DL = _machine.Bios.ScreenRows;
    }

    public void SetPaletteRegisters() {
        switch (_state.AL) {
            case 0x00:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("INT 10: SetSinglePaletteRegister {0:X2} {1:X2}", _state.BL, _state.BH);
                }
                SetEgaPaletteRegister(_state.BL, _state.BH);
                break;
            case 0x01:
                stdvga_set_overscan_border_color(_state.BH);
                break;
            case 0x02:
                stdvga_set_all_palette_reg(_state.ES, _state.DX);
                break;
            case 0x03:
                stdvga_toggle_intensity(_state.BL);
                break;
            case 0x07:
                if (_state.BL > 0x14)
                    return;
                _state.BH = stdvga_attr_read(_state.BL);
                break;
            case 0x08:
                _state.BH = stdvga_get_overscan_border_color();
                break;
            case 0x09:
                stdvga_get_all_palette_reg(_state.ES, _state.DX);
                break;
            case 0x10:
                stdvga_dac_write(new[] {_state.DH, _state.CH, _state.CL}, (byte)_state.BX, 1);
                break;
            case 0x12:
                stdvga_dac_write(_state.ES, _state.DX, (byte)_state.BX, _state.CX);
                break;
            case 0x13:
                stdvga_select_video_dac_color_page(_state.BL, _state.BH);
                break;
            case 0x15:
                byte[] rgb = stdvga_dac_read((byte)_state.BX, 1);
                _state.DH = rgb[0];
                _state.CH = rgb[1];
                _state.CL = rgb[2];
                break;
            case 0x17:
                stdvga_dac_read(_state.ES, _state.DX, (byte)_state.BX, _state.CX);
                break;
            case 0x18:
                stdvga_pelmask_write(_state.BL);
                break;
            case 0x19:
                _state.BL = stdvga_pelmask_read();
                break;
            case 0x1a:
                stdvga_read_video_dac_state(out byte pMode, out byte curPage);
                _state.BH = curPage;
                _state.BL = pMode;
                break;
            case 0x1b:
                stdvga_perform_gray_scale_summing((byte)_state.BX, _state.CX);
                break;
            default:
                throw new NotSupportedException($"0x{_state.AL:X2} is not a valid palette register subFunction");
        }
    }
    private void stdvga_read_video_dac_state(out byte pMode, out byte curPage) {
        byte val1 = (byte)(stdvga_attr_read(0x10) >> 7);
        byte val2 = (byte)(stdvga_attr_read(0x14) & 0x0f);
        if ((val1 & 0x01) == 0)
            val2 >>= 2;
        pMode = val1;
        curPage = val2;
    }
    private byte stdvga_pelmask_read() {
        return inb(VGAREG.PEL_MASK);
    }
    
    private void stdvga_dac_read(ushort segment, ushort offset, byte start, ushort count) {
        outb(start, VGAREG.DAC_READ_ADDRESS);
        while (count > 0) {
            _memory.UInt8[segment, offset++] = inb(VGAREG.DAC_DATA);
            _memory.UInt8[segment, offset++] = inb(VGAREG.DAC_DATA);
            _memory.UInt8[segment, offset++] = inb(VGAREG.DAC_DATA);
            count--;
        }
    }
    
    private void stdvga_select_video_dac_color_page(byte flag, byte data) {
        if ((flag & 0x01) == 0) {
            // select paging mode
            stdvga_attr_mask(0x10, 0x80, (byte)(data << 7));
            return;
        }
        // select page
        byte val = stdvga_attr_read(0x10);
        if ((val & 0x80) == 0)
            data <<= 2;
        data &= 0x0f;
        stdvga_attr_write(0x14, data);
    }
    private void stdvga_dac_write(ushort segment, ushort offset, byte startIndex, ushort count) {
        byte[] rgb = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), 3);
        stdvga_dac_write(rgb, startIndex, count);
    }

    private void stdvga_get_all_palette_reg(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            _memory.UInt8[segment, offset++] = stdvga_attr_read(i);
        }
        _memory.UInt8[segment, offset] = stdvga_attr_read(0x11);
    }
    private byte stdvga_get_overscan_border_color() {
        return stdvga_attr_read(0x11);
    }

    private byte stdvga_attr_read(byte index) {
        inb(VGAREG.ACTL_RESET);
        byte orig = inb(VGAREG.ACTL_ADDRESS);
        outb(index, VGAREG.ACTL_ADDRESS);
        byte v = inb(VGAREG.ACTL_READ_DATA);
        inb(VGAREG.ACTL_RESET);
        outb(orig, VGAREG.ACTL_ADDRESS);
        return v;
    }
    private void stdvga_toggle_intensity(byte flag) {
        stdvga_attr_mask(0x10, 0x08, (byte)((flag & 0x01) << 3));
    }
    private void stdvga_attr_mask(byte index, byte off, byte on) {
        inb(VGAREG.ACTL_RESET);
        byte orig = inb(VGAREG.ACTL_ADDRESS);
        outb(index, VGAREG.ACTL_ADDRESS);
        byte v = inb(VGAREG.ACTL_READ_DATA);
        outb((byte)((v & ~off) | on), VGAREG.ACTL_WRITE_DATA);
        outb(orig, VGAREG.ACTL_ADDRESS);
    }
    private void stdvga_set_all_palette_reg(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            stdvga_attr_write(i, _memory.UInt8[segment, offset++]);
        }
        stdvga_attr_write(0x11, _memory.UInt8[segment, offset]);
    }

    private void stdvga_set_overscan_border_color(byte color) {
        stdvga_attr_write(0x11, color);
    }

    private void SetEgaPaletteRegister(byte register, byte value) {
        if (register > 0x14)
            return;
        stdvga_attr_write(register, value);
    }

    public void GetVideoState() {
        throw new NotImplementedException();
    }
    public void WriteTextInTeletypeMode() {
        throw new NotImplementedException();
    }
    public void SetColorPaletteOrBackGroundColor() {
        throw new NotImplementedException();
    }
    public void WriteCharacterAtCursor() {
        throw new NotImplementedException();
    }
    public void WriteCharacterAndAttributeAtCursor() {
        throw new NotImplementedException();
    }
    public void ReadCharacterAndAttributeAtCursor() {
        throw new NotImplementedException();
    }
    public void ScrollPageDown() {
        throw new NotImplementedException();
    }
    public void ScrollPageUp() {
        throw new NotImplementedException();
    }
    public void SelectActiveDisplayPage() {
        throw new NotImplementedException();
    }
    public void GetCursorPosition() {
        throw new NotImplementedException();
    }
    public void SetCursorPosition() {
        throw new NotImplementedException();
    }
    public void SetCursorType() {
        throw new NotImplementedException();
    }
}

internal struct StandardVgaMode {
    public ushort Mode;
    public VgaMode Info;
    public byte Pelmask;
    public byte[] Dac;
    public ushort Dacsize;
    public byte[] SequRegs;
    public byte Miscreg;
    public byte[] CrtcRegs;
    public byte[] ActlRegs;
    public byte[] GrdcRegs;
    public StandardVgaMode(ushort mode, VgaMode info, byte pelmask, byte[] dac, byte[] sequRegs, byte miscreg, byte[] crtcRegs, byte[] actlRegs, byte[] grdcRegs) : this() {
        Mode = mode;
        Info = info;
        Pelmask = pelmask;
        Dac = dac;
        SequRegs = sequRegs;
        Miscreg = miscreg;
        CrtcRegs = crtcRegs;
        ActlRegs = actlRegs;
        GrdcRegs = grdcRegs;
    }
}

internal struct VgaMode {
    public MemoryModel MemoryModel;
    public ushort Width;
    public ushort Height;
    public byte BitDepth;
    public byte CharacterWidth;
    public byte CharacterHeight;
    public MemorySegment Segment;
    public VgaMode(MemoryModel memoryModel, ushort width, ushort height, byte bitDepth, byte characterWidth, byte characterHeight, MemorySegment segment) {
        MemoryModel = memoryModel;
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        CharacterWidth = characterWidth;
        CharacterHeight = characterHeight;
        Segment = segment;
    }
}

[Flags]
internal enum ModeFlags {
    // Mode flags
    Legacy = 0x0001,
    GraySum = 0x0002,
    NoPalette = 0x0008,
    CustomCrtc = 0x0800,
    LinearFb = 0x4000,
    NoClearMem = 0x8000,
    VbeFlags = 0xfe00
}

internal enum MemoryModel {
    TEXT,
    CGA,
    HERCULES,
    PLANAR,
    PACKED,
    NON_CHAIN_4_256,
    DIRECT,
    YUV
}

internal enum MemorySegment {
    GRAPH = 0xA000,
    CTEXT = 0xB800,
    MTEXT = 0xB000
}

internal enum VBEReturnStatus {
    // AL
    Supported = 0x4F,
    Unsupported = 0x00,
    // AH
    Successful = 0x00,
    Failed = 0x01,
    NotSupported = 0x02,
    Invalid = 0x03
}

internal enum VGAREG {
    // VGA registers
    ACTL_ADDRESS = 0x3c0,
    ACTL_WRITE_DATA = 0x3c0,
    ACTL_READ_DATA = 0x3c1,

    INPUT_STATUS = 0x3c2,
    WRITE_MISC_OUTPUT = 0x3c2,
    VIDEO_ENABLE = 0x3c3,
    SEQU_ADDRESS = 0x3c4,
    SEQU_DATA = 0x3c5,

    PEL_MASK = 0x3c6,
    DAC_STATE = 0x3c7,
    DAC_READ_ADDRESS = 0x3c7,
    DAC_WRITE_ADDRESS = 0x3c8,
    DAC_DATA = 0x3c9,

    READ_FEATURE_CTL = 0x3ca,
    READ_MISC_OUTPUT = 0x3cc,

    GRDC_ADDRESS = 0x3ce,
    GRDC_DATA = 0x3cf,

    MDA_CRTC_ADDRESS = 0x3b4,
    MDA_CRTC_DATA = 0x3b5,
    VGA_CRTC_ADDRESS = 0x3d4,
    VGA_CRTC_DATA = 0x3d5,

    MDA_WRITE_FEATURE_CTL = 0x3ba,
    VGA_WRITE_FEATURE_CTL = 0x3da,
    ACTL_RESET = 0x3da,

    MDA_MODECTL = 0x3b8,
    CGA_MODECTL = 0x3d8,
    CGA_PALETTE = 0x3d9
}