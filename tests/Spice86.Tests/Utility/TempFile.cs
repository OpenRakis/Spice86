namespace Spice86.Tests.Utility;

public sealed class TempFile : IDisposable {
    public string Path { get; }
    public string Directory { get; }

    public byte[] Data => File.ReadAllBytes(Path);

    public TempFile(string content = "") {
        // Create a unique directory inside the system temp folder
        Directory = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        System.IO.Directory.CreateDirectory(Directory);

        // Create a unique file inside that directory
        Path = System.IO.Path.Join(
            Directory,
            Guid.NewGuid().ToString()
        );

        File.WriteAllText(Path, content);
    }

    public void Dispose() {
        if (System.IO.Directory.Exists(Directory)) {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}



