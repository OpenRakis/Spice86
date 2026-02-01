namespace Spice86.Core.Emulator.StateSerialization;

public class EmulatorStateSerializationFolder {
    public EmulatorStateSerializationFolder(string folder) {
        Folder = folder;
        CreateIfNotExist(folder);
    }
    private static void CreateIfNotExist(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }
    }

    public string Folder { get; set; }
}