namespace Spice86.Shared.Video;

public class Ega640x200 : VideoModeBase {
    public Ega640x200() {
        Id = VideoModeIdentifier.Ega640x200;
        Width = 640;
        Height = 200;
        PhysicalAddress = 0x000A0000;
    }
}