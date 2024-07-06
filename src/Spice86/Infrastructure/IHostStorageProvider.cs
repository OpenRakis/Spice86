namespace Spice86.Infrastructure;

using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;

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
    /// Spanws the file picker to save the emulator state to a file.
    /// </summary>
    /// <param name="configuration">The emulator configuration, used for RecordedDataDirectory path property.</param>
    /// <param name="programExecutor">The emulated program's executor, to save the emulator dump and other reverse-engineering information.</param>
    Task DumpEmulatorStateToFile(Configuration configuration, IProgramExecutor programExecutor);

    /// <summary>
    /// Spanws the file picker to save a binary file.
    /// </summary>
    /// <param name="bytes">The binary content of the file.</param>
    /// <returns>The operation as an awaitable Task.</returns>
    Task SaveBinaryFile(byte[] bytes);
}
