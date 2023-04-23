namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

internal class VgaFunctions {
    private readonly IIOPortHandler _ioPortDispatcher;
    private readonly Memory _memory;

    public VgaFunctions(Memory memory, IIOPortHandler ioPortDispatcher) {
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
        stdvga_setup();
    }

    public void stdvga_set_cursor_pos(ushort address) {
        VgaPort crtc_addr = stdvga_get_crtc();
        address /= 2; // Assume we're in text mode.
        stdvga_crtc_write(crtc_addr, 0x0e, (byte)(address >> 8));
        stdvga_crtc_write(crtc_addr, 0x0f, (byte)address);
    }

    public void stdvga_planar4_plane(int plane) {
        if (plane < 0) {
            // Return to default mode (read plane0, write all planes)
            stdvga_sequ_write(0x02, 0x0f);
            stdvga_grdc_write(0x04, 0);
        } else {
            stdvga_sequ_write(0x02, (byte)(1 << plane));
            stdvga_grdc_write(0x04, (byte)plane);
        }
    }

    public int stdvga_get_linelength(VgaMode vgaMode) {
        byte value = stdvga_crtc_read(stdvga_get_crtc(), 0x13);
        return value * 8 / stdvga_vram_ratio(vgaMode);
    }

    public int stdvga_vram_ratio(VgaMode vgaMode) {
        return vgaMode.MemoryModel switch {
            MemoryModel.Text => 2,
            MemoryModel.Cga => 4 / vgaMode.BitsPerPixel,
            MemoryModel.Planar => 4,
            _ => 1
        };
    }

    public byte stdvga_crtc_read(VgaPort crtc_addr, byte index) {
        outb(index, crtc_addr);
        return inb(crtc_addr + 1);
    }

    public void stdvga_set_palette(byte paletteId) {
        byte i;
        for (i = 1; i < 4; i++) {
            stdvga_attr_mask(i, 0x01, (byte)(paletteId & 0x01));
        }
    }

    public void stdvga_set_border_color(byte color) {
        byte v1 = (byte)(color & 0x0f);
        if ((v1 & 0x08) != 0) {
            v1 += 0x08;
        }
        stdvga_attr_write(0x00, v1);

        byte i;
        for (i = 1; i < 4; i++) {
            stdvga_attr_mask(i, 0x10, (byte)(color & 0x10));
        }
    }

    public void stdvga_set_cursor_shape(ushort cursorType) {
        VgaPort crtc_addr = stdvga_get_crtc();
        stdvga_crtc_write(crtc_addr, 0x0a, (byte)(cursorType >> 8));
        stdvga_crtc_write(crtc_addr, 0x0b, (byte)cursorType);
    }

    private void stdvga_setup() {
        // switch to color mode and enable CPU access 480 lines
        stdvga_misc_write(0xc3);
        // more than 64k 3C4/04
        stdvga_sequ_write(0x04, 0x02);
    }

    public VgaPort stdvga_get_crtc() {
        if ((stdvga_misc_read() & 1) != 0) {
            return VgaPort.VGA_CRTC_ADDRESS;
        }
        return VgaPort.MDA_CRTC_ADDRESS;
    }

    public void stdvga_load_font(byte[] fontBytes, ushort count, ushort start, byte destinationFlags, byte fontSize) {
        get_font_access();
        ushort blockAddress = (ushort)(((destinationFlags & 0x03) << 14) + ((destinationFlags & 0x04) << 11));
        ushort destination = (ushort)(blockAddress + start * 32);
        for (ushort i = 0; i < count; i++) {
            uint address = MemoryUtils.ToPhysicalAddress(VgaBios.GraphicsSegment, (ushort)(destination + i * 32));
            var value = new Span<byte>(fontBytes, i * fontSize, fontSize);
            _memory.LoadData(address, value.ToArray(), fontSize);
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
        return inb(VgaPort.READ_MISC_OUTPUT);
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

    public void stdvga_misc_write(byte value) {
        outb(value, VgaPort.WRITE_MISC_OUTPUT);
    }

    public void stdvga_crtc_write(VgaPort crtcAddr, byte index, byte value) {
        outw((ushort)(value << 8 | index), crtcAddr);
    }

    public void stdvga_grdc_write(byte index, byte value) {
        outw((ushort)(value << 8 | index), VgaPort.GRDC_ADDRESS);
    }

    public void stdvga_sequ_write(byte index, byte value) {
        outw((ushort)(value << 8 | index), VgaPort.SEQU_ADDRESS);
    }

    private void outw(ushort value, VgaPort port) {
        _ioPortDispatcher.WriteWord((int)port, value);
    }

    public void stdvga_attr_write(byte index, byte value) {
        inb(VgaPort.ACTL_RESET);
        byte orig = inb(VgaPort.ACTL_ADDRESS);
        outb(index, VgaPort.ACTL_ADDRESS);
        outb(value, VgaPort.ACTL_WRITE_DATA);
        outb(orig, VgaPort.ACTL_ADDRESS);
    }

    public void stdvga_perform_gray_scale_summing(byte start, int count) {
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

    public byte[] stdvga_dac_read(byte start, int count) {
        byte[] result = new byte[3 * count];
        outb(start, VgaPort.DAC_READ_ADDRESS);
        for (int i = 0; i < result.Length; i++) {
            result[i] = inb(VgaPort.DAC_DATA);
        }
        return result;
    }

    public void stdvga_attrindex_write(byte value) {
        inb(VgaPort.ACTL_RESET);
        outb(value, VgaPort.ACTL_ADDRESS);
    }

    private byte inb(VgaPort port) {
        return _ioPortDispatcher.ReadByte((int)port);
    }

    public void stdvga_dac_write(IReadOnlyList<byte> palette, byte startIndex, ushort count) {
        outb(startIndex, VgaPort.DAC_WRITE_ADDRESS);
        int i = 0;
        while (count > 0) {
            outb(palette[i++], VgaPort.DAC_DATA);
            outb(palette[i++], VgaPort.DAC_DATA);
            outb(palette[i++], VgaPort.DAC_DATA);
            count--;
        }
    }

    private void outb(byte value, VgaPort port) {
        _ioPortDispatcher.WriteByte((int)port, value);
    }

    public void stdvga_pelmask_write(byte value) {
        outb(value, VgaPort.PEL_MASK);
    }

    public void stdvga_read_video_dac_state(out byte pMode, out byte curPage) {
        byte val1 = (byte)(stdvga_attr_read(0x10) >> 7);
        byte val2 = (byte)(stdvga_attr_read(0x14) & 0x0f);
        if ((val1 & 0x01) == 0) {
            val2 >>= 2;
        }
        pMode = val1;
        curPage = val2;
    }

    public byte stdvga_pelmask_read() {
        return inb(VgaPort.PEL_MASK);
    }

    public void stdvga_dac_read(ushort segment, ushort offset, byte start, ushort count) {
        outb(start, VgaPort.DAC_READ_ADDRESS);
        while (count > 0) {
            _memory.UInt8[segment, offset++] = inb(VgaPort.DAC_DATA);
            _memory.UInt8[segment, offset++] = inb(VgaPort.DAC_DATA);
            _memory.UInt8[segment, offset++] = inb(VgaPort.DAC_DATA);
            count--;
        }
    }

    public void stdvga_select_video_dac_color_page(byte flag, byte data) {
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

    public void stdvga_dac_write(ushort segment, ushort offset, byte startIndex, ushort count) {
        byte[] rgb = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), 3 * count);
        stdvga_dac_write(rgb, startIndex, count);
    }

    public void stdvga_get_all_palette_reg(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            _memory.UInt8[segment, offset++] = stdvga_attr_read(i);
        }
        _memory.UInt8[segment, offset] = stdvga_attr_read(0x11);
    }

    public byte stdvga_get_overscan_border_color() {
        return stdvga_attr_read(0x11);
    }

    public byte stdvga_attr_read(byte index) {
        inb(VgaPort.ACTL_RESET);
        byte orig = inb(VgaPort.ACTL_ADDRESS);
        outb(index, VgaPort.ACTL_ADDRESS);
        byte v = inb(VgaPort.ACTL_READ_DATA);
        inb(VgaPort.ACTL_RESET);
        outb(orig, VgaPort.ACTL_ADDRESS);
        return v;
    }

    public void stdvga_toggle_intensity(byte flag) {
        stdvga_attr_mask(0x10, 0x08, (byte)((flag & 0x01) << 3));
    }

    private void stdvga_attr_mask(byte index, byte off, byte on) {
        inb(VgaPort.ACTL_RESET);
        byte orig = inb(VgaPort.ACTL_ADDRESS);
        outb(index, VgaPort.ACTL_ADDRESS);
        byte v = inb(VgaPort.ACTL_READ_DATA);
        outb((byte)(v & ~off | on), VgaPort.ACTL_WRITE_DATA);
        outb(orig, VgaPort.ACTL_ADDRESS);
    }

    public void stdvga_set_all_palette_reg(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            stdvga_attr_write(i, _memory.UInt8[segment, offset++]);
        }
        stdvga_attr_write(0x11, _memory.UInt8[segment, offset]);
    }

    public void stdvga_set_overscan_border_color(byte color) {
        stdvga_attr_write(0x11, color);
    }

    public void SetEgaPaletteRegister(byte register, byte value) {
        if (register > 0x14) {
            return;
        }
        stdvga_attr_write(register, value);
    }

    public void stdvga_set_text_block_specifier(byte spec) {
        stdvga_sequ_write(0x03, spec);
    }

    public void stdvga_set_scan_lines(byte lines) {
        stdvga_crtc_mask(stdvga_get_crtc(), 0x09, 0x1f, lines - 1);
    }

    private void stdvga_crtc_mask(VgaPort crtc_addr, int index, int off, int on) {
        outb((byte)index, crtc_addr);
        byte value = inb(crtc_addr + 1);
        outb((byte)(value & ~off | on), crtc_addr + 1);
    }

    public ushort stdvga_get_vde() {
        VgaPort crtc_addr = stdvga_get_crtc();
        ushort vde = stdvga_crtc_read(crtc_addr, 0x12);
        byte ovl = stdvga_crtc_read(crtc_addr, 0x07);
        vde = (ushort)(vde + ((ovl & 0x02) << 7) + ((ovl & 0x40) << 3) + 1);
        return vde;
    }

    public void vgahw_set_displaystart(VgaMode vgaMode, int val) {
        VgaPort crtc_addr = stdvga_get_crtc();
        val = val * stdvga_vram_ratio(vgaMode) / 4;
        stdvga_crtc_write(crtc_addr, 0x0c, (byte)(val >> 8));
        stdvga_crtc_write(crtc_addr, 0x0d, (byte)val);
    }

    public int vgahw_get_displaystart(VgaMode vgaMode) {
        VgaPort crtc_addr = stdvga_get_crtc();
        int addr = stdvga_crtc_read(crtc_addr, 0x0c) << 8 | stdvga_crtc_read(crtc_addr, 0x0d);
        return addr * 4 / stdvga_vram_ratio(vgaMode);
    }

    public void VgahwSetMode(VideoMode videoMode, ModeFlags flags) {
        VgaMode vgaMode = videoMode.VgaMode;

        // if palette loading (bit 3 of modeset ctl = 0)
        if (!flags.HasFlag(ModeFlags.NoPalette)) {
            // Set the PEL mask
            stdvga_pelmask_write(videoMode.PixelMask);

            // From which palette
            byte[] palette = videoMode.Dac;
            byte paletteSize = (byte)(videoMode.Dac.Length / 3);

            // Always 256*3 values
            stdvga_dac_write(palette, 0, paletteSize);
            byte[] empty = new byte[3];
            for (int i = paletteSize; i < 256; i++) {
                stdvga_dac_write(empty, (byte)i, 1);
            }

            if (flags.HasFlag(ModeFlags.GraySum)) {
                stdvga_perform_gray_scale_summing(0x00, 0x100);
            }
        }

        // Set Attribute Ctl
        for (byte i = 0; i < videoMode.AttributeControllerRegisterValues.Length; i++) {
            stdvga_attr_write(i, videoMode.AttributeControllerRegisterValues[i]);
        }

        // Set Sequencer Ctl
        for (byte i = 0; i < videoMode.SequencerRegisterValues.Length; i++) {
            stdvga_sequ_write(i, videoMode.SequencerRegisterValues[i]);
        }

        // Set Grafx Ctl
        for (byte i = 0; i < videoMode.GraphicsControllerRegisterValues.Length; i++) {
            stdvga_grdc_write(i, videoMode.GraphicsControllerRegisterValues[i]);
        }

        // Set CRTC address VGA or MDA
        byte miscellaneousRegisterValue = videoMode.MiscellaneousRegisterValue;
        VgaPort crtc_addr = VgaPort.VGA_CRTC_ADDRESS;
        if ((miscellaneousRegisterValue & 1) == 0) {
            crtc_addr = VgaPort.MDA_CRTC_ADDRESS;
        }

        // Disable CRTC write protection
        stdvga_crtc_write(crtc_addr, 0x11, 0x00);
        // Set CRTC regs
        for (byte i = 0; i <= 0x18; i++) {
            stdvga_crtc_write(crtc_addr, i, videoMode.CrtControllerRegisterValues[i]);
        }

        // Set the misc register
        stdvga_misc_write(miscellaneousRegisterValue);

        // Enable video
        stdvga_attrindex_write(0x20);

        // Clear screen
        if (!flags.HasFlag(ModeFlags.NoClearMem)) {
            clear_screen(vgaMode);
        }

        // Write the fonts in memory
        if (vgaMode.MemoryModel == MemoryModel.Text) {
            stdvga_load_font(VgaRom.VgaFont16, 0x100, 0, 0, 16);
        }
    }

    public void clear_screen(VgaMode vgaMode) {
        switch (vgaMode.MemoryModel) {
            case MemoryModel.Text:
                memset16_far(vgaMode.StartSegment, 0, 0x0720, 32 * 1024);
                break;
            case MemoryModel.Cga:
                memset16_far(vgaMode.StartSegment, 0, 0x0000, 32 * 1024);
                break;
            case MemoryModel.Hercules:
            case MemoryModel.Planar:
            case MemoryModel.Packed:
            case MemoryModel.NonChain4X256:
            case MemoryModel.Direct:
            case MemoryModel.Yuv:
            default:
                memset16_far(vgaMode.StartSegment, 0, 0x0000, 64 * 1024);
                break;
        }
    }

    public void memset16_far(ushort segment, ushort offset, ushort value, int amount) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        for (int i = 0; i < amount >> 1; i++) {
            _memory.SetUint16((uint)(address + i), value);
        }
    }
}