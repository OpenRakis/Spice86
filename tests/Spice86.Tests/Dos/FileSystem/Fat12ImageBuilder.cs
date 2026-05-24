namespace Spice86.Tests.Dos.FileSystem;

using Spice86.Shared.Emulator.Storage.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;

internal sealed class Fat12ImageBuilder {
    private readonly List<(string FileName, byte[] Content)> _files = new();

    internal Fat12ImageBuilder WithFile(string fileName, byte[] content) {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        byte[] storedContent = new byte[content.Length];
        Array.Copy(content, storedContent, content.Length);
        _files.Add((fileName, storedContent));
        return this;
    }

    internal byte[] Build() {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"spice86-fat12-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try {
            for (int i = 0; i < _files.Count; i++) {
                (string fileName, byte[] content) file = _files[i];
                string filePath = Path.Combine(tempDirectory, file.fileName);
                string? directoryPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directoryPath)) {
                    Directory.CreateDirectory(directoryPath);
                }
                File.WriteAllBytes(filePath, file.content);
            }

            VirtualFloppyImage image = new(tempDirectory);
            return image.Build();
        } finally {
            Directory.Delete(tempDirectory, true);
        }
    }
}