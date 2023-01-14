namespace Spice86.Shared.Video;

public class Ega320x200 : VideoModeBase {
    public Ega320x200() {
        Id = VideoModeIdentifier.Ega320x200;
        Width = 320;
        Height = 200;
        IsPlanar = true;
        PhysicalAddress = 0x000A0000;
    }
}