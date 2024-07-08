namespace Spice86.Infrastructure;

using PropertyModels.Extensions;

using System;
using System.IO;
using System.Timers;

public class FilePoller {
    private readonly string _filePath;
    private ulong _lastHash;
    private bool _fileChanged;
    private readonly Timer _timer;
    private readonly Action _actionToPerform;

    public FilePoller(string filePath, Action actionToPerformOnChange, double interval = 1000) {
        _filePath = filePath;
        _actionToPerform = actionToPerformOnChange;
        _timer = new Timer(interval);
        _timer.Elapsed += CheckFileChange;
        _timer.AutoReset = true;
    }

    public void Start() {
        _timer.Start();
    }

    public void Stop() {
        _timer.Stop();
    }

    private void CheckFileChange(object? sender, ElapsedEventArgs e) {
        if (!File.Exists(_filePath))
            return;

        ulong currentHash = CalculateHash(_filePath);

        if (_lastHash != 0 && _lastHash != currentHash) {
            _fileChanged = !_fileChanged;
        } else if (_lastHash == currentHash && _fileChanged) {
            _fileChanged = false;
            _actionToPerform();
        }

        _lastHash = currentHash;
    }

    private static ulong CalculateHash(string filePath) {
        string fileContents = File.ReadAllText(filePath);

        return fileContents.GetDeterministicHashCode64();
    }
}