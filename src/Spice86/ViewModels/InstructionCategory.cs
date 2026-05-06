namespace Spice86.ViewModels;

/// <summary>
/// Category of an instruction in the disassembly view, used to select the icon column icon.
/// </summary>
public enum InstructionCategory {
    /// <summary>No applicable category; no icon is shown.</summary>
    None,

    /// <summary>DOS interrupt or DOS-related call.</summary>
    Dos,

    /// <summary>BIOS interrupt or hardware-level call.</summary>
    Bios,

    /// <summary>Mouse driver call (INT 33h or BIOS INT 74h).</summary>
    Mouse,

    /// <summary>Sound/music subsystem (OPL FM, Sound Blaster, MPU-401, GUS).</summary>
    Sound,

    /// <summary>Video/graphics subsystem (VGA I/O).</summary>
    Video,

    /// <summary>Expanded or extended memory call (EMS INT 67h, XMS driver).</summary>
    Memory,

    /// <summary>General CPU operation (arithmetic, logic, data movement).</summary>
    Cpu,

    /// <summary>I/O port access (IN/OUT instructions or joystick gameport).</summary>
    IoPort,

    /// <summary>Control flow instruction (CALL, JMP, conditional branch, RET, LOOP, INT).</summary>
    Flow
}
