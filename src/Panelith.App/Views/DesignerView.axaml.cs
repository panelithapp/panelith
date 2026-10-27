using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Panelith.App.ViewModels;

namespace Panelith.App.Views;

public sealed partial class DesignerView : UserControl
{
    private TopLevel? _topLevel;

    public DesignerView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DesignerViewModel designer || e.Handled || !IsEffectivelyVisible)
        {
            return;
        }

        var typing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key is Key.S or Key.Z or Key.Y && typing)
        {
            Focus();
        }
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10d : 1d;
        switch (e.Key)
        {
            case Key.Z when control:
                designer.UndoCommand.Execute(null);
                break;
            case Key.Y when control:
                designer.RedoCommand.Execute(null);
                break;
            case Key.S when control:
                designer.SaveCommand.Execute(null);
                break;
            case Key.D when control && !typing:
                designer.DuplicateCommand.Execute(null);
                break;
            case Key.Delete when !typing:
                designer.DeleteCommand.Execute(null);
                break;
            case Key.Left when !typing:
                designer.Nudge(-step, 0);
                break;
            case Key.Right when !typing:
                designer.Nudge(step, 0);
                break;
            case Key.Up when !typing:
                designer.Nudge(0, -step);
                break;
            case Key.Down when !typing:
                designer.Nudge(0, step);
                break;
            case Key.Escape when !typing:
                designer.Select(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private async void AddImage(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindows() && DataContext is DesignerViewModel designer && TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a picture",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = MediaImportViewModel.PicturePatterns }],
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                designer.AddImage(path);
            }
        }
    }

    private async void SetWallpaper(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DesignerViewModel designer || !OperatingSystem.IsWindows()
            || TopLevel.GetTopLevel(this) is not Window owner || owner.DataContext is not MainViewModel main
            || !designer.EnsureSaved() || designer.SavedId is not { } id)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture or video",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Pictures and videos") { Patterns = [.. MediaImportViewModel.PicturePatterns, .. MediaImportViewModel.VideoPatterns] },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path
            && await MediaImportWindow.ShowAsync(owner, new MediaImportViewModel(main.Screens.Service, path, id)) is { } changed)
        {
            main.Screens.Reload();
            designer.ReloadFromDisk(changed);
        }
    }

    private void ApplySwatch(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color } button)
        {
            return;
        }

        if (button.FindLogicalAncestorOfType<ItemsControl>()?.DataContext is PropertyField field)
        {
            field.UseSwatchCommand.Execute(color);
        }
    }
}
