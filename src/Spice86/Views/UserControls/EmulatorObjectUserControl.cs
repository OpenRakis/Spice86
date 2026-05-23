namespace Spice86.Views.UserControls;

using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels;

/// <summary>
/// Base control that keeps an <see cref="IEmulatorObjectViewModel"/> visibility state in sync with this control.
/// A view-model is considered visible only when the control is attached to the visual tree and visible.
/// </summary>
public abstract class EmulatorObjectUserControl : UserControl {
    private IEmulatorObjectViewModel? _emulatorViewModel;
    private bool _isAttachedToVisualTree;

    /// <summary>
    /// Gets the currently bound emulator-backed view-model when available.
    /// </summary>
    protected IEmulatorObjectViewModel? EmulatorViewModel => _emulatorViewModel;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        SynchronizeEmulatorVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        _isAttachedToVisualTree = false;
        SynchronizeEmulatorVisibility();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        SetEmulatorViewModel(DataContext as IEmulatorObjectViewModel);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) {
            SynchronizeEmulatorVisibility();
        }
    }

    /// <summary>
    /// Called when the typed emulator view-model reference changes.
    /// </summary>
    protected virtual void OnEmulatorViewModelChanged() {
    }

    /// <summary>
    /// Called after base synchronization updates <see cref="IEmulatorObjectViewModel.IsVisible"/>.
    /// </summary>
    protected virtual void OnEmulatorVisibilitySynchronized() {
    }

    /// <summary>
    /// Returns whether this control is effectively visible for refresh purposes.
    /// </summary>
    protected bool IsViewModelEffectivelyVisible => _isAttachedToVisualTree && IsVisible;

    private void SetEmulatorViewModel(IEmulatorObjectViewModel? viewModel) {
        if (ReferenceEquals(_emulatorViewModel, viewModel)) {
            return;
        }

        if (_emulatorViewModel is not null) {
            _emulatorViewModel.IsVisible = false;
        }

        _emulatorViewModel = viewModel;
        OnEmulatorViewModelChanged();
        SynchronizeEmulatorVisibility();
    }

    private void SynchronizeEmulatorVisibility() {
        if (_emulatorViewModel is not null) {
            _emulatorViewModel.IsVisible = IsViewModelEffectivelyVisible;
        }
        OnEmulatorVisibilitySynchronized();
    }
}