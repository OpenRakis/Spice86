namespace Spice86.Infrastructure;

using Avalonia.Platform.Storage;

/// <inheritdoc cref="IHostStorageProvider" />
public class HostStorageProvider : IHostStorageProvider {
    private readonly IStorageProvider _storageProvider;

    public HostStorageProvider(IStorageProvider storageProvider) => _storageProvider = storageProvider;

    /// <inheritdoc />
    public bool CanPickFolder => _storageProvider.CanPickFolder;

    /// <inheritdoc />
    public bool CanOpen => _storageProvider.CanOpen;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options) {
        return await _storageProvider.OpenFilePickerAsync(options);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options) {
        return await _storageProvider.OpenFolderPickerAsync(options);
    }

    /// <inheritdoc />
    public async Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder) {
        return await _storageProvider.TryGetWellKnownFolderAsync(wellKnownFolder);
    }
}
