namespace Spice86.Infrastructure;

using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;

/// <inheritdoc cref="IHostStorageProvider" />
public class HostStorageProvider : IHostStorageProvider {
    private readonly IStorageProvider _storageProvider;

    public HostStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    /// <inheritdoc />
    public bool CanPickFolder => _storageProvider.CanPickFolder;

    /// <inheritdoc />
    public bool CanOpen => _storageProvider.CanOpen;

    /// <inheritdoc/>
    public bool CanSave => _storageProvider.CanSave;

    /// <inheritdoc/>
    public async Task<IStorageFolder?> TryGetFolderFromPathAsync(string folderPath) {
        return await _storageProvider.TryGetFolderFromPathAsync(folderPath);
    }

    /// <inheritdoc />
    public async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options) {
        return await _storageProvider.SaveFilePickerAsync(options);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options) {
        return await _storageProvider.OpenFolderPickerAsync(options);
    }

    /// <inheritdoc />
    public async Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder) {
        return await _storageProvider.TryGetWellKnownFolderAsync(wellKnownFolder);
    }

    public async Task SaveBitmapFile(WriteableBitmap bitmap) {
        if (CanSave && CanPickFolder) {
            FilePickerSaveOptions options = new() {
                Title = "Save bitmap image...",
                DefaultExtension = "bmp",
                SuggestedStartLocation = await TryGetWellKnownFolderAsync(WellKnownFolder.Documents)
            };
            string? file = (await SaveFilePickerAsync(options))?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(file)) {
                bitmap.Save(file);
            }
        }
    }
    
    public async Task SaveBinaryFile(byte[] bytes) {
        if (CanSave && CanPickFolder) {
            FilePickerSaveOptions options = new() {
                Title = "Save binary file",
                SuggestedFileName = "dump.bin",
                DefaultExtension = "bin",
                SuggestedStartLocation = await TryGetWellKnownFolderAsync(WellKnownFolder.Documents)
            };
            string? file = (await SaveFilePickerAsync(options))?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(file)) {
                await File.WriteAllBytesAsync(file, bytes);
            }
        }
    }

    public async Task DumpEmulatorStateToFile(Configuration configuration, IProgramExecutor programExecutor) {
        if (CanSave && CanPickFolder) {
            FolderPickerOpenOptions options = new() {
                Title = "Dump emulator state to directory...",
                AllowMultiple = false,
                SuggestedStartLocation = await TryGetFolderFromPathAsync(configuration.RecordedDataDirectory)
            };
            if (!Directory.Exists(configuration.RecordedDataDirectory)) {
                options.SuggestedStartLocation = await TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
            }

            Uri? dir = (await OpenFolderPickerAsync(options)).FirstOrDefault()?.Path;
            if (!string.IsNullOrWhiteSpace(dir?.AbsolutePath)) {
                programExecutor.DumpEmulatorStateToDirectory(dir.AbsolutePath);
            }
        }
    }
}
