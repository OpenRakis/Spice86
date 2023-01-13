namespace Spice86.Shared.Video;

public class Vga320x200 : VideoModeBase {
    public Vga320x200() {
        Id = 0x13;
        Width = 320;
        Height = 200;
        IsPlanar = true;
        PhysicalAddress = 0x000A0000;
    }
}