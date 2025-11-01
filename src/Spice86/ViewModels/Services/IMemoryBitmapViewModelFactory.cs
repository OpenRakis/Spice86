namespace Spice86.ViewModels.Services;

/// <summary>
/// Factory for creating MemoryBitmapViewModel instances with proper dependency injection.
/// </summary>
public interface IMemoryBitmapViewModelFactory {
    /// <summary>
    /// Creates a new instance of MemoryBitmapViewModel.
    /// </summary>
    /// <returns>A new MemoryBitmapViewModel instance.</returns>
    MemoryBitmapViewModel CreateNew();
}
