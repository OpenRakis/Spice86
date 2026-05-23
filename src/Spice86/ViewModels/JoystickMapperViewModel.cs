namespace Spice86.ViewModels;

using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System.Collections.ObjectModel;
using System.IO;

/// <summary>
/// ViewModel for the joystick mapper window. Lets the user load,
/// edit and save a joystick mapping JSON file backed by an
/// <see cref="IJoystickMappingStore"/>.
/// </summary>
public sealed partial class JoystickMapperViewModel : ViewModelBase {
    private readonly IJoystickMappingStore _store;
    private readonly IHostStorageProvider _storageProvider;
    private readonly ILoggerService _loggerService;

    [ObservableProperty]
    private ObservableCollection<JoystickProfileEditorViewModel> _profiles = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveProfileCommand))]
    private JoystickProfileEditorViewModel? _selectedProfile;

    [ObservableProperty]
    private string _defaultProfileName = string.Empty;

    [ObservableProperty]
    private int _schemaVersion = 1;

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="JoystickMapperViewModel"/>.
    /// </summary>
    public JoystickMapperViewModel(IJoystickMappingStore store,
        IHostStorageProvider storageProvider,
        ILoggerService loggerService) {
        _store = store;
        _storageProvider = storageProvider;
        _loggerService = loggerService;
        LoadFrom(new JoystickMapping());
    }

    /// <summary>Replaces the current edited mapping with a fresh, empty one.</summary>
    [RelayCommand]
    public void NewMapping() {
        CurrentFilePath = string.Empty;
        LoadFrom(new JoystickMapping());
        StatusMessage = "New empty mapping.";
    }

    /// <summary>Adds a new empty profile to the mapping and selects it.</summary>
    [RelayCommand]
    public void AddProfile() {
        JoystickProfile profile = new() { Name = "New profile" };
        JoystickProfileEditorViewModel editor = new(profile);
        Profiles.Add(editor);
        SelectedProfile = editor;
    }

    /// <summary>Removes the currently selected profile.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    public void RemoveProfile() {
        if (SelectedProfile is null) {
            return;
        }
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
    }

    private bool CanRemoveProfile() => SelectedProfile is not null;

    /// <summary>
    /// Opens a file picker, loads the chosen JSON mapping file and replaces
    /// the current edited document with it.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync() {
        if (!_storageProvider.CanOpen) {
            StatusMessage = "Open file picker is not available on this platform.";
            return;
        }
        FilePickerOpenOptions options = new() {
            Title = "Load joystick mapping...",
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonFileType, AllFilesFileType }
        };
        IReadOnlyList<IStorageFile> files = await _storageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) {
            return;
        }
        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            StatusMessage = "Selected file has no local path.";
            return;
        }
        JoystickMapping? mapping = _store.Load(path);
        if (mapping is null) {
            StatusMessage = $"Failed to load mapping from {path} (see log for details).";
            return;
        }
        CurrentFilePath = path;
        LoadFrom(mapping);
        StatusMessage = $"Loaded mapping from {path}.";
    }

    /// <summary>
    /// Saves the current edited document to <see cref="CurrentFilePath"/>;
    /// falls back to <see cref="SaveAsAsync"/> if no path is known.
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync() {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) {
            await SaveAsAsync();
            return;
        }
        SaveTo(CurrentFilePath);
    }

    /// <summary>
    /// Opens a save file picker and saves the current edited document to
    /// the chosen path, also updating <see cref="CurrentFilePath"/>.
    /// </summary>
    [RelayCommand]
    public async Task SaveAsAsync() {
        if (!_storageProvider.CanSave) {
            StatusMessage = "Save file picker is not available on this platform.";
            return;
        }
        FilePickerSaveOptions options = new() {
            Title = "Save joystick mapping as...",
            DefaultExtension = "json",
            SuggestedFileName = SuggestedFileName(),
            FileTypeChoices = new[] { JsonFileType }
        };
        IStorageFile? file = await _storageProvider.SaveFilePickerAsync(options);
        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        CurrentFilePath = path;
        SaveTo(path);
    }

    private void SaveTo(string path) {
        JoystickMapping mapping = ToMapping();
        try {
            _store.Save(path, mapping);
            StatusMessage = $"Saved mapping to {path}.";
        } catch (IOException ex) {
            StatusMessage = $"Failed to save mapping to {path}: {ex.Message}";
            _loggerService.Warning(ex, "Failed to save joystick mapping to {Path}", path);
        } catch (UnauthorizedAccessException ex) {
            StatusMessage = $"Failed to save mapping to {path}: {ex.Message}";
            _loggerService.Warning(ex, "Failed to save joystick mapping to {Path}", path);
        }
    }

    private string SuggestedFileName() {
        if (!string.IsNullOrWhiteSpace(CurrentFilePath)) {
            return Path.GetFileName(CurrentFilePath);
        }
        return "joystick-mapping.json";
    }

    private void LoadFrom(JoystickMapping mapping) {
        SchemaVersion = mapping.SchemaVersion;
        DefaultProfileName = mapping.DefaultProfileName;
        Profiles = new ObservableCollection<JoystickProfileEditorViewModel>();
        foreach (JoystickProfile profile in mapping.Profiles) {
            Profiles.Add(new JoystickProfileEditorViewModel(profile));
        }
        SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
    }

    /// <summary>
    /// Materializes the current edited state back into a
    /// <see cref="JoystickMapping"/> for serialization.
    /// </summary>
    public JoystickMapping ToMapping() {
        JoystickMapping mapping = new() {
            SchemaVersion = SchemaVersion,
            DefaultProfileName = DefaultProfileName,
        };
        foreach (JoystickProfileEditorViewModel editor in Profiles) {
            mapping.Profiles.Add(editor.ToProfile());
        }
        return mapping;
    }

    private static readonly FilePickerFileType JsonFileType =
        new("JSON files") { Patterns = new[] { "*.json" } };

    private static readonly FilePickerFileType AllFilesFileType =
        new("All files") { Patterns = new[] { "*" } };
}
