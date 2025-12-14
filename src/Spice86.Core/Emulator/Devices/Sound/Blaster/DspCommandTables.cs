// SPDX-License-Identifier: GPL-2.0-or-later
// DSP command tables ported from DOSBox Staging
// Reference: src/hardware/audio/soundblaster.cpp lines 205-265

namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// DSP command length lookup tables for Sound Blaster and Sound Blaster 16.
/// These tables specify how many parameter bytes each DSP command expects.
/// </summary>
public static class DspCommandTables {
    /// <summary>
    /// Number of parameter bytes for DSP commands on SB/SB Pro models.
    /// Index is the command byte (0x00-0xFF), value is the number of parameter bytes.
    /// </summary>
    public static readonly byte[] CommandLengthSb = new byte[256] {
        // 0x00
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x10 - DMA and DAC commands (with Wari hack: 0x15, 0x16, 0x17 have 2 bytes)
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x20 - Direct DAC and recording
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x30
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        
        // 0x40 - Set time constant, block size, etc
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x60
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x70 - ADPCM reference byte
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        
        // 0x80 - Pause DMA
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xA0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xB0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        
        // 0xC0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xD0 - Pause/resume/speaker control
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xE0 - DSP identification and version
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xF0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0
    };
    
    /// <summary>
    /// Number of parameter bytes for DSP commands on SB16 model.
    /// Index is the command byte (0x00-0xFF), value is the number of parameter bytes.
    /// SB16 adds extended commands in the 0xB0-0xCF range (all require 3 parameter bytes).
    /// </summary>
    public static readonly byte[] CommandLengthSb16 = new byte[256] {
        // 0x00
        0, 0, 0, 0,  1, 2, 0, 0,  1, 0, 0, 0,  0, 0, 2, 1,
        // 0x10 - DMA and DAC commands (with Wari hack: 0x15, 0x16, 0x17 have 2 bytes)
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x20 - Direct DAC and recording
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x30
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        
        // 0x40 - Set time constant, block size, etc
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x60
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x70 - ADPCM reference byte
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        
        // 0x80 - Pause DMA
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xA0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xB0 - SB16 extended DMA commands (all require 3 parameters)
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        
        // 0xC0 - SB16 extended DMA commands continued (all require 3 parameters)
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        // 0xD0 - Pause/resume/speaker control
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xE0 - DSP identification and version
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        // 0xF0 - SB16 ASP and misc
        0, 0, 0, 0,  0, 0, 0, 0,  0, 1, 0, 0,  0, 0, 0, 0
    };
    
    /// <summary>
    /// Gets the number of parameter bytes required for a DSP command.
    /// </summary>
    /// <param name="command">DSP command byte</param>
    /// <param name="isSb16">True if Sound Blaster 16, false for earlier models</param>
    /// <returns>Number of parameter bytes expected</returns>
    public static byte GetCommandLength(byte command, bool isSb16) {
        return isSb16 ? CommandLengthSb16[command] : CommandLengthSb[command];
    }
}
