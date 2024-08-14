namespace Spice86.Infrastructure;

using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function.Dump;

/// <inheritdoc cref="IHostStorageProvider" />
public class HostStorageProvider : IHostStorageProvider {
    private readonly IStorageProvider _storageProvider;
    private readonly Configuration _configuration;
    private readonly EmulatorStateSerializer _emulatorStateSerializer;

    public HostStorageProvider(IStorageProvider storageProvider, Configuration configuration, EmulatorStateSerializer emulatorStateSerializer) {
        _storageProvider = storageProvider;
        _configuration = configuration;
        _emulatorStateSerializer = emulatorStateSerializer;
    }

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

    public async Task DumpEmulatorStateToFile() {
        if (CanSave && CanPickFolder) {
            FolderPickerOpenOptions options = new() {
                Title = "Dump emulator state to directory...",
                AllowMultiple = false,
                SuggestedStartLocation = await TryGetFolderFromPathAsync(_configuration.RecordedDataDirectory)
            };
            if (!Directory.Exists(_configuration.RecordedDataDirectory)) {
                options.SuggestedStartLocation = await TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
            }
            IReadOnlyList<IStorageFolder> dirs = await OpenFolderPickerAsync(options);
            IStorageFolder? directory = dirs.Count > 0 ? dirs[0] : null;
            Uri? directoryPath = directory?.Path;
            if (!string.IsNullOrWhiteSpace(directoryPath?.AbsolutePath)) {
                _emulatorStateSerializer.SerializeEmulatorStateToDirectory(directoryPath.AbsolutePath);
            }
        }
    }
}

/// <summary>
/// Provides either a file or directory picker UI.
/// </summary>
public interface IHostStorageProvider {
    /// <summary>
    /// Can the folder picker be used on the current platform.
    /// </summary>
    bool CanPickFolder { get; }

    /// <summary>
    /// Can the open file picker be used on the current platform.
    /// </summary>
    bool CanOpen { get; }

    /// <summary>
    /// Returns true if it's possible to open save file picker on the current platform.
    /// </summary>
    bool CanSave { get; }

    /// <summary>
    /// Attempts to read folder from the file-system by its path.
    /// </summary>
    /// <returns>
    /// Folder or null if it doesn't exist.
    /// </returns>
    Task<IStorageFolder?> TryGetFolderFromPathAsync(string folderPath);

    /// <summary>
    /// Opens save file picker dialog.
    /// </summary>
    /// <returns>
    /// Saved Avalonia.Platform.Storage.IStorageFile or null if user canceled the dialog.
    /// </returns>
    Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options);

    /// <summary>
    /// Gets the path to a well known folder.
    /// </summary>
    /// <param name="wellKnownFolder">The well known folder to search for.</param>
    /// <returns>The path to the well known folder, or <c>null</c> if not found.</returns>
    Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder);

    /// <summary>
    /// Opens the folder picker dialog.
    /// </summary>
    /// <param name="options">The folder picker configuration.</param>
    /// <returns>A list of selected folders.</returns>
    Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options);

    /// <summary>
    /// Spawns the file pciker to saves a bitmap to a file.
    /// </summary>
    /// <param name="bitmap">The bitmap to save on the host storage.</param>
    /// <returns>The operation as an awaitable Task.</returns>
    Task SaveBitmapFile(WriteableBitmap bitmap);

    /// <summary>
    /// Spawns the file picker to save the emulator state to a file.
    /// </summary>
    Task DumpEmulatorStateToFile();

    /// <summary>
    /// Spawns the file picker to save a binary file.
    /// </summary>
    /// <param name="bytes">The binary content of the file.</param>
    /// <returns>The operation as an awaitable Task.</returns>
    Task SaveBinaryFile(byte[] bytes);
}
