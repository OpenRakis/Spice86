namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog;
using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Runtime.InteropServices;

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


    private static readonly stdvga_mode_s[] VgaModes = {
        new(0x00, new vgamode_s(MM.TEXT, 40, 25, 4, 9, 16, SEG_CTEXT), 0xFF, palette2, sequ_01, 0x67, crtc_01, actl_01, grdc_01),
        new(0x01, new vgamode_s(MM.TEXT, 40, 25, 4, 9, 16, SEG_CTEXT), 0xFF, palette2, sequ_01, 0x67, crtc_01, actl_01, grdc_01),
        new(0x02, new vgamode_s(MM.TEXT, 80, 25, 4, 9, 16, SEG_CTEXT), 0xFF, palette2, sequ_03, 0x67, crtc_03, actl_01, grdc_01),
        new(0x03, new vgamode_s(MM.TEXT, 80, 25, 4, 9, 16, SEG_CTEXT), 0xFF, palette2, sequ_03, 0x67, crtc_03, actl_01, grdc_01),
        new(0x04, new vgamode_s(MM.CGA, 320, 200, 2, 8, 8, SEG_CTEXT), 0xFF, palette1, sequ_04, 0x63, crtc_04, actl_04, grdc_04),
        new(0x05, new vgamode_s(MM.CGA, 320, 200, 2, 8, 8, SEG_CTEXT), 0xFF, palette1, sequ_04, 0x63, crtc_04, actl_04, grdc_04),
        new(0x06, new vgamode_s(MM.CGA, 640, 200, 1, 8, 8, SEG_CTEXT), 0xFF, palette1, sequ_06, 0x63, crtc_06, actl_06, grdc_06),
        new(0x07, new vgamode_s(MM.TEXT, 80, 25, 4, 9, 16, SEG_MTEXT), 0xFF, palette0, sequ_03, 0x66, crtc_07, actl_07, grdc_07),
        new(0x0D, new vgamode_s(MM.PLANAR, 320, 200, 4, 8, 8, SEG_GRAPH), 0xFF, palette1, sequ_0d, 0x63, crtc_0d, actl_0d, grdc_0d),
        new(0x0E, new vgamode_s(MM.PLANAR, 640, 200, 4, 8, 8, SEG_GRAPH), 0xFF, palette1, sequ_0e, 0x63, crtc_0e, actl_0d, grdc_0d),
        new(0x0F, new vgamode_s(MM.PLANAR, 640, 350, 1, 8, 14, SEG_GRAPH), 0xFF, palette0, sequ_0e, 0xa3, crtc_0f, actl_0f, grdc_0d),
        new(0x10, new vgamode_s(MM.PLANAR, 640, 350, 4, 8, 14, SEG_GRAPH), 0xFF, palette2, sequ_0e, 0xa3, crtc_0f, actl_10, grdc_0d),
        new(0x11, new vgamode_s(MM.PLANAR, 640, 480, 1, 8, 16, SEG_GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_11, actl_11, grdc_0d),
        new(0x12, new vgamode_s(MM.PLANAR, 640, 480, 4, 8, 16, SEG_GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_11, actl_10, grdc_0d),
        new(0x13, new vgamode_s(MM.PACKED, 320, 200, 8, 8, 8, SEG_GRAPH), 0xFF, palette3, sequ_13, 0x63, crtc_13, actl_13, grdc_13),
        new(0x6A, new vgamode_s(MM.PLANAR, 800, 600, 4, 8, 16, SEG_GRAPH), 0xFF, palette2, sequ_0e, 0xe3, crtc_6A, actl_10, grdc_0d)
    };


    private const ushort SEG_GRAPH = 0xA000;
    private const ushort SEG_CTEXT = 0xB800;
    private const ushort SEG_MTEXT = 0xB000;

    private readonly ILogger _logger;
    private readonly VgaRom _vgaRom;
    private readonly Bios _bios;
    private stdvga_mode_s _currentMode;

    public VgaBios(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _bios = _machine.Bios;
        _vgaRom = machine.VgaRom;
        _logger = loggerService.WithLogLevel(LogEventLevel.Information);
        FillDispatchTable();

        stdvga_setup();
        init_bios_area();
    }

    /// <summary>
    ///     The interrupt vector this class handles.
    /// </summary>
    public override byte Index => 0x10;

    public void WriteString() {
        cursorpos cursorpos = new(_state.DL, _state.DH, _state.BH);
        ushort count = _state.CX;
        ushort offset = _state.BP;
        byte attr = _state.BL;
        while (count-- > 0) {
            char car = (char)_memory.UInt8[_state.ES, offset++];
            if ((_state.AL & 0x02) != 0) {
                attr = _memory.UInt8[_state.ES, offset++];
            }

            carattr ca = new(car, attr, true);
            write_teletype(cursorpos, ca);
        }

        if ((_state.AL & 0x01) != 0)
            set_cursor_pos(cursorpos);
    }

    private void set_cursor_pos(cursorpos cp) {
        if (cp.page > 7)
            // Should not happen...
            return;

        if (cp.page == _bios.CurrentVideoPage) {
            // Update cursor in hardware
            stdvga_set_cursor_pos(text_address(cp));
        }

        // Update BIOS cursor pos
        _bios.CursorPosition[cp.page] = (byte)(cp.y << 8 | cp.x);
    }

    private void stdvga_set_cursor_pos(ushort address) {
        VGAREG crtc_addr = stdvga_get_crtc();
        address /= 2; // Assume we're in text mode.
        stdvga_crtc_write(crtc_addr, 0x0e, (byte)(address >> 8));
        stdvga_crtc_write(crtc_addr, 0x0f, (byte)address);
    }

    private ushort text_address(cursorpos cp) {
        int stride = _bios.ScreenColumns * 2;
        int pageoffset = _bios.VideoPageSize * cp.page;
        return (ushort)(pageoffset + cp.y * stride + cp.x * 2);
    }

    public void write_teletype(cursorpos pcp, carattr ca) {
        switch (ca.car) {
            case (char)7:
                //FIXME should beep
                break;
            case (char)8:
                if (pcp.x > 0)
                    pcp.x--;
                break;
            case '\r':
                pcp.x = 0;
                break;
            case '\n':
                pcp.y++;
                break;
            default:
                write_char(pcp, ca);
                break;
        }

        // Do we need to scroll ?
        ushort nbrows = _bios.ScreenRows;
        if (pcp.y > nbrows) {
            pcp.y--;

            cursorpos win = new(0, 0, pcp.page);
            cursorpos winsize = new(_bios.ScreenColumns, nbrows + 1, 0);
            carattr attr = new(' ', 0, false);
            vgafb_scroll(win, winsize, 1, attr);
        }
    }

    private void vgafb_scroll(cursorpos win, cursorpos winsize, int lines, carattr ca) {
        if (lines == 0) {
            // Clear window
            vgafb_clear_chars(win, winsize, ca);
        } else if (lines > 0) {
            // Scroll the window up (eg, from page down key)
            winsize.y -= lines;
            vgafb_move_chars(win, winsize, lines);

            win.y += winsize.y;
            winsize.y = lines;
            vgafb_clear_chars(win, winsize, ca);
        } else {
            // Scroll the window down (eg, from page up key)
            win.y -= lines;
            winsize.y += lines;
            vgafb_move_chars(win, winsize, lines);

            win.y += lines;
            winsize.y = -lines;
            vgafb_clear_chars(win, winsize, ca);
        }
    }

    private void vgafb_move_chars(cursorpos dest, cursorpos movesize, int lines) {
        vgamode_s vmode_g = get_current_mode();

        if (vmode_g.memmodel != MM.TEXT) {
            gfx_move_chars(vmode_g, dest, movesize, lines);
            return;
        }

        int stride = _bios.ScreenColumns * 2;
        ushort dest_addr = text_address(dest), src_addr = (ushort)(dest_addr + lines * stride);
        memmove_stride(vmode_g.sstart, dest_addr, src_addr, movesize.x * 2, stride, (ushort)movesize.y);
    }

    private void gfx_move_chars(vgamode_s vmode_g, cursorpos dest, cursorpos movesize, int lines) {
        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.x = (ushort)(dest.x * 8);
        op.xlen = (ushort)(movesize.x * 8);
        int cheight = _bios.CharacterPointHeight;
        op.y = (ushort)(dest.y * cheight);
        op.ylen = (ushort)(movesize.y * cheight);
        op.srcy = (ushort)(op.y + lines * cheight);
        op.op = GO.MEMMOVE;
        handle_gfx_op(op);
    }

    private void vgafb_clear_chars(cursorpos win, cursorpos winsize, carattr ca) {
        vgamode_s vmode_g = get_current_mode();

        if (vmode_g.memmodel != MM.TEXT) {
            gfx_clear_chars(vmode_g, win, winsize, ca);
            return;
        }

        int stride = _bios.ScreenColumns * 2;
        ushort attr = (ushort)((ca.use_attr ? ca.attr : 0x07) << 8 | ca.car);
        memset16_stride(vmode_g.sstart, text_address(win), attr, winsize.x * 2, stride, winsize.y);
    }

    private void memset16_stride(ushort seg, ushort dst, ushort val, int setlen, int stride, int lines) {
        for (; lines > 0; lines--, dst += (ushort)stride)
            memset16_far(seg, dst, val, setlen);
    }

    private void gfx_clear_chars(vgamode_s vmode_g, cursorpos win, cursorpos winsize, carattr ca) {
        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.x = (ushort)(win.x * 8);
        op.xlen = (ushort)(winsize.x * 8);
        int cheight = _bios.CharacterPointHeight;
        op.y = (ushort)(win.y * cheight);
        op.ylen = (ushort)(winsize.y * cheight);
        op.pixels[0] = ca.attr;
        if (vga_emulate_text())
            op.pixels[0] = (byte)(ca.attr >> 4);
        op.op = GO.MEMSET;
        handle_gfx_op(op);
    }

    private void handle_gfx_op(gfx_op op) {
        switch (op.vmode_g.memmodel) {
            case MM.PLANAR:
                gfx_planar(op);
                break;
            case MM.CGA:
                gfx_cga(op);
                break;
            case MM.PACKED:
                gfx_packed(op);
                break;
            case MM.DIRECT:
                gfx_direct(op);
                break;
        }
    }

    private void gfx_direct(gfx_op op) {
        throw new NotSupportedException("SVGA not supported");
    }

    private void gfx_packed(gfx_op op) {
        ushort dest_far = (ushort)(op.y * op.linelength + op.x);
        switch (op.op) {
            default:
            case GO.READ8:
                op.pixels = _memory.GetData(MemoryUtils.ToPhysicalAddress(SEG_GRAPH, dest_far), 8);
                break;
            case GO.WRITE8:
                _memory.LoadData(MemoryUtils.ToPhysicalAddress(SEG_GRAPH, dest_far), op.pixels, 8);
                break;
            case GO.MEMSET:
                memset_stride(SEG_GRAPH, dest_far, op.pixels[0], op.xlen, op.linelength, op.ylen);
                break;
            case GO.MEMMOVE:
                ushort src_far = (ushort)(op.srcy * op.linelength + op.x);
                memmove_stride(SEG_GRAPH, dest_far, src_far, op.xlen, op.linelength, op.ylen);
                break;
        }
    }

    private void gfx_cga(gfx_op op) {
        int bpp = op.vmode_g.depth;
        ushort dest_far = (ushort)(op.y / 2 * op.linelength + op.x / 8 * bpp);
        switch (op.op) {
            default:
            case GO.READ8:
                if ((op.y & 1) != 0)
                    dest_far += 0x2000;
                if (bpp == 1) {
                    byte datab = _memory.UInt8[SEG_CTEXT, dest_far];
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        op.pixels[pixel] = (byte)(datab >> 7 - pixel & 1);
                } else {
                    ushort datas = _memory.UInt16[SEG_CTEXT, dest_far];
                    datas = be16_to_cpu(datas);
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        op.pixels[pixel] = (byte)(datas >> (7 - pixel) * 2 & 3);
                }
                break;
            case GO.WRITE8:
                if ((op.y & 1) != 0)
                    dest_far += 0x2000;
                if (bpp == 1) {
                    byte datab = 0;
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        datab |= (byte)((op.pixels[pixel] & 1) << 7 - pixel);
                    _memory.UInt8[SEG_CTEXT, dest_far] = datab;
                } else {
                    ushort datas = 0;
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        datas |= (byte)((op.pixels[pixel] & 3) << (7 - pixel) * 2);
                    datas = cpu_to_be16(datas);
                    _memory.UInt16[SEG_CTEXT, dest_far] = datas;
                }
                break;
            case GO.MEMSET:
                byte data = op.pixels[0];
                if (bpp == 1)
                    data = (byte)(data & 1 | (data & 1) << 1);
                data &= 3;
                data |= (byte)(data << 2 | data << 4 | data << 6);
                memset_stride(SEG_CTEXT, dest_far, data, op.xlen / 8 * bpp, op.linelength, (ushort)(op.ylen / 2));
                memset_stride(SEG_CTEXT, (ushort)(dest_far + 0x2000), data, op.xlen / 8 * bpp, op.linelength, (ushort)(op.ylen / 2));
                break;
            case GO.MEMMOVE:
                ushort src_far = (ushort)(op.srcy / 2 * op.linelength + op.x / 8 * bpp);
                memmove_stride(SEG_CTEXT, dest_far, src_far, op.xlen / 8 * bpp, op.linelength, (ushort)(op.ylen / 2));
                memmove_stride(SEG_CTEXT, (ushort)(dest_far + 0x2000), (ushort)(src_far + 0x2000), op.xlen / 8 * bpp, op.linelength, (ushort)(op.ylen / 2));
                break;
        }
    }

    private ushort cpu_to_be16(ushort val) {
        return (ushort)(val << 8 | val >> 8);
    }

    private ushort be16_to_cpu(ushort val) {
        return (ushort)(val << 8 | val >> 8);
    }

    private void gfx_planar(gfx_op op) {
        ushort dest_far = (ushort)(op.y * op.linelength + op.x / 8);
        int plane;
        switch (op.op) {
            default:
            case GO.READ8:
                op.pixels = new byte[8];
                for (plane = 0; plane < 4; plane++) {
                    stdvga_planar4_plane(plane);
                    byte data = _memory.UInt8[SEG_GRAPH, dest_far];
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        op.pixels[pixel] |= (byte)((data >> 7 - pixel & 1) << plane);
                }
                break;
            case GO.WRITE8:
                for (plane = 0; plane < 4; plane++) {
                    stdvga_planar4_plane(plane);
                    byte data = 0;
                    int pixel;
                    for (pixel = 0; pixel < 8; pixel++)
                        data |= (byte)((op.pixels[pixel] >> plane & 1) << 7 - pixel);
                    _memory.UInt8[SEG_GRAPH, dest_far] = data;
                }
                break;
            case GO.MEMSET:
                for (plane = 0; plane < 4; plane++) {
                    stdvga_planar4_plane(plane);
                    byte data = (byte)((op.pixels[0] & 1 << plane) != 0 ? 0xFF : 0x00);
                    memset_stride(SEG_GRAPH, dest_far, data, op.xlen / 8, op.linelength, op.ylen);
                }
                break;
            case GO.MEMMOVE:
                ushort src_far = (ushort)(op.srcy * op.linelength + op.x / 8);
                for (plane = 0; plane < 4; plane++) {
                    stdvga_planar4_plane(plane);
                    memmove_stride(SEG_GRAPH, dest_far, src_far, op.xlen / 8, op.linelength, op.ylen);
                }
                break;
        }
        stdvga_planar4_plane(-1);
    }

    private void memmove_stride(ushort seg, ushort dst, ushort src, int copylen, int stride, ushort lines) {
        if (src < dst) {
            dst += (ushort)(stride * (lines - 1));
            src += (ushort)(stride * (lines - 1));
            stride = -stride;
        }
        for (; lines > 0; lines--, dst += (ushort)stride, src += (ushort)stride)
            memcpy_far(seg, dst, seg, src, copylen);
    }

    private void memcpy_far(ushort dstseg, ushort dst, ushort srcseg, ushort src, int copylen
    ) {
        _memory.MemCopy(MemoryUtils.ToPhysicalAddress(srcseg, src), MemoryUtils.ToPhysicalAddress(dstseg, dst), (uint)copylen);
    }

    private void memset_stride(ushort seg, ushort dst, byte val, int setlen, int stride, ushort lines) {
        for (; lines > 0; lines--, dst += (ushort)stride)
            memset_far(seg, dst, val, setlen);
    }

    private void memset_far(ushort seg, ushort dst, byte val, int setlen) {
        _memory.Memset(MemoryUtils.ToPhysicalAddress(seg, dst), val, (uint)setlen);
    }

    private void stdvga_planar4_plane(int plane) {
        if (plane < 0) {
            // Return to default mode (read plane0, write all planes)
            stdvga_sequ_write(0x02, 0x0f);
            stdvga_grdc_write(0x04, 0);
        } else {
            stdvga_sequ_write(0x02, (byte)(1 << plane));
            stdvga_grdc_write(0x04, (byte)plane);
        }
    }

    private bool vga_emulate_text() {
        return false;
    }

    private void write_char(cursorpos pcp, carattr ca) {
        vgafb_write_char(pcp, ca);
        pcp.x++;
        // Do we need to wrap ?
        if (pcp.x == _bios.ScreenColumns) {
            pcp.x = 0;
            pcp.y++;
        }
    }

    private void vgafb_write_char(cursorpos cp, carattr ca) {
        vgamode_s vmode_g = get_current_mode();

        if (vmode_g.memmodel != MM.TEXT) {
            gfx_write_char(vmode_g, cp, ca);
            return;
        }

        ushort dest_far = text_address(cp);
        if (ca.use_attr) {
            ushort dummy = (ushort)(ca.attr << 8 | ca.car);
            _memory.UInt16[vmode_g.sstart, dest_far] = dummy;
        } else {
            _memory.UInt16[vmode_g.sstart, dest_far] = ca.car;
        }
    }

    private void gfx_write_char(vgamode_s vmode_g, cursorpos cp, carattr ca) {
        if (cp.x >= _bios.ScreenColumns)
            return;

        segoff_s font = get_font_data(ca.car);
        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.x = (ushort)(cp.x * 8);
        int cheight = _bios.CharacterPointHeight;
        op.y = (ushort)(cp.y * cheight);
        byte fgattr = ca.attr, bgattr = 0x00;
        int usexor = 0;
        if (vga_emulate_text()) {
            if (ca.use_attr) {
                bgattr = (byte)(fgattr >> 4);
                fgattr = (byte)(fgattr & 0x0f);
            } else {
                // Read bottom right pixel of the cell to guess bg color
                op.op = GO.READ8;
                op.y += (ushort)(cheight - 1);
                handle_gfx_op(op);
                op.y -= (ushort)(cheight - 1);
                bgattr = op.pixels[7];
                fgattr = (byte)(bgattr ^ 0x7);
            }
        } else if ((fgattr & 0x80) != 0 && vmode_g.depth < 8) {
            usexor = 1;
            fgattr &= 0x7f;
        }
        int i;
        for (i = 0; i < cheight; i++, op.y++) {
            byte fontline = _memory.UInt8[font.seg, (ushort)(font.offset + i)];
            if (usexor != 0) {
                op.op = GO.READ8;
                handle_gfx_op(op);
                int j;
                for (j = 0; j < 8; j++)
                    op.pixels[j] ^= (byte)((fontline & 0x80 >> j) != 0 ? fgattr : 0x00);
            } else {
                int j;
                for (j = 0; j < 8; j++)
                    op.pixels[j] = (fontline & 0x80 >> j) != 0 ? fgattr : bgattr;
            }
            op.op = GO.WRITE8;
            handle_gfx_op(op);
        }
    }

    private void init_gfx_op(out gfx_op op, vgamode_s vmode_g) {
        op = new gfx_op {
            pixels = new byte[8],
            vmode_g = vmode_g,
            linelength = vgahw_get_linelength(vmode_g),
            displaystart = vgahw_get_displaystart(vmode_g)
        };
    }

    private int vgahw_get_displaystart(vgamode_s vmode_g) {
        VGAREG crtc_addr = stdvga_get_crtc();
        int addr = (stdvga_crtc_read(crtc_addr, 0x0c) << 8 | stdvga_crtc_read(crtc_addr, 0x0d));
        return addr * 4 / stdvga_vram_ratio(vmode_g);
    }

    private int vgahw_get_linelength(vgamode_s vmode_g) {
        return stdvga_get_linelength(vmode_g);
    }

    private int stdvga_get_linelength(vgamode_s vmode_g) {
        byte val = stdvga_crtc_read(stdvga_get_crtc(), 0x13);
        return val * 8 / stdvga_vram_ratio(vmode_g);
    }

    private int stdvga_vram_ratio(vgamode_s vmode_g) {
        return vmode_g.memmodel switch {
            MM.TEXT => 2,
            MM.CGA => 4 / vmode_g.depth,
            MM.PLANAR => 4,
            _ => 1
        };
    }

    private byte stdvga_crtc_read(VGAREG crtc_addr, byte index) {
        outb(index, crtc_addr);
        return inb(crtc_addr + 1);
    }

    private segoff_s get_font_data(char c) {
        int char_height = _bios.CharacterPointHeight;
        segoff_s font;
        if (char_height == 8 && c >= 128) {
            font = GET_IVT(0x1f);
            c = (char)(c - 128);
        } else {
            font = GET_IVT(0x43);
        }
        font.offset += (ushort)(c * char_height);
        return font;
    }

    private segoff_s GET_IVT(int vector) {
        return new segoff_s {
            segoff = _memory.UInt32[MemoryMap.InterruptVectorSegment, (ushort)(4 * vector)]
        };
    }

    private vgamode_s get_current_mode() {
        return _currentMode.Info;
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

    public void SetPaletteRegisters() {
        switch (_state.AL) {
            case 0x00:
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
                if (_state.BL > 0x14) {
                    return;
                }
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

    public void GetVideoState() {
        _state.BH = _bios.CurrentVideoPage;
        _state.AL = (byte)(_bios.VideoMode | _bios.VideoCtl & 0x80);
        _state.AH = (byte)_bios.ScreenColumns;
    }

    public void WriteTextInTeletypeMode() {
        carattr ca = new((char)_state.AL, _state.BL, false);
        cursorpos cp = get_cursor_pos(_bios.CurrentVideoPage);
        write_teletype(cp, ca);
        set_cursor_pos(cp);
    }

    private cursorpos get_cursor_pos(byte page) {
        if (page > 7)
            return new cursorpos(0, 0, 0);
        ushort xy = _bios.CursorPosition[page];
        return new cursorpos(xy, xy >> 8, page);
    }

    public void SetColorPaletteOrBackGroundColor() {
        switch (_state.BH) {
            case 0x00:
                stdvga_set_border_color(_state.BL);
                break;
            case 0x01:
                stdvga_set_palette(_state.BL);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_state.BH), _state.BH, $"INT 10: {nameof(SetColorPaletteOrBackGroundColor)} Invalid subfunction 0x{_state.BH:X2}");
        }
    }

    private void stdvga_set_palette(byte palid) {
        byte i;
        for (i = 1; i < 4; i++)
            stdvga_attr_mask(i, 0x01, (byte)(palid & 0x01));
    }

    private void stdvga_set_border_color(byte color) {
        byte v1 = (byte)(color & 0x0f);
        if ((v1 & 0x08) != 0)
            v1 += 0x08;
        stdvga_attr_write(0x00, v1);

        byte i;
        for (i = 1; i < 4; i++)
            stdvga_attr_mask(i, 0x10, (byte)(color & 0x10));
    }

    public void WriteCharacterAtCursor() {
        carattr ca = new((char)_state.AL, _state.BL, false);
        cursorpos cp = get_cursor_pos(_state.BH);
        int count = _state.CX;
        while (count-- > 0)
            write_char(cp, ca);
    }

    public void WriteCharacterAndAttributeAtCursor() {
        carattr ca = new((char)_state.AL, _state.BL, true);
        cursorpos cp = get_cursor_pos(_state.BH);
        int count = _state.CX;
        while (count-- > 0)
            write_char(cp, ca);
    }

    public void ReadCharacterAndAttributeAtCursor() {
        carattr ca = vgafb_read_char(get_cursor_pos(_state.BH));
        _state.AL = (byte)ca.car;
        _state.AH = ca.attr;
    }

    private carattr vgafb_read_char(cursorpos cp) {
        vgamode_s vmode_g = get_current_mode();

        if (vmode_g.memmodel != MM.TEXT)
            return gfx_read_char(vmode_g, cp);

        ushort dest_far = text_address(cp);
        ushort v = _memory.UInt16[vmode_g.sstart, dest_far];
        return new carattr((char)v, (byte)(v >> 8), false);
    }

    private carattr gfx_read_char(vgamode_s vmode_g, cursorpos cp) {
        byte[] lines = new byte[16];
        int cheight = _bios.CharacterPointHeight;
        if (cp.x >= _bios.ScreenColumns || cheight > lines.Length)
            goto fail;

        // Read cell from screen
        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.op = GO.READ8;
        op.x = (ushort)(cp.x * 8);
        op.y = (ushort)(cp.y * cheight);
        char car = (char)0;
        byte fgattr = 0x00, bgattr = 0x00;
        if (vga_emulate_text()) {
            // Read bottom right pixel of the cell to guess bg color
            op.y += (ushort)(cheight - 1);
            handle_gfx_op(op);
            op.y -= (ushort)(cheight - 1);
            bgattr = op.pixels[7];
            fgattr = (byte)(bgattr ^ 0x7);
            // Report space character for blank cells (skip null character check)
            car = ' ';
        }
        byte i, j;
        for (i = 0; i < cheight; i++, op.y++) {
            byte line = 0;
            handle_gfx_op(op);
            for (j = 0; j < 8; j++)
                if (op.pixels[j] != bgattr) {
                    line |= (byte)(0x80 >> j);
                    fgattr = op.pixels[j];
                }
            lines[i] = line;
        }

        // Determine font
        for (; car < 256; car++) {
            segoff_s font = get_font_data(car);
            if (memcmp_far(lines, font.seg, font.offset, cheight) == 0)
                return new carattr(car, (byte)(fgattr | (bgattr << 4)), false);
        }
        fail:
        return new carattr((char)0, 0, false);
    }

    private int memcmp_far(byte[] s1, ushort s2seg, ushort s2, int n) {
        int i = 0;
        while (n-- > 0) {
            int d = s1[i] - _memory.UInt8[s2seg, s2];
            if (d != 0)
                return d < 0 ? -1 : 1;
            i++;
            s2++;
        }
        return 0;
    }

    public void ScrollPageDown() {
        verify_scroll(-1);
    }

    private void verify_scroll(int dir) {
        // Verify parameters
        byte ulx = _state.CL, uly = _state.CH, lrx = _state.DL, lry = _state.DH;
        ushort nbrows = (ushort)(_bios.ScreenRows + 1);
        if (lry >= nbrows)
            lry = (byte)(nbrows - 1);
        ushort nbcols = _bios.ScreenColumns;
        if (lrx >= nbcols)
            lrx = (byte)(nbcols - 1);
        int wincols = lrx - ulx + 1, winrows = lry - uly + 1;
        if (wincols <= 0 || winrows <= 0)
            return;
        int lines = _state.AL;
        if (lines >= winrows)
            lines = 0;
        lines *= dir;

        // Scroll (or clear) window
        cursorpos win = new(ulx, uly, _bios.CurrentVideoPage);
        cursorpos winsize = new(wincols, winrows, 0);
        carattr attr = new(' ', _state.BH, true);
        vgafb_scroll(win, winsize, lines, attr);
    }

    public void ScrollPageUp() {
        verify_scroll(1);
    }

    public void SelectActiveDisplayPage() {
        set_active_page(_state.AL);
    }

    private void set_active_page(byte page) {
        if (page > 7)
            return;

        // Get the mode
        vgamode_s vmode_g = get_current_mode();

        // Calculate memory address of start of page
        cursorpos cp = new(0, 0, page);
        int address = text_address(cp);
        vgahw_set_displaystart(vmode_g, address);

        // And change the BIOS page
        _bios.VideoPageStart = (ushort)address;
        _bios.CurrentVideoPage = page;

        if (_logger.IsEnabled(LogEventLevel.Information))
            _logger.Information("INT 10 Set active page {Page:X2} address {Address:X4}", page, address);

        // Display the cursor, now the page is active
        set_cursor_pos(get_cursor_pos(page));
    }

    private void vgahw_set_displaystart(vgamode_s vmode_g, int val) {
        VGAREG crtc_addr = stdvga_get_crtc();
        val = val * stdvga_vram_ratio(vmode_g) / 4;
        stdvga_crtc_write(crtc_addr, 0x0c, (byte)(val >> 8));
        stdvga_crtc_write(crtc_addr, 0x0d, (byte)val);
    }

    public void GetCursorPosition() {
        _state.CX = _bios.CursorType;
        cursorpos cp = get_cursor_pos(_state.BH);
        _state.DL = (byte)cp.x;
        _state.DH = (byte)cp.y;
    }

    public void SetCursorPosition() {
        cursorpos cp = new(_state.DL, _state.DH, _state.BH);
        set_cursor_pos(cp);
    }

    public void SetCursorType() {
        set_cursor_shape(_state.CX);
    }

    private void set_cursor_shape(ushort cursor_type) {
        _bios.CursorType = cursor_type;
        
        stdvga_set_cursor_shape(get_cursor_shape());
    }

    private void stdvga_set_cursor_shape(ushort cursor_type) {
        VGAREG crtc_addr = stdvga_get_crtc();
        stdvga_crtc_write(crtc_addr, 0x0a, (byte)(cursor_type >> 8));
        stdvga_crtc_write(crtc_addr, 0x0b, (byte)cursor_type);
    }

    private ushort get_cursor_shape() {
        ushort cursor_type = _bios.CursorType;
        bool emulate_cursor = (_bios.VideoCtl & 1) == 0;
        if (!emulate_cursor)
            return cursor_type;
        byte start = (byte)((cursor_type >> 8) & 0x3f);
        byte end = (byte)(cursor_type & 0x1f);
        ushort cheight = _bios.CharacterPointHeight;
        if (cheight <= 8 || end >= 8 || start >= 0x20)
            return cursor_type;
        if (end != (start + 1))
            start = (byte)(((start + 1) * cheight / 8) - 1);
        else
            start = (byte)(((end + 1) * cheight / 8) - 2);
        end = (byte)(((end + 1) * cheight / 8) - 1);
        return (ushort)((start << 8) | end);
    }

    private void init_bios_area() {
        // init detected hardware BIOS Area
        // set 80x25 color (not clear from RBIL but usual)
        set_equipment_flags(0x30, 0x20);

        // Set the basic modeset options
        _bios.ModesetCtl = 0x51;
        _bios.DisplayCombinationCode = 0x08;
    }

    private void stdvga_setup() {
        // switch to color mode and enable CPU access 480 lines
        stdvga_misc_write(0xc3);
        // more than 64k 3C4/04
        stdvga_sequ_write(0x04, 0x02);
    }

    private void set_equipment_flags(int clear, int set) {
        _bios.EquipmentListFlags = (ushort)(_bios.EquipmentListFlags & ~clear | set);
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
        _state.AL = vgafb_read_pixel(_state.CX, _state.DX);
    }

    private byte vgafb_read_pixel(ushort x, ushort y) {
        vgamode_s vmode_g = get_current_mode();

        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.x = ALIGN_DOWN(x, 8);
        op.y = y;
        op.op = GO.READ8;
        handle_gfx_op(op);

        return op.pixels[x & 0x07];
    }

    private ushort ALIGN_DOWN(ushort value, int alignment) {
        int mask = alignment - 1;
        return (ushort)(value & ~mask);
    }

    private void WriteDot() {
        vgafb_write_pixel(_state.AL, _state.CX, _state.DX);
    }

    private void vgafb_write_pixel(byte color, ushort x, ushort y) {
        vgamode_s vmode_g = get_current_mode();

        gfx_op op;
        init_gfx_op(out op, vmode_g);
        op.x = ALIGN_DOWN(x, 8);
        op.y = y;
        op.op = GO.READ8;
        handle_gfx_op(op);

        bool usexor = (color & 0x80) != 0 && vmode_g.depth < 8;
        if (usexor)
            op.pixels[x & 0x07] ^= (byte)(color & 0x7f);
        else
            op.pixels[x & 0x07] = color;
        op.op = GO.WRITE8;
        handle_gfx_op(op);
    }

    private void ReadLightPenPosition() {
        _state.AX = _state.BX = _state.CX = _state.DX = 0;
    }

    private void SetMode() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: Set video mode to {AL:X2}", _state.AL);
        }
        int mode = _state.AL & 0x7f;
        ModeFlags flags = ModeFlags.Legacy | (ModeFlags)_bios.ModesetCtl & (ModeFlags.NoPalette | ModeFlags.GraySum);
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

    private void VgaSetMode(int mode, ModeFlags flags) {
        stdvga_mode_s vmode_g = VgaHwFindMode(mode);

        VgahwSetMode(vmode_g, flags);
        vgamode_s vgamodeS = vmode_g.Info;

        // Set the BIOS mem
        ushort width = vgamodeS.Width;
        ushort height = vgamodeS.Height;
        MM memmodel = vgamodeS.memmodel;
        ushort cheight = vgamodeS.CharacterHeight;
        if (mode < 0x100) {
            _bios.VideoMode = (byte)mode;
        } else {
            _bios.VideoMode = 0xff;
        }

        _currentMode = vmode_g;

        if (memmodel == MM.TEXT) {
            _bios.ScreenColumns = (byte)width;
            _bios.ScreenRows = (byte)(height - 1);
            _bios.CursorType = 0x0607;
        } else {
            _bios.ScreenColumns = (byte)(width / vgamodeS.CharacterWidth);
            _bios.ScreenRows = (byte)(height / vgamodeS.CharacterHeight - 1);
            _bios.CursorType = 0x0000;
        }
        _bios.VideoPageSize = calc_page_size(memmodel, width, height);
        _bios.CrtControllerBaseAddress = (ushort)stdvga_get_crtc();
        _bios.CharacterPointHeight = cheight;
        _bios.VideoCtl = (byte)(0x60 | (flags.HasFlag(ModeFlags.NoClearMem) ? 0x80 : 0x00));
        _bios.FeatureSwitches = 0xF9;
        _bios.ModesetCtl &= 0x7F;
        for (int i = 0; i < 8; i++) {
            _bios.CursorPosition[i] = 0x0000;
        }
        _bios.VideoPageStart = 0x0000;
        _bios.CurrentVideoPage = 0x00;

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
                return;
        }
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43] = address.Offset;
        _memory.UInt16[MemoryMap.InterruptVectorSegment, 4 * 0x43 + 2] = address.Segment;
    }

    private VGAREG stdvga_get_crtc() {
        if ((stdvga_misc_read() & 1) != 0) {
            return VGAREG.VGA_CRTC_ADDRESS;
        }
        return VGAREG.MDA_CRTC_ADDRESS;
    }

    private ushort calc_page_size(MM memmodel, int width, int height) {
        int result = memmodel switch {
            MM.TEXT => ALIGN(width * height * 2, 2 * 1024),
            MM.CGA => 16 * 1024,
            _ => ALIGN(width * height / 8, 8 * 1024)
        };
        return (ushort)result;
    }

    private int ALIGN(int alignment, int value) {
        int mask = alignment - 1;
        return value + mask & ~mask;
    }

    private void VgahwSetMode(stdvga_mode_s stdmode_g, ModeFlags flags) {
        vgamode_s vmode_g = stdmode_g.Info;

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
            for (int i = palsize; i < 256; i++) {
                stdvga_dac_write(empty, (byte)i, 1);
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
        if (vmode_g.memmodel == MM.TEXT) {
            stdvga_load_font(VgaRom.vgafont16, 0x100, 0, 0, 16);
        }
    }

    private void stdvga_load_font(byte[] fontBytes, ushort count, ushort start, byte destFlags, byte fontSize) {
        get_font_access();
        ushort blockaddr = (ushort)(((destFlags & 0x03) << 14) + ((destFlags & 0x04) << 11));
        ushort dest_far = (ushort)(blockaddr + start * 32);
        for (ushort i = 0; i < count; i++) {
            uint address = MemoryUtils.ToPhysicalAddress(SEG_GRAPH, (ushort)(dest_far + i * 32));
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

    private void clear_screen(vgamode_s vmode_g) {
        switch (vmode_g.memmodel) {
            case MM.TEXT:
                memset16_far(vmode_g.sstart, 0, 0x0720, 32 * 1024);
                break;
            case MM.CGA:
                memset16_far(vmode_g.sstart, 0, 0x0000, 32 * 1024);
                break;
            default:
                memset16_far(vmode_g.sstart, 0, 0x0000, 64 * 1024);
                break;
        }
    }

    private void memset16_far(ushort segment, ushort start, ushort value, int sizeInBytes) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, start);
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

    private static stdvga_mode_s VgaHwFindMode(int mode) {
        foreach (stdvga_mode_s standardVgaMode in VgaModes) {
            if (standardVgaMode.Mode == mode) {
                return standardVgaMode;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode");
    }

    private void SetDisplayCombinationCode() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: Set display combination {Value:X2}", _state.BL);
        }
        _state.AL = 0x1A; // Function supported
        _bios.DisplayCombinationCode = _state.BL;
    }

    private void GetDisplayCombinationCode() {
        _state.AL = 0x1A; // Function supported
        _state.BL = _bios.DisplayCombinationCode; // Primary display
        _state.BH = 0x00; // No secondary display
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("INT 10: Get display combination {Value:X2}", _state.BL);
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
        _state.CX = (ushort)(_bios.CharacterPointHeight & 0xff);

        // Set Highest char row
        _state.DL = _bios.ScreenRows;
    }

    private void stdvga_read_video_dac_state(out byte pMode, out byte curPage) {
        byte val1 = (byte)(stdvga_attr_read(0x10) >> 7);
        byte val2 = (byte)(stdvga_attr_read(0x14) & 0x0f);
        if ((val1 & 0x01) == 0) {
            val2 >>= 2;
        }
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
        if ((val & 0x80) == 0) {
            data <<= 2;
        }
        data &= 0x0f;
        stdvga_attr_write(0x14, data);
    }

    private void stdvga_dac_write(ushort segment, ushort offset, byte startIndex, ushort count) {
        byte[] rgb = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), 3 * count);
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
        outb((byte)(v & ~off | on), VGAREG.ACTL_WRITE_DATA);
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
        if (register > 0x14) {
            return;
        }
        stdvga_attr_write(register, value);
    }
}

internal record struct gfx_op(vgamode_s vmode_g, int linelength, int displaystart, GO op, ushort x, ushort y, byte[] pixels, ushort xlen, ushort ylen, ushort srcy);

internal enum GO {
    READ8,
    WRITE8,
    MEMSET,
    MEMMOVE
}

[StructLayout(LayoutKind.Explicit)]
internal record struct segoff_s {
    [FieldOffset(0)]
    public ushort offset;

    [FieldOffset(2)]
    public ushort seg;

    [FieldOffset(0)]
    public uint segoff;
}

public record struct carattr(char car, byte attr, bool use_attr);

public record struct cursorpos(int x, int y, int page);

internal struct stdvga_mode_s {
    public ushort Mode;
    public vgamode_s Info;
    public byte Pelmask;
    public byte[] Dac;
    public ushort Dacsize => (ushort)Dac.Length;
    public byte[] SequRegs;
    public byte Miscreg;
    public byte[] CrtcRegs;
    public byte[] ActlRegs;
    public byte[] GrdcRegs;

    public stdvga_mode_s(ushort mode, vgamode_s info, byte pelmask, byte[] dac, byte[] sequRegs, byte miscreg, byte[] crtcRegs, byte[] actlRegs, byte[] grdcRegs) : this() {
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

internal struct vgamode_s {
    public MM memmodel;
    public ushort Width;
    public ushort Height;
    public byte depth;
    public byte CharacterWidth;
    public byte CharacterHeight;
    public ushort sstart;

    public vgamode_s(MM memmodel_, ushort width, ushort height, byte depth_, byte characterWidth, byte characterHeight, ushort sstart_) {
        memmodel = memmodel_;
        Width = width;
        Height = height;
        depth = depth_;
        CharacterWidth = characterWidth;
        CharacterHeight = characterHeight;
        sstart = sstart_;
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

internal enum MM {
    TEXT,
    CGA,
    HERCULES,
    PLANAR,
    PACKED,
    NON_CHAIN_4_256,
    DIRECT,
    YUV
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