namespace Spice86.Tests.Utility;

public sealed class TempFile : IDisposable {
    public string Path { get; }

    public TempFile(string prefix) {
        Path = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            $"{prefix}_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Path);
    }

    public string CreateDirectory(params string[] segments) {
        string directoryPath = System.IO.Path.Join([Path, .. segments]);
        System.IO.Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public string CreateFile(string fileName, byte[] content) {
        string filePath = System.IO.Path.Join(Path, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public string CreateTextFile(string fileName, string content) {
        string filePath = System.IO.Path.Join(Path, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public void Dispose() {
        if (System.IO.Directory.Exists(Path)) {
            foreach (string filePath in System.IO.Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)) {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
            System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}